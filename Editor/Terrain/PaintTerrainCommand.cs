
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using System.Threading;
using Unity.Mathematics;
using System;

namespace MrPathV2
{
    /// <summary>
    /// 地形纹理绘制命令 - 优化版
    /// 提供高性能的地形纹理绘制功能，支持异步处理和资源管理优化
    /// </summary>
    public class PaintTerrainCommand : TerrainCommandBase
    {
        #region 常量定义

        private const int DEFAULT_BATCH_SIZE = 256;
        private const string OPERATION_NAME = "绘制纹理 (Paint Textures)";

        #endregion

        #region 构造函数

        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider)
        {
        }

        public override string GetCommandName() => OPERATION_NAME;

        #endregion

        #region 核心处理方法

        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            // 使用结构化的资源管理器
            TerrainPaintResourceManager resourceManager = null;

            try
            {
                resourceManager = new TerrainPaintResourceManager();
                
                // 初始化共享数据
                await InitializeSharedDataAsync(resourceManager, spine, token);

                // 处理每个地形
                await ProcessTerrainsInBatchAsync(resourceManager, terrains, token);

                // 应用结果并清理
                await ApplyResultsAsync(resourceManager, token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[MrPath] 用户取消了{GetCommandName()}操作");
                throw;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MrPath] {GetCommandName()}执行失败: {ex.Message}");
                throw;
            }
            finally
            {
                // 确保资源管理器被正确释放
                resourceManager?.Dispose();
                
                // 标记高度提供器需要更新
                HeightProvider?.MarkAsDirty();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化共享数据（轮廓、脊线等）
        /// </summary>
        private async Task InitializeSharedDataAsync(TerrainPaintResourceManager resourceManager, PathSpine spine, CancellationToken token)
        {
            // 在主线程中执行，避免跨线程内存分配问题
            await Task.Yield(); // 让出控制权，但保持在主线程

            token.ThrowIfCancellationRequested();

            // 直接分配NativeArray，不再使用对象池
            // 生成道路轮廓 - 必须在主线程中执行
            RoadContourGenerator.GenerateContour(spine, Creator.profile,
                out var roadContour, out var contourBounds, Allocator.Persistent);

            resourceManager.SetRoadContour(roadContour, contourBounds);

            // 处理包围盒优先级
            var finalBounds = DetermineFinalBounds(contourBounds, roadContour, spine);
            resourceManager.SetContourBounds(finalBounds);

            // 创建脊线数据
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
            resourceManager.SetSpineData(spineData);
            
        }

        /// <summary>
        /// 确定最终使用的包围盒
        /// </summary>
        private float4 DetermineFinalBounds(float4 contourBounds, NativeArray<float2> roadContour, PathSpine spine)
        {
            // 优先使用外部提供的预览包围盒
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                return new float4(pb.x, pb.y, pb.z, pb.w);
            }

            // 如果轮廓无效，使用脊线+Profile的AABB
            if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fallback = GetExpandedXZBounds(spine, Creator.profile);
                return new float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }

            return contourBounds;
        }

        /// <summary>
        /// 批量处理地形
        /// </summary>
        private async Task ProcessTerrainsInBatchAsync(TerrainPaintResourceManager resourceManager,
            List<Terrain> terrains, CancellationToken token)
        {
            var validTerrains = new List<TerrainWorkItem>();

            // 预处理：验证地形并准备工作项
            foreach (var terrain in terrains)
            {
                token.ThrowIfCancellationRequested();

                var workItem = await PrepareTerrainWorkItemAsync(terrain, token);
                if (workItem != null)
                {
                    validTerrains.Add(workItem);
                    resourceManager.AddWorkItem(workItem);
                }
            }

            if (validTerrains.Count == 0)
            {
                Debug.LogWarning("[MrPath] 没有有效的地形需要处理");
                return;
            }

            // 调度并行作业
            await ScheduleAndExecuteJobsAsync(resourceManager, validTerrains, token);
        }

        /// <summary>
        /// 准备单个地形的工作项
        /// </summary>
        // 在 PaintTerrainCommand.cs 中，替换此方法

        private async Task<TerrainWorkItem> PrepareTerrainWorkItemAsync(Terrain terrain, CancellationToken token)
        {
            // 在主线程中执行，避免跨线程内存分配问题
            await Task.Yield(); // 让出控制权，但保持在主线程

            token.ThrowIfCancellationRequested();

            // 注册撤销操作
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());

            var td = terrain.terrainData;
            var recipeAsset = Creator.profile.roadRecipe;

            // 验证配方
            if (recipeAsset == null)
            {
                Debug.LogWarning($"[MrPath] 地形 \"{terrain.name}\" 未配置道路配方，跳过处理");
                return null;
            }

            // 创建并返回工作项
            return CreateTerrainWorkItem(terrain, recipeAsset);
        }

        /// <summary>
        /// 为单个地形创建工作项
        /// </summary>
        private TerrainWorkItem CreateTerrainWorkItem(Terrain terrain, StylizedRoadRecipe recipeAsset)
        {
            var td = terrain.terrainData;
            if (td == null)
            {
                Debug.LogWarning($"[MrPath] 地形 \"{terrain.name}\" 缺少 TerrainData，跳过处理");
                return null;
            }

            // 解析图层映射
            var layerMap = LayerResolver.Resolve(terrain, recipeAsset);
            if (td.alphamapLayers == 0)
            {
                Debug.LogWarning($"[MrPath] 地形 \"{terrain.name}\" 没有有效的地形图层，跳过处理");
                return null;
            }

            // 准备Alpha贴图数据 - 对于大型地形使用分块处理
            var alphamaps3D = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
            
            // 检查数组大小，如果超过池限制则使用直接分配
            int totalSize = alphamaps3D.Length;
            
            NativeArray<float> alphamaps1D;
            
            // 统一使用直接分配，不再区分大小
            alphamaps1D = new NativeArray<float>(totalSize, Allocator.Persistent);

            // 高效的数据转换
            ConvertAlphamaps3DTo1D(alphamaps3D, alphamaps1D);

            // --- 【核心修改】 ---
            // 1. 从 Profile 中获取道路的真实世界宽度
            //    (注意：这里假设宽度属性名为 roadWidth，请根据您的实际 Profile 类进行调整)
            float roadWorldWidth = Creator.profile.roadWidth;

            // 2. 将世界宽度传递给 RecipeData 构造函数
            var profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
            var recipeData = new RecipeData(recipeAsset, layerMap, roadWorldWidth, Allocator.Persistent);
            // --- 【修改结束】 ---

            return new TerrainWorkItem
            {
                Terrain = terrain,
                TerrainData = td,
                Alphamaps3D = alphamaps3D,
                Alphamaps1D = alphamaps1D,
                ProfileData = profileData,
                RecipeData = recipeData,
                LayerMap = layerMap,
               
                UseDirectAllocation = true // 统一使用直接分配
            };
        }

        /// <summary>
        /// 调度并执行并行作业
        /// </summary>
        private async Task ScheduleAndExecuteJobsAsync(TerrainPaintResourceManager resourceManager,
            List<TerrainWorkItem> workItems, CancellationToken token)
        {
            var jobHandles = new NativeList<JobHandle>(workItems.Count, Allocator.TempJob);

            try
            {
                // 为每个地形创建并调度作业
                foreach (var workItem in workItems)
                {
                    token.ThrowIfCancellationRequested();

                    var job = CreatePaintJob(resourceManager, workItem);
                    
                    // 使用行级并行处理而不是像素级处理
                    var handle = job.Schedule(
                        workItem.TerrainData.alphamapResolution, // 按行并行处理
                        DEFAULT_BATCH_SIZE);

                    jobHandles.Add(handle);
                }

                // 等待所有作业完成
                var combinedHandle = JobHandle.CombineDependencies(jobHandles.AsArray());
                await WaitForJobCompletionAsync(combinedHandle, token);
            }
            finally
            {
                if (jobHandles.IsCreated)
                {
                    jobHandles.Dispose();
                }
            }
        }

        /// <summary>
        /// 创建绘制作业
        /// </summary>
        private PaintSplatmapJob CreatePaintJob(TerrainPaintResourceManager resourceManager, TerrainWorkItem workItem)
        {
            // 计算覆盖区域
            var (useCoverageLimit, coverageMin, coverageMax) = CalculateCoverageArea(workItem, resourceManager.ContourBounds);
            
            return new PaintSplatmapJob
            {
                spine = resourceManager.SpineData,
                profile = workItem.ProfileData,
                recipe = workItem.RecipeData,
                terrainPos = workItem.Terrain.GetPosition(),
                terrainSize = workItem.TerrainData.size,
                alphamapResolution = workItem.TerrainData.alphamapResolution,
                alphamapLayerCount = workItem.TerrainData.alphamapLayers,
                alphamaps = workItem.Alphamaps1D,
                roadContour = resourceManager.RoadContour,
                contourBounds = resourceManager.ContourBounds,
                // 新增：覆盖区域限制
                useCoverageLimit = useCoverageLimit,
                coverageMin = coverageMin,
                coverageMax = coverageMax
            };
        }

        /// <summary>
        /// 计算地形的覆盖区域像素范围
        /// </summary>
        private (bool useCoverageLimit, int2 coverageMin, int2 coverageMax) CalculateCoverageArea(TerrainWorkItem workItem, float4 contourBounds)
        {
            // 如果没有预览边界，使用轮廓边界
            float4 bounds = contourBounds;
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                bounds = new float4(pb.x, pb.y, pb.z, pb.w);
            }

            var terrainPos = workItem.Terrain.GetPosition();
            var terrainSize = workItem.TerrainData.size;
            var resolution = workItem.TerrainData.alphamapResolution;

            // 计算地形在世界坐标中的范围
            float terrainMinX = terrainPos.x;
            float terrainMinZ = terrainPos.z;
            float terrainMaxX = terrainPos.x + terrainSize.x;
            float terrainMaxZ = terrainPos.z + terrainSize.z;

            // 计算覆盖边界与地形的交集
            float intersectMinX = Mathf.Max(bounds.x, terrainMinX);
            float intersectMinZ = Mathf.Max(bounds.y, terrainMinZ);
            float intersectMaxX = Mathf.Min(bounds.z, terrainMaxX);
            float intersectMaxZ = Mathf.Min(bounds.w, terrainMaxZ);

            // 检查是否有有效交集
            if (intersectMinX >= intersectMaxX || intersectMinZ >= intersectMaxZ)
            {
                // 没有交集，不处理任何像素
                return (true, new int2(resolution, resolution), new int2(-1, -1));
            }

            // 转换为像素坐标
            float invTerrainSizeX = 1f / terrainSize.x;
            float invTerrainSizeZ = 1f / terrainSize.z;

            int pixelMinX = Mathf.FloorToInt((intersectMinX - terrainMinX) * invTerrainSizeX * (resolution - 1));
            int pixelMinZ = Mathf.FloorToInt((intersectMinZ - terrainMinZ) * invTerrainSizeZ * (resolution - 1));
            int pixelMaxX = Mathf.CeilToInt((intersectMaxX - terrainMinX) * invTerrainSizeX * (resolution - 1));
            int pixelMaxZ = Mathf.CeilToInt((intersectMaxZ - terrainMinZ) * invTerrainSizeZ * (resolution - 1));

            // 确保像素坐标在有效范围内
            pixelMinX = Mathf.Clamp(pixelMinX, 0, resolution - 1);
            pixelMinZ = Mathf.Clamp(pixelMinZ, 0, resolution - 1);
            pixelMaxX = Mathf.Clamp(pixelMaxX, 0, resolution - 1);
            pixelMaxZ = Mathf.Clamp(pixelMaxZ, 0, resolution - 1);

            // 计算覆盖面积比例，如果覆盖面积小于50%则启用限制
            float totalPixels = resolution * resolution;
            float coveredPixels = (pixelMaxX - pixelMinX + 1) * (pixelMaxZ - pixelMinZ + 1);
            bool useCoverageLimit = coveredPixels < totalPixels * 0.5f;

            return (useCoverageLimit, new int2(pixelMinX, pixelMinZ), new int2(pixelMaxX, pixelMaxZ));
        }

        /// <summary>
        /// 等待作业完成（支持取消）
        /// </summary>
        private async Task WaitForJobCompletionAsync(JobHandle jobHandle, CancellationToken token)
        {
            while (!jobHandle.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            jobHandle.Complete();
        }

        /// <summary>
        /// 应用处理结果
        /// </summary>
        private async Task ApplyResultsAsync(TerrainPaintResourceManager resourceManager, CancellationToken token)
        {
            // 在主线程中执行，避免跨线程访问Unity对象
            await Task.Yield(); // 让出控制权，但保持在主线程

            foreach (var workItem in resourceManager.WorkItems)
            {
                token.ThrowIfCancellationRequested();

                // 转换数据格式并应用到地形
                ConvertAlphamaps1DTo3D(workItem.Alphamaps1D, workItem.Alphamaps3D);
                workItem.TerrainData.SetAlphamaps(0, 0, workItem.Alphamaps3D);
            }
        }

        #endregion

        #region 数据转换方法

        /// <summary>
        /// 高效的3D到1D Alpha贴图转换
        /// </summary>
        private static void ConvertAlphamaps3DTo1D(float[,,] source, NativeArray<float> destination)
        {
            int height = source.GetLength(0);
            int width = source.GetLength(1);
            int depth = source.GetLength(2);

            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        destination[index++] = source[y, x, z];
                    }
                }
            }
        }

        /// <summary>
        /// 高效的1D到3D Alpha贴图转换
        /// </summary>
        private static void ConvertAlphamaps1DTo3D(NativeArray<float> source, float[,,] destination)
        {
            int height = destination.GetLength(0);
            int width = destination.GetLength(1);
            int depth = destination.GetLength(2);

            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        destination[y, x, z] = source[index++];
                    }
                }
            }
        }

        #endregion
    }

    #region 辅助类和结构

    /// <summary>
    /// 地形工作项，包含单个地形处理所需的所有数据
    /// </summary>
    internal class TerrainWorkItem
    {
        public Terrain Terrain { get; set; }
        public TerrainData TerrainData { get; set; }
        public float[,,] Alphamaps3D { get; set; }
        public NativeArray<float> Alphamaps1D { get; set; }
        public PathJobsUtility.ProfileData ProfileData { get; set; }
        public RecipeData RecipeData { get; set; }
        public Dictionary<TerrainLayer, int> LayerMap { get; set; }
        // ArrayPool字段已移除，不再使用对象池
        public bool UseDirectAllocation { get; set; } // 标记是否使用直接分配
    }

    /// <summary>
    /// 地形绘制资源管理器，负责统一管理所有Native资源
    /// </summary>
    internal class TerrainPaintResourceManager : System.IDisposable
    {
        private readonly List<TerrainWorkItem> _workItems = new List<TerrainWorkItem>();
        private readonly List<PathJobsUtility.ProfileData> _profileDataList = new List<PathJobsUtility.ProfileData>();
        private readonly List<RecipeData> _recipeDataList = new List<RecipeData>();
        // _arrayPools字段已移除，不再使用对象池

        public NativeArray<float2> RoadContour { get; private set; }
        public float4 ContourBounds { get; private set; }
        public PathJobsUtility.SpineData SpineData { get; private set; }
        public IReadOnlyList<TerrainWorkItem> WorkItems => _workItems;

        public void SetRoadContour(NativeArray<float2> roadContour, float4 bounds)
        {
            RoadContour = roadContour;
            ContourBounds = bounds;
        }

        public void SetContourBounds(float4 bounds)
        {
            ContourBounds = bounds;
        }

        public void SetSpineData(PathJobsUtility.SpineData spineData)
        {
            SpineData = spineData;
        }

        // SetArrayPool方法已移除，不再使用对象池

        public void AddWorkItem(TerrainWorkItem workItem)
        {
            _workItems.Add(workItem);
            _profileDataList.Add(workItem.ProfileData);
            _recipeDataList.Add(workItem.RecipeData);
        }

        public void Dispose()
        {
            // 清理工作项中的Native数组 - 统一直接释放
            foreach (var workItem in _workItems)
            {
                if (workItem.Alphamaps1D.IsCreated)
                {
                    workItem.Alphamaps1D.Dispose();
                }
            }

            // 清理配置数据
            foreach (var profileData in _profileDataList)
            {
                if (profileData.IsCreated)
                {
                    profileData.Dispose();
                }
            }

            foreach (var recipeData in _recipeDataList)
            {
                recipeData.Dispose();
            }

            // 清理共享数据
            if (RoadContour.IsCreated)
            {
                RoadContour.Dispose();
            }

            if (SpineData.IsCreated)
            {
                SpineData.Dispose();
            }

            _workItems.Clear();
            _profileDataList.Clear();
            _recipeDataList.Clear();
        }
    }

    #endregion
}