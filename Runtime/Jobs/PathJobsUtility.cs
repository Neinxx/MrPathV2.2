// PathJobsUtility.cs (已修正构造函数)
using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
namespace MrPathV2
{

    public static class PathJobsUtility
    {
        #region 共享数据结构

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

        public struct LayerData
        {
            public float width;
            public float horizontalOffset;
            public float verticalOffset;
            public float falloff;
        }

        public struct ProfileData : IDisposable
        {
            [ReadOnly] public NativeArray<LayerData> layers;
            [ReadOnly] public NativeArray<int> terrainLayerIndices;
            [ReadOnly] public bool forceHorizontal; // 【新增】防倾斜开关字段

            public bool IsCreated => layers.IsCreated;
            public int Length => layers.Length;

            // 【修正】构造函数签名，增加 PathProfile 参数
            public ProfileData(List<PathLayer> sourceLayers, Dictionary<TerrainLayer, int> terrainLayerMap, PathProfile profile, Allocator allocator)
            {
                // 【修正】为 forceHorizontal 字段赋值
                forceHorizontal = profile.forceHorizontal;

                layers = new NativeArray<LayerData>(sourceLayers.Count, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < sourceLayers.Count; i++)
                {
                    var gradient = sourceLayers[i].terrainPaintingRecipe?.blendLayers?.FirstOrDefault()?.blendMask?.gradient;
                    float falloffStart = (gradient != null && gradient.keys.Length > 0) ? gradient.keys[0].time : -1.0f;
                    float falloffRatio = (math.clamp(falloffStart, -1.0f, 1.0f) + 1.0f) / 2.0f;

                    layers[i] = new LayerData
                    {
                        width = sourceLayers[i].width,
                        horizontalOffset = sourceLayers[i].horizontalOffset,
                        verticalOffset = sourceLayers[i].verticalOffset,
                        falloff = falloffRatio
                    };
                }

                if (terrainLayerMap != null)
                {
                    terrainLayerIndices = new NativeArray<int>(sourceLayers.Count, allocator);
                    for (int i = 0; i < sourceLayers.Count; i++)
                    {
                        int mapping = -1;
                        var recipe = sourceLayers[i].terrainPaintingRecipe;
                        if (recipe != null && recipe.blendLayers.Count > 0 && recipe.blendLayers[0].terrainLayer != null)
                        {
                            if (!terrainLayerMap.TryGetValue(recipe.blendLayers[0].terrainLayer, out mapping)) { mapping = -1; }
                        }
                        terrainLayerIndices[i] = mapping;
                    }
                }
                else
                {
                    terrainLayerIndices = new NativeArray<int>(0, allocator);
                }
            }

            public void Dispose()
            {
                if (layers.IsCreated) layers.Dispose();
                if (terrainLayerIndices.IsCreated) terrainLayerIndices.Dispose();
            }
        }
        #endregion
    }
}