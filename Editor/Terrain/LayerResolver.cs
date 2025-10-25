using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Terrain
{
    /// <summary>
    ///     解析 StylizedRoadRecipe 中的 TerrainLayer，并与目标 Terrain 进行比对。
    ///     若缺失则提示用户是否添加，添加到 terrainData.terrainLayers 数组末尾。
    /// </summary>
    public static class LayerResolver
    {
        public static Dictionary<TerrainLayer, int> Resolve(UnityEngine.Terrain terrain, StylizedRoadRecipe recipe, bool interactive = true)
        {
            var result = new Dictionary<TerrainLayer, int>();
            if (terrain == null || terrain.terrainData == null || recipe == null) return result;

            var td = terrain.terrainData;
            var layers = new List<TerrainLayer>(td.terrainLayers ?? Array.Empty<TerrainLayer>());

            // 现有映射
            for (var i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l) result.TryAdd(l, i);
            }

            // 按配方逐一检查，缺失则询问是否添加到地形（interactive=true时）
            foreach (var roadLayer in recipe.GetLayers())
            {
                var tl = roadLayer?.contentLayer;
                if (!tl) continue;

                if (!result.ContainsKey(tl))
                {
                    if (interactive)
                    {
                        var ok = EditorUtility.DisplayDialog(
                            "添加缺失地形图层",
                            $"检测到配方引用的 TerrainLayer 未在当前地形中存在:\n\n{tl.name}\n\n是否将其添加到地形图层列表末尾?",
                            "是 (添加)",
                            "否 (跳过)");

                        if (ok)
                        {
                            Undo.RegisterCompleteObjectUndo(td, "添加地形图层");
                            // 寻找空位，优先填补前面的空槽
                            var insertIndex = -1;
                            for (var si = 0; si < layers.Count; si++)
                            {
                                if (layers[si] == null)
                                {
                                    insertIndex = si;
                                    break;
                                }
                            }
                            if (insertIndex >= 0)
                            {
                                layers[insertIndex] = tl;
                            }
                            else
                            {
                                layers.Add(tl);
                            }
                            td.terrainLayers = layers.ToArray();
                            result[tl] = insertIndex >= 0 ? insertIndex : layers.Count - 1;
                        }
                    }
                    // 非交互模式下不修改地形，也不弹窗，保持缺失层未映射
                }
            }

            return result;
        }

        /// <summary>
        ///     确保配方中的图层都存在于地形，并返回完整映射（非交互模式）
        /// </summary>
        public static Dictionary<TerrainLayer, int> ResolveEnsurePresent(UnityEngine.Terrain terrain, StylizedRoadRecipe recipe)
        {
            // 提前返回：检查输入参数有效性
            if (!IsInputValid(terrain, recipe))
            {
                return new Dictionary<TerrainLayer, int>();
            }

            var td = terrain.terrainData;
            var layers = new List<TerrainLayer>(td.terrainLayers ?? Array.Empty<TerrainLayer>());

            // 获取现有图层映射
            var result = GetExistingLayerMapping(layers);

            // 确保配方图层存在
            EnsureRecipeLayersPresent(recipe, layers, result, td);

            return result;
        }

        /// <summary>
        ///     检查输入参数是否有效
        /// </summary>
        private static bool IsInputValid(UnityEngine.Terrain terrain, StylizedRoadRecipe recipe)
        {
            return terrain &&
                   terrain.terrainData &&
                   recipe;
        }

        /// <summary>
        ///     获取现有图层映射
        /// </summary>
        private static Dictionary<TerrainLayer, int> GetExistingLayerMapping(List<TerrainLayer> layers)
        {
            var result = new Dictionary<TerrainLayer, int>();

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer)
                {
                    result.TryAdd(layer, i);
                }
            }

            return result;
        }

        /// <summary>
        ///     确保配方图层存在
        /// </summary>
        private static void EnsureRecipeLayersPresent(
            StylizedRoadRecipe recipe, 
            List<TerrainLayer> layers, 
            Dictionary<TerrainLayer, int> result, 
            TerrainData td)
        {
            foreach (var roadLayer in recipe.GetLayers())
            {
                // 提前返回：检查图层有效性
                if (!IsRoadLayerValid(roadLayer))
                {
                    continue;
                }

                var terrainLayer = roadLayer.contentLayer;

                // 提前返回：图层已存在
                if (result.ContainsKey(terrainLayer))
                {
                    continue;
                }

                // 添加缺失图层
                AddMissingLayer(terrainLayer, layers, result, td);
            }
        }

        /// <summary>
        ///     检查道路图层是否有效
        /// </summary>
        private static bool IsRoadLayerValid(RoadLayer roadLayer)
        {
            return roadLayer != null && roadLayer.contentLayer;
        }

        /// <summary>
        ///     添加缺失图层
        /// </summary>
        private static void AddMissingLayer(
            TerrainLayer terrainLayer, 
            List<TerrainLayer> layers, 
            Dictionary<TerrainLayer, int> result, 
            TerrainData td)
        {
            Undo.RegisterCompleteObjectUndo(td, "添加地形图层");

            // 寻找可用的插入位置
            var insertIndex = FindAvailableSlot(layers);

            // 插入或添加图层
            if (insertIndex >= 0)
            {
                layers[insertIndex] = terrainLayer;
            }
            else
            {
                layers.Add(terrainLayer);
                insertIndex = layers.Count - 1;
            }

            // 更新地形数据
            td.terrainLayers = layers.ToArray();

            // 更新映射结果
            result[terrainLayer] = insertIndex;
        }

        /// <summary>
        ///     寻找可用的图层槽位
        /// </summary>
        private static int FindAvailableSlot(List<TerrainLayer> layers)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (!layers[i])
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
