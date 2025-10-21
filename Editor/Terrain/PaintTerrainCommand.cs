// 文件: Editor/Terrain/PaintTerrainCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2._2.Editor.Settings;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Jobs;
using __temp.MrPathV2._2.Runtime.Jobs.Extensions;
using __temp.MrPathV2._2.Runtime.Settings; // <-- 修正：添加 using
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// 确保 Painter 命名空间可访问
namespace __temp.MrPathV2._2.Editor.Terrain
{
    public class PaintTerrainCommand : TerrainCommandBase
    {
        #region 常量与枚举
        private const string OperationName = "绘制纹理 (Paint Textures)";
        public enum PaintingBackend { CPU_Job_TwoPass, GPU_Compute }
        #endregion

        #region 构造函数
        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider) { }

        public override string GetCommandName() => OperationName;
        #endregion

        #region 核心处理方法
        protected override async Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            // --- 1. 选择后端 ---
            PaintingBackend backend = PaintingBackend.CPU_Job_TwoPass; // 默认
            var projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            
            // --- 修正：检查 advancedSettings 是否存在 ---
            if (projectSettings != null && projectSettings.advancedSettings != null)
            {
                backend = projectSettings.advancedSettings.paintingBackend;
            }
            else
            {
                Debug.LogWarning("[PaintTerrainCommand] Could not find AdvancedSettings. Defaulting to CPU backend.");
            }
            // ------------------------------------

            if (backend == PaintingBackend.GPU_Compute && !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[PaintTerrainCommand] Compute Shaders not supported. Falling back to CPU Job backend.");
                backend = PaintingBackend.CPU_Job_TwoPass;
            }
            Debug.Log($"[PaintTerrainCommand] Using backend: {backend}");

            // --- 2. 资源管理器 ---
            RecipeGpuDataManager gpuDataManager = null;
            Dictionary<UnityEngine.Terrain, RecipeData> cpuRecipeDataMap = null;

            // --- 3. 共享数据 (Persistent) ---
            PathJobsUtility.SpineData spineData = default;
            PathJobsUtility.ProfileData profileData = default;
            NativeArray<float2> roadContour = default;
            float4 finalBounds = default;

            try
            {
                // --- 4. 准备共享数据 (主线程) ---
                await Task.Yield();
                token.ThrowIfCancellationRequested();

                RoadContourGenerator.GenerateContour(spine, Creator.profile, out roadContour, out var contourBounds, Allocator.Persistent);
                finalBounds = DetermineFinalBounds(contourBounds, roadContour, spine);
                spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
                profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
                
                token.ThrowIfCancellationRequested();

                // --- 5. 准备后端特定数据 ---
                if (backend == PaintingBackend.GPU_Compute)
                {
                    gpuDataManager = new RecipeGpuDataManager();
                    var firstTerrain = terrains.FirstOrDefault(t => t != null && t.terrainData != null);
                    if (firstTerrain != null && Creator.profile.roadRecipe != null)
                    {
                        var layerMapForGpu = LayerResolver.Resolve(firstTerrain, Creator.profile.roadRecipe);
                        gpuDataManager.UpdateData(Creator.profile.roadRecipe, layerMapForGpu);
                    }
                }
                else // CPU_Job_TwoPass
                {
                     cpuRecipeDataMap = new Dictionary<UnityEngine.Terrain, RecipeData>();
                     foreach(var terrain in terrains) 
                     {
                          if (terrain == null || terrain.terrainData == null || Creator.profile.roadRecipe == null) continue;
                          var layerMap = LayerResolver.Resolve(terrain, Creator.profile.roadRecipe);
                          float roadWorldWidth = Creator.profile.roadWidth;
                          float roadWorldLength = Creator.GetPathLength();
                          var recipeData = new RecipeData(Creator.profile.roadRecipe, layerMap, roadWorldWidth, roadWorldLength, Allocator.Persistent);
                           if (!recipeData.IsCreated) {
                               Debug.LogError($"[PaintTerrainCommand] Failed to create CPU RecipeData for terrain {terrain.name}");
                               continue;
                           }
                           cpuRecipeDataMap.Add(terrain, recipeData);
                     }
                }
                // ---------------------------

                token.ThrowIfCancellationRequested();

                // --- 6. 处理每个地形 (创建并执行 Painter 任务) ---
                List<Task> tasks = new List<Task>();
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();

                    var td = terrain.terrainData;
                    if (td == null || td.alphamapLayers == 0 || Creator.profile.roadRecipe == null) continue;

                    var (_, coverageMin, coverageMax) = CalculateCoverageArea(terrain, finalBounds);
                    int numPixelsX = coverageMax.x - coverageMin.x + 1;
                    int numPixelsY = coverageMax.y - coverageMin.y + 1;
                    if (numPixelsX <= 0 || numPixelsY <= 0) continue;

                    ITerrainPainter painter;
                    RecipeData currentCpuRecipeData = default;

                    if (backend == PaintingBackend.CPU_Job_TwoPass)
                    {
                        if(cpuRecipeDataMap == null || !cpuRecipeDataMap.TryGetValue(terrain, out currentCpuRecipeData) || !currentCpuRecipeData.IsCreated) 
                        {
                             Debug.LogWarning($"[PaintTerrainCommand] Skipping terrain {terrain.name} due to missing CPU RecipeData.");
                             continue;
                        }
                        painter = new CpuTerrainPainter();
                    }
                    else
                    {
                        if (gpuDataManager == null) {
                             Debug.LogWarning($"[PaintTerrainCommand] Skipping terrain {terrain.name} due to invalid GpuDataManager.");
                             continue;
                        }
                        painter = new GpuTerrainPainter();
                    }
                    
                    tasks.Add(ExecutePainterAsync(painter, terrain, spineData, profileData,
                                                  currentCpuRecipeData, gpuDataManager,
                                                  roadContour, finalBounds,
                                                  coverageMin, coverageMax, token));

                } // End foreach terrain

                await Task.WhenAll(tasks);
                // ----------------------------------
            }
            catch (OperationCanceledException)
            {
                 Debug.Log($"[PaintTerrainCommand] Operation cancelled.");
                 throw;
            }
            catch (Exception ex)
            {
                 Debug.LogError($"[PaintTerrainCommand] Error during processing: {ex.Message}\n{ex.StackTrace}");
                 throw;
            }
            finally
            {
                // --- 7. 清理资源 ---
                roadContour.SafeDispose();
                 if (spineData.IsCreated) spineData.Dispose();
                 if (profileData.IsCreated) profileData.Dispose();
                gpuDataManager?.Dispose();
                // CPU RecipeData instances are disposed within each ExecutePainterAsync call. Avoid double disposal here to prevent redundant operations.
                // if (cpuRecipeDataMap != null) {
                //      foreach(var recipeData in cpuRecipeDataMap.Values) {
                //           if (recipeData.IsCreated) recipeData.Dispose();
                //      }
                // }
                HeightProvider?.MarkAsDirty();
            }
        }
        
        private async Task ExecutePainterAsync(
             ITerrainPainter painter, UnityEngine.Terrain terrain,
             PathJobsUtility.SpineData spineData, PathJobsUtility.ProfileData profileData,
             RecipeData cpuRecipeData, RecipeGpuDataManager gpuDataManager,
             NativeArray<float2> roadContour, float4 finalBounds,
             int2 coverageMin, int2 coverageMax, CancellationToken token)
        {
             try
             {
                   await painter.ExecuteAsync(terrain, spineData, profileData, cpuRecipeData, gpuDataManager, roadContour, finalBounds, coverageMin, coverageMax, token);
             }
             finally
             {
                  painter?.Dispose();
                   // CPU RecipeData 是为这个特定任务创建的，在这里释放
                   if (cpuRecipeData.IsCreated && painter is CpuTerrainPainter) {
                        cpuRecipeData.Dispose();
                   }
             }
        }

        private (bool useCoverageLimit, int2 coverageMin, int2 coverageMax) CalculateCoverageArea(UnityEngine.Terrain terrain, float4 contourBounds)
        {
            var td = terrain.terrainData;
            if(td == null) return (true, int2.zero, new int2(-1,-1));

            float4 bounds = contourBounds;
            if (PreferredBoundsXZ.HasValue) {
                 var pb = PreferredBoundsXZ.Value;
                 bounds = new float4(pb.x, pb.y, pb.z, pb.w);
            }

            var terrainPos = terrain.GetPosition();
            var terrainSize = td.size;
            var resolution = td.alphamapResolution;

            float terrainMinX = terrainPos.x, terrainMinZ = terrainPos.z;
            float terrainMaxX = terrainPos.x + terrainSize.x, terrainMaxZ = terrainPos.z + terrainSize.z;
            float intersectMinX = Mathf.Max(bounds.x, terrainMinX), intersectMinZ = Mathf.Max(bounds.y, terrainMinZ);
            float intersectMaxX = Mathf.Min(bounds.z, terrainMaxX), intersectMaxZ = Mathf.Min(bounds.w, terrainMaxZ);

            if (intersectMinX >= intersectMaxX || intersectMinZ >= intersectMaxZ)
            {
                return (true, new int2(0, 0), new int2(-1, -1));
            }

            float invSizeX = 1f / terrainSize.x, invSizeZ = 1f / terrainSize.z;
            int pixelMinX = Mathf.FloorToInt((intersectMinX - terrainMinX) * invSizeX * (resolution - 1));
            int pixelMinZ = Mathf.FloorToInt((intersectMinZ - terrainMinZ) * invSizeZ * (resolution - 1));
            int pixelMaxX = Mathf.CeilToInt((intersectMaxX - terrainMinX) * invSizeX * (resolution - 1));
            int pixelMaxZ = Mathf.CeilToInt((intersectMaxZ - terrainMinZ) * invSizeZ * (resolution - 1));

            pixelMinX = Mathf.Clamp(pixelMinX, 0, resolution - 1);
            pixelMinZ = Mathf.Clamp(pixelMinZ, 0, resolution - 1);
            pixelMaxX = Mathf.Clamp(pixelMaxX, 0, resolution - 1);
            pixelMaxZ = Mathf.Clamp(pixelMaxZ, 0, resolution - 1);

            return (true, new int2(pixelMinX, pixelMinZ), new int2(pixelMaxX, pixelMaxZ));
        }
        
        // --- 修正: 保持 `CalculateCoverageArea(TerrainWorkItem, ...)` 重载（如果仍有内部依赖）
        // (或者删除旧的 TerrainPaintResourceManager 和 TerrainWorkItem)
        // 为简单起见，我们假设旧的 TerrainWorkItem 不再需要，只保留 Terrain 版本
        
        private float4 DetermineFinalBounds(float4 contourBounds, NativeArray<float2> roadContour, PathSpine spine)
        {
            if (PreferredBoundsXZ.HasValue) return new float4(PreferredBoundsXZ.Value.x, PreferredBoundsXZ.Value.y, PreferredBoundsXZ.Value.z, PreferredBoundsXZ.Value.w);
            if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                 var fallback = GetExpandedXZBounds(spine, Creator.profile);
                 return new float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }
            return contourBounds;
        }

        #endregion
        
        // --- 移除了旧的辅助类 (TerrainWorkItem, TerrainPaintResourceManager) ---
    }
}