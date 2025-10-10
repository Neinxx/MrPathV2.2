// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Jobs/PathJobsUtility.cs (最终统一版)
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MrPathV2
{
    public static class PathJobsUtility
    {
        public struct SpineData : IDisposable 
        {
            [ReadOnly] public NativeArray<float3> points;
            [ReadOnly] public NativeArray<float3> tangents;
            [ReadOnly] public NativeArray<float3> normals;
            public bool IsCreated => points.IsCreated;
            public int Length => points.Length;

            public SpineData(PathSpine spine, Allocator allocator)
            {
                points = new NativeArray<float3>(spine.VertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                tangents = new NativeArray<float3>(spine.VertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                normals = new NativeArray<float3>(spine.VertexCount, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < spine.VertexCount; i++)
                {
                    points[i] = spine.points[i];
                    tangents[i] = spine.tangents[i];
                    normals[i] = spine.surfaceNormals[i];
                }
            }

            public void Dispose()
            {
                if (points.IsCreated) points.Dispose();
                if (tangents.IsCreated) tangents.Dispose();
                if (normals.IsCreated) normals.Dispose();
            }
        }

        public struct ProfileData : IDisposable
        {
            [ReadOnly] public float roadWidth;
            [ReadOnly] public float falloffWidth;
            [ReadOnly] public bool forceHorizontal;
            
            [ReadOnly] private NativeArray<float> _bakedCrossSection;
            [ReadOnly] private NativeArray<float> _bakedFalloff;
            
            [ReadOnly] public NativeArray<LayerData> layers;
            [ReadOnly] public NativeArray<int> terrainLayerIndices;
            public int Length => layers.Length;

            public bool IsCreated => _bakedCrossSection.IsCreated;
            private const int BAKE_RESOLUTION = 64;

            public ProfileData(PathProfile profile, Dictionary<TerrainLayer, int> terrainLayerMap, Allocator allocator)
            {
                roadWidth = profile.roadWidth;
                falloffWidth = profile.falloffWidth;
                forceHorizontal = profile.forceHorizontal;

                _bakedCrossSection = new NativeArray<float>(BAKE_RESOLUTION, allocator);
                BakeCurve(profile.crossSection, _bakedCrossSection, -1, 1);

                _bakedFalloff = new NativeArray<float>(BAKE_RESOLUTION, allocator);
                BakeCurve(profile.falloffShape, _bakedFalloff, 0, 1);
                
                var sourceLayers = profile.layers;
                layers = new NativeArray<LayerData>(sourceLayers.Count, allocator);
                terrainLayerIndices = new NativeArray<int>(sourceLayers.Count, allocator);

                if (sourceLayers != null && sourceLayers.Count > 0)
                {
                    for (int i = 0; i < sourceLayers.Count; i++)
                    {
                        // 这部分仅用于纹理绘制，几何数据不再使用
                        layers[i] = new LayerData { /* ... 填充旧的 layer 数据 ... */ };
                        
                        if (terrainLayerMap != null)
                        {
                            int mapping = -1;
                            var recipe = sourceLayers[i].terrainPaintingRecipe;
                            if (recipe != null && recipe.blendLayers.Count > 0 && recipe.blendLayers[0].terrainLayer != null)
                            {
                                if (!terrainLayerMap.TryGetValue(recipe.blendLayers[0].terrainLayer, out mapping)) { mapping = -1; }
                            }
                            terrainLayerIndices[i] = mapping;
                        } else {
                            terrainLayerIndices[i] = -1;
                        }
                    }
                }
            }
            
            public float EvaluateCrossSection(float t) => EvaluateBakedCurve(_bakedCrossSection, t, -1, 1);
            public float EvaluateFalloff(float t) => EvaluateBakedCurve(_bakedFalloff, t, 0, 1);

            private static void BakeCurve(AnimationCurve curve, NativeArray<float> bakedData, float start, float end)
            {
                for (int i = 0; i < BAKE_RESOLUTION; i++)
                {
                    float time = start + (end - start) * (i / (float)(BAKE_RESOLUTION - 1));
                    bakedData[i] = curve.Evaluate(time);
                }
            }
            
            private static float EvaluateBakedCurve(NativeArray<float> bakedData, float t, float start, float end)
            {
                float normalizedT = (t - start) / (end - start);
                float floatIndex = normalizedT * (BAKE_RESOLUTION - 1);
                int indexA = (int)math.floor(floatIndex);
                int indexB = (int)math.ceil(floatIndex);
                if (indexA < 0) return bakedData[0];
                if (indexB >= BAKE_RESOLUTION) return bakedData[BAKE_RESOLUTION - 1];
                if (indexA == indexB) return bakedData[indexA];
                return math.lerp(bakedData[indexA], bakedData[indexB], floatIndex - indexA);
            }

            public void Dispose()
            {
                if (_bakedCrossSection.IsCreated) _bakedCrossSection.Dispose();
                if (_bakedFalloff.IsCreated) _bakedFalloff.Dispose();
                if (layers.IsCreated) layers.Dispose();
                if (terrainLayerIndices.IsCreated) terrainLayerIndices.Dispose();
            }
        }
        
        public struct LayerData 
        {
            public float width;
            public float horizontalOffset;
            public float falloff;
            public float3 verticalOffset;
        }
    }
}