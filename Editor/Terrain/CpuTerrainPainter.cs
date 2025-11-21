using System;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Jobs;
using MrPathV2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
// <-- 确保 using
using NativeArrayExtensions = MrPathV2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace MrPathV2.Editor.Terrain
{
    public class CpuTerrainPainter : ITerrainPainter
    {
        // ITerrainPainter 接口实现
        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData,
            NativeArray<float2> roadContour,
            float4 contourBounds,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            await ExecuteAsyncInternal(terrain, spineData, profileData, recipeData, roadContour, contourBounds, coverageMin, coverageMax, token);
        }
        // -----------------------------

        public void Dispose() { }


        // 保留原有的完整参数版本供内部使用
        private static async Task ExecuteAsyncInternal(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData, // CPU uses this
            NativeArray<float2> roadContour,
            float4 contourBounds,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            var td = terrain.terrainData;
            if (td == null)
            {
                Debug.LogError("[CpuTerrainPainter] TerrainData is null.");
                return;
            }
            var resolution = td.alphamapResolution;
            var layers = td.alphamapLayers;

            // 仅读取覆盖区域，避免整张控制纹理的昂贵复制
            var startX = Mathf.Clamp(coverageMin.x, 0, resolution - 1);
            var startY = Mathf.Clamp(coverageMin.y, 0, resolution - 1);
            // 半开区间：[min,max) => 宽高为差值，不 +1
            var numPixelsX = Mathf.Clamp(coverageMax.x - coverageMin.x, 0, resolution - startX);
            var numPixelsY = Mathf.Clamp(coverageMax.y - coverageMin.y, 0, resolution - startY);
            var totalPixelsInBounds = numPixelsX * numPixelsY;

            var alphamaps3D = td.GetAlphamaps(startX, startY, numPixelsX, numPixelsY);
            var alphamaps1D = NativeArrayExtensions.CreateTracked<float>(alphamaps3D.Length, Allocator.Persistent);
            // 将区域内现有Alpha复制到一维数组，保持未覆盖区权重
            ConvertAlphamaps3DTo1D(alphamaps3D, alphamaps1D);

            NativeArray<RoadPixelInfo> pixelInfoMap = default;

            if (!recipeData.IsCreated)
            {
                Debug.LogError("[CpuTerrainPainter] RecipeData is invalid!");
                alphamaps1D.SafeDispose();
                return;
            }

            try
            {
                JobHandle combinedHandle;
                if (totalPixelsInBounds > 0)
                {
                    pixelInfoMap = NativeArrayExtensions.CreateTracked<RoadPixelInfo>(totalPixelsInBounds, Allocator.TempJob);

                    var job1 = new FindRoadPixelsJob // <-- Fix: 已在 using 中
                    {
                        Spine = spineData,
                        Profile = profileData,
                        TerrainPos = terrain.GetPosition(),
                        TerrainSize = td.size,
                        AlphamapResolution = resolution,
                        RoadContour = roadContour,
                        ContourBounds = contourBounds,
                        CoverageMin = coverageMin,
                        CoverageMax = coverageMax,
                        PixelInfoMap = pixelInfoMap
                    };
                    var handle1 = job1.Schedule(totalPixelsInBounds, 128);

                    var job2 = new PaintSplatmapJob // <-- Fix: 已在 using 中
                    {
                        Recipe = recipeData,
                        AlphamapResolution = resolution,
                        AlphamapLayerCount = layers,
                        PixelInfoMap = pixelInfoMap, // <-- Fix: 字段已存在
                        CoverageMin = coverageMin,
                        CoverageMax = coverageMax,
                        Alphamaps = alphamaps1D,
                        // 传递不透明绘制开关与阈值（与预览保持一致：阈值 0.2）
                        OpaquePainting = profileData.OpaquePreview,
                        AlphaClipThreshold = profileData.OpaquePreview ? MrPathV2.Runtime.Core.Constants.MaskConstants.DefaultAlphaClipThreshold : 0f
                    };
                    combinedHandle = job2.Schedule(totalPixelsInBounds, 128, handle1);
                }
                else
                {
                    combinedHandle = new JobHandle();
                }

                while (!combinedHandle.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                combinedHandle.Complete();

                token.ThrowIfCancellationRequested();

                await Task.Yield(); // 确保在主线程
                ConvertAlphamaps1DTo3D(alphamaps1D, alphamaps3D);
                td.SetAlphamaps(startX, startY, alphamaps3D);
                terrain.Flush();
                EditorUtility.SetDirty(td);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[CpuTerrainPainter] Operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CpuTerrainPainter] Error during execution: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                alphamaps1D.SafeDispose();
                pixelInfoMap.SafeDispose();
            }
        }

        // --- Data Conversion Helpers ---
        private static void ConvertAlphamaps3DTo1D(float[,,] source, NativeArray<float> destination)
        {
            int height = source.GetLength(0), width = source.GetLength(1), depth = source.GetLength(2);
            var index = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                destination[index++] = source[y, x, z];
            }
        }
        private static void ConvertAlphamaps1DTo3D(NativeArray<float> source, float[,,] destination)
        {
            int height = destination.GetLength(0), width = destination.GetLength(1), depth = destination.GetLength(2);
            if (!source.IsCreated || source.Length != width * height * depth)
            {
                Debug.LogError("ConvertAlphamaps1DTo3D size mismatch!");
                return;
            }
            var index = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                destination[y, x, z] = source[index++];
            }
        }
    }
}
