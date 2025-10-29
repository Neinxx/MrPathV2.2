// 文件: Editor/Terrain/PaintTerrainCommand.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Editor.Settings;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Jobs;
using MrPathV2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
// <-- 修正：添加 using

// 确保 Painter 命名空间可访问
namespace MrPathV2.Editor.Terrain
{
    public class PaintTerrainCommand : TerrainCommandBase
    {
        #region 常量与枚举

        private const string OperationName = "绘制纹理 (Paint Textures)";

        public enum PaintingBackend
        {
            CPUJobTwoPass,
            GPUCompute
        }

        #endregion

        #region 构造函数

        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider) { }

        public override string GetCommandName() => OperationName;

        #endregion

        #region 核心处理方法

        /// <summary>
        ///     处理地形绘制，采用提前返回风格和单一职责原则
        /// </summary>
        protected override async Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            // 提前返回：检查地形列表有效性
            if (terrains == null || terrains.Count == 0)
            {
                Debug.LogWarning("[PaintTerrainCommand] No terrains to process.");
                return;
            }

            SharedData sharedData = default;

            try
            {
                // 选择绘制后端
                var backend = SelectPaintingBackend();

                // 准备共享数据
                sharedData = await PrepareSharedDataAsync(spine, token);
                if (!sharedData.IsValid)
                {
                    Debug.LogError("[PaintTerrainCommand] Failed to prepare shared data.");
                    return;
                }

                // 准备后端特定数据
                var backendData = await PrepareBackendDataAsync(backend, terrains, token);

                // 处理所有地形
                await ProcessAllTerrainsAsync(backend, terrains, sharedData, backendData, token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[PaintTerrainCommand] Operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PaintTerrainCommand] Error during processing: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                // 清理共享资源
                CleanupSharedResources(ref sharedData);

                // 标记高度提供器为脏
                HeightProvider?.MarkAsDirty();
            }
        }

        /// <summary>
        ///     清理共享资源
        /// </summary>
        private static void CleanupSharedResources(ref SharedData sharedData)
        {
            sharedData.RoadContour.SafeDispose();
            if (sharedData.SpineData.IsCreated) sharedData.SpineData.Dispose();
            if (sharedData.ProfileData.IsCreated) sharedData.ProfileData.Dispose();
        }

        /// <summary>
        ///     选择绘制后端
        /// </summary>
        private static PaintingBackend SelectPaintingBackend()
        {
            var backend = PaintingBackend.CPUJobTwoPass; // 默认
            var projectSettings = MrPathProjectSettings.GetOrCreateSettings();

            // 检查高级设置
            if (projectSettings?.advancedSettings != null)
            {
                backend = projectSettings.advancedSettings.paintingBackend;
            }
            else
            {
                Debug.LogWarning("[PaintTerrainCommand] Could not find AdvancedSettings. Defaulting to CPU backend.");
            }

            // 检查GPU支持
            if (backend == PaintingBackend.GPUCompute && !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[PaintTerrainCommand] Compute Shaders not supported. Falling back to CPU Job backend.");
                backend = PaintingBackend.CPUJobTwoPass;
            }

            Debug.Log($"[PaintTerrainCommand] Using backend: {backend}");
            return backend;
        }

        /// <summary>
        ///     准备共享数据
        /// </summary>
        private async Task<SharedData> PrepareSharedDataAsync(PathSpine spine, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();

            // 生成道路轮廓
            RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
            var finalBounds = DetermineFinalBounds(contourBounds, roadContour, spine);

            // 创建脊柱和剖面数据
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
            var profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);

            token.ThrowIfCancellationRequested();

            return new SharedData
            {
                RoadContour = roadContour,
                FinalBounds = finalBounds,
                SpineData = spineData,
                ProfileData = profileData,
                IsValid = roadContour.IsCreated && spineData.IsCreated && profileData.IsCreated
            };
        }

        /// <summary>
        ///     准备后端特定数据
        /// </summary>
        private async Task<BackendData> PrepareBackendDataAsync(PaintingBackend backend, List<UnityEngine.Terrain> terrains, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();

            var backendData = new BackendData();

            if (backend == PaintingBackend.CPUJobTwoPass)
            {
                backendData.CpuRecipeDataMap = await PrepareCpuRecipeDataAsync(terrains, token);
            }
            // GPU后端不需要提前准备数据

            return backendData;
        }

        /// <summary>
        ///     准备CPU配方数据
        /// </summary>
        private async Task<Dictionary<UnityEngine.Terrain, RecipeData>> PrepareCpuRecipeDataAsync(List<UnityEngine.Terrain> terrains, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();

            var cpuRecipeDataMap = new Dictionary<UnityEngine.Terrain, RecipeData>();

            foreach (var terrain in terrains)
            {
                // 提前返回：检查地形有效性
                if (!IsTerrainValidForPainting(terrain))
                {
                    continue;
                }

                var layerMap = LayerResolver.Resolve(terrain, Creator.profile.roadRecipe);
                var roadWorldWidth = Creator.profile.roadWidth;
                var roadWorldLength = Creator.GetPathLength();

                var recipeData = new RecipeData(Creator.profile.roadRecipe, layerMap, roadWorldWidth, roadWorldLength, Allocator.Persistent);

                if (!recipeData.IsCreated)
                {
                    Debug.LogError($"[PaintTerrainCommand] Failed to create CPU RecipeData for terrain {terrain.name}");
                    continue;
                }

                cpuRecipeDataMap.Add(terrain, recipeData);
            }

            return cpuRecipeDataMap;
        }

        /// <summary>
        ///     处理所有地形
        /// </summary>
        private async Task ProcessAllTerrainsAsync(
            PaintingBackend backend,
            List<UnityEngine.Terrain> terrains,
            SharedData sharedData,
            BackendData backendData,
            CancellationToken token)
        {
            var tasks = new List<Task>();

            foreach (var terrain in terrains)
            {
                token.ThrowIfCancellationRequested();

                // 提前返回：检查地形有效性
                if (!IsTerrainValidForPainting(terrain))
                {
                    continue;
                }

                // 计算覆盖区域
                var (_, coverageMin, coverageMax) = CalculateCoverageArea(terrain, sharedData.FinalBounds);

                // 提前返回：检查覆盖区域有效性
                if (!IsCoverageAreaValid(coverageMin, coverageMax))
                {
                    continue;
                }

                // 自适应后端选择：根据覆盖区域大小和设置选择最佳后端
                var selectedBackend = SelectBackendForTerrain(backend, terrain, coverageMin, coverageMax);

                // 创建绘制器任务
                var task = CreatePainterTask(selectedBackend, terrain, sharedData, backendData, coverageMin, coverageMax, token);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        ///     检查地形是否适合绘制
        /// </summary>
        private bool IsTerrainValidForPainting(UnityEngine.Terrain terrain)
        {
            return terrain != null &&
                   terrain.terrainData != null &&
                   terrain.terrainData.alphamapLayers > 0 &&
                   Creator.profile?.roadRecipe != null;
        }

        /// <summary>
        ///     检查覆盖区域是否有效
        /// </summary>
        private static bool IsCoverageAreaValid(Vector2Int coverageMin, Vector2Int coverageMax)
        {
            var numPixelsX = coverageMax.x - coverageMin.x + 1;
            var numPixelsY = coverageMax.y - coverageMin.y + 1;
            return numPixelsX > 0 && numPixelsY > 0;
        }

        /// <summary>
        ///     创建绘制器任务
        /// </summary>
        private Task CreatePainterTask(
            PaintingBackend backend,
            UnityEngine.Terrain terrain,
            SharedData sharedData,
            BackendData backendData,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            ITerrainPainter painter;
            RecipeData cpuRecipeData = default;
            RecipeGpuDataManager gpuDataManager = null;

            if (backend == PaintingBackend.CPUJobTwoPass)
            {
                // 获取CPU配方数据
                if (backendData.CpuRecipeDataMap == null || !backendData.CpuRecipeDataMap.TryGetValue(terrain, out cpuRecipeData) || !cpuRecipeData.IsCreated)
                {
                    Debug.LogWarning($"[PaintTerrainCommand] Skipping terrain {terrain.name} due to missing CPU RecipeData.");
                    return Task.CompletedTask;
                }
                painter = new CpuTerrainPainter();
            }
            else // GPU_Compute
            {
                // 为每个地形创建独立的GPU数据管理器
                gpuDataManager = new RecipeGpuDataManager();
                if (Creator.profile?.roadRecipe != null)
                {
                    var terrainLayerMap = LayerResolver.ResolveEnsurePresent(terrain, Creator.profile.roadRecipe);
                    gpuDataManager.UpdateData(Creator.profile.roadRecipe, terrainLayerMap);
                }
                painter = new GpuTerrainPainter();
            }

            return ExecutePainterAsync(painter, terrain, sharedData.SpineData, sharedData.ProfileData,
                cpuRecipeData, gpuDataManager, sharedData.RoadContour, sharedData.FinalBounds,
                coverageMin, coverageMax, token);
        }

        /// <summary>
        ///     共享数据结构
        /// </summary>
        private struct SharedData
        {
            public NativeArray<float2> RoadContour;
            public float4 FinalBounds;
            public PathJobsUtility.SpineData SpineData;
            public PathJobsUtility.ProfileData ProfileData;
            public bool IsValid;
        }

        /// <summary>
        ///     后端特定数据结构
        /// </summary>
        private struct BackendData
        {
            public Dictionary<UnityEngine.Terrain, RecipeData> CpuRecipeDataMap;
        }

        private static async Task ExecutePainterAsync(
            ITerrainPainter painter, UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData, PathJobsUtility.ProfileData profileData,
            RecipeData cpuRecipeData, RecipeGpuDataManager gpuDataManager,
            NativeArray<float2> roadContour, float4 finalBounds,
            Vector2Int coverageMin, Vector2Int coverageMax, CancellationToken token)
        {
            try
            {
                await painter.ExecuteAsync(terrain, spineData, profileData, cpuRecipeData, gpuDataManager, roadContour, finalBounds, coverageMin, coverageMax, token);
            }
            finally
            {
                painter?.Dispose();
                // CPU RecipeData 是为这个特定任务创建的，在这里释放
                if (cpuRecipeData.IsCreated && painter is CpuTerrainPainter)
                {
                    cpuRecipeData.Dispose();
                }
                // GPU 数据管理器按地形单独创建，在此释放避免共享资源冲突
                gpuDataManager?.Dispose();
            }
        }

        private static (bool useCoverageLimit, Vector2Int coverageMin, Vector2Int coverageMax) CalculateCoverageArea(UnityEngine.Terrain terrain, float4 finalBounds)
        {
            var td = terrain.terrainData;
            if (td == null) return (true, new Vector2Int(0, 0), new Vector2Int(-1, -1));

            var bounds = finalBounds;

            var terrainPos = terrain.GetPosition();
            var terrainSize = td.size;
            var resolution = td.alphamapResolution;

            float terrainMinX = terrainPos.x, terrainMinZ = terrainPos.z;
            float terrainMaxX = terrainPos.x + terrainSize.x, terrainMaxZ = terrainPos.z + terrainSize.z;
            float intersectMinX = Mathf.Max(bounds.x, terrainMinX), intersectMinZ = Mathf.Max(bounds.y, terrainMinZ);
            float intersectMaxX = Mathf.Min(bounds.z, terrainMaxX), intersectMaxZ = Mathf.Min(bounds.w, terrainMaxZ);

            if (intersectMinX >= intersectMaxX || intersectMinZ >= intersectMaxZ)
            {
                return (true, new Vector2Int(0, 0), new Vector2Int(-1, -1));
            }

            float invSizeX = 1f / terrainSize.x, invSizeZ = 1f / terrainSize.z;
            var pixelMinX = Mathf.FloorToInt((intersectMinX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMinZ = Mathf.FloorToInt((intersectMinZ - terrainMinZ) * invSizeZ * (resolution - 1));
            var pixelMaxX = Mathf.CeilToInt((intersectMaxX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMaxZ = Mathf.CeilToInt((intersectMaxZ - terrainMinZ) * invSizeZ * (resolution - 1));

            pixelMinX = Mathf.Clamp(pixelMinX, 0, resolution - 1);
            pixelMinZ = Mathf.Clamp(pixelMinZ, 0, resolution - 1);
            pixelMaxX = Mathf.Clamp(pixelMaxX, 0, resolution - 1);
            pixelMaxZ = Mathf.Clamp(pixelMaxZ, 0, resolution - 1);

            return (true, new   (pixelMinX, pixelMinZ), new (pixelMaxX, pixelMaxZ));
        }


        private float4 DetermineFinalBounds(float4 contourBounds, NativeArray<float2> roadContour, PathSpine spine)
        {
            if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fallback = GetExpandedXZBounds(spine, Creator.profile);
                return new float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }

            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                var minX = Mathf.Min(contourBounds.x, pb.x);
                var minZ = Mathf.Min(contourBounds.y, pb.y);
                var maxX = Mathf.Max(contourBounds.z, pb.z);
                var maxZ = Mathf.Max(contourBounds.w, pb.w);
                return new float4(minX, minZ, maxX, maxZ);
            }

            return contourBounds;
        }

        private static PaintingBackend SelectBackendForTerrain(PaintingBackend userSelectedBackend, UnityEngine.Terrain terrain, Vector2Int coverageMin, Vector2Int coverageMax)
        {
            var adv = MrPathProjectSettings.GetOrCreateSettings()?.advancedSettings;
            // 如果用户强制选择 GPU，则直接使用 GPU（若支持）。
            if (userSelectedBackend == PaintingBackend.GPUCompute)
            {
                return SystemInfo.supportsComputeShaders ? PaintingBackend.GPUCompute : PaintingBackend.CPUJobTwoPass;
            }

            // 如果禁用自动切换或阈值为 0，则保持用户选择（默认 CPU）。
            var threshold = adv?.gpuAutoSwitchThreshold ?? 0;
            if (threshold <= 0)
            {
                return userSelectedBackend;
            }

            // 计算覆盖区域像素数
            int width = Mathf.Max(0, coverageMax.x - coverageMin.x + 1);
            int height = Mathf.Max(0, coverageMax.y - coverageMin.y + 1);
            int areaPixels = width * height;

            // 动态阈值：不超过整张纹理分辨率的 10%，但至少为设置的阈值
            var td = terrain.terrainData;
            int terrainPixels = td.alphamapWidth * td.alphamapHeight;
            int dynamicMax = Mathf.Max(threshold, Mathf.FloorToInt(terrainPixels * 0.10f));

            if (SystemInfo.supportsComputeShaders && areaPixels >= dynamicMax)
            {
                return PaintingBackend.GPUCompute;
            }

            return PaintingBackend.CPUJobTwoPass;
        }

        #endregion


    }
}
