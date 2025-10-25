using System;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2._2.Runtime.Jobs;
using MrPathV2._2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
// <-- 确保 using
using NativeArrayExtensions = MrPathV2._2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace MrPathV2._2.Editor.Terrain
{
    public class CpuTerrainPainter : ITerrainPainter
    {
        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData, // CPU uses this
            RecipeGpuDataManager recipeGpuData, // Not used
            NativeArray<float2> roadContour,
            float4 contourBounds,
            int2 coverageMin,
            int2 coverageMax,
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

            var alphamaps3D = td.GetAlphamaps(0, 0, resolution, resolution);
            var alphamaps1D = NativeArrayExtensions.CreateTracked<float>(alphamaps3D.Length, Allocator.Persistent);
            // 将原有地形Alpha数据复制到可写的一维数组，避免未覆盖区域被清零
            ConvertAlphamaps3DTo1D(alphamaps3D, alphamaps1D);

            var numPixelsX = coverageMax.x - coverageMin.x + 1;
            var numPixelsY = coverageMax.y - coverageMin.y + 1;
            var totalPixelsInBounds = numPixelsX * numPixelsY;

            JobHandle combinedHandle = default;
            NativeArray<RoadPixelInfo> pixelInfoMap = default;

            if (!recipeData.IsCreated)
            {
                Debug.LogError("[CpuTerrainPainter] RecipeData is invalid!");
                alphamaps1D.SafeDispose();
                return;
            }

            try
            {
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
                        Alphamaps = alphamaps1D
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
                td.SetAlphamaps(0, 0, alphamaps3D);
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
        // -----------------------------

        public void Dispose() { }

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
