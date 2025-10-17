using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 解析 StylizedRoadRecipe 中的 TerrainLayer，并与目标 Terrain 进行比对。
    /// 若缺失则提示用户是否添加，添加到 terrainData.terrainLayers 数组末尾。
    /// </summary>
    public static class LayerResolver
    {
        public static Dictionary<TerrainLayer, int> Resolve(Terrain terrain, StylizedRoadRecipe recipe)
        {
            var result = new Dictionary<TerrainLayer, int>();
            if (terrain == null || terrain.terrainData == null || recipe == null) return result;

            var td = terrain.terrainData;
            var layers = new List<TerrainLayer>(td.terrainLayers ?? System.Array.Empty<TerrainLayer>());

            // 现有映射
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l != null && !result.ContainsKey(l)) result[l] = i;
            }

            // 按配方逐一检查，缺失则询问是否添加到地形
            foreach (var blend in recipe.blendLayers)
            {
                var tl = blend?.terrainLayer;
                if (tl == null) continue;

                if (!result.ContainsKey(tl))
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
                        int insertIndex = -1;
                        for (int si = 0; si < layers.Count; si++)
                        {
                            if (layers[si] == null) { insertIndex = si; break; }
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
            }

            return result;
        }
    }
}