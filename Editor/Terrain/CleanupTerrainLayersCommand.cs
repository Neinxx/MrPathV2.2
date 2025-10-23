using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Interfaces;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Terrain
{
    /// <summary>
    /// 清理受路径影响的地形中未使用的 Splat 图层。
    /// 遵循单一职责：仅负责层清理；路径采样与地形选择由基类处理。
    /// </summary>
    public class CleanupTerrainLayersCommand : TerrainCommandBase
    {
        public CleanupTerrainLayersCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider) { }

        public override string GetCommandName() => "CleanupTerrainLayers";

        protected override Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            int totalRemovedLayers = 0;
            const float threshold = 1e-4f; // 判断“非零”的最小值

            foreach (var terrain in terrains)
            {
                token.ThrowIfCancellationRequested();
                var td = terrain.terrainData;
                if (td == null || td.alphamapLayers <= 0 || td.terrainLayers == null)
                    continue;

                int res = td.alphamapResolution;
                int oldLayerCount = td.alphamapLayers;
                var oldAlpha = td.GetAlphamaps(0, 0, res, res);
                var oldSplats = td.terrainLayers;

                // Step 1: 检查每个 layer 是否被使用（在整个 alphamap 范围内）
                var keptIndices = new List<int>();
                for (int l = 0; l < oldLayerCount; l++)
                {
                    bool isUsed = false;
                    for (int y = 0; y < res && !isUsed; y++)
                    {
                        for (int x = 0; x < res && !isUsed; x++)
                        {
                            if (oldAlpha[y, x, l] > threshold)
                            {
                                isUsed = true;
                            }
                        }
                    }

                    if (isUsed)
                    {
                        keptIndices.Add(l);
                    }
                }

                int removedCount = oldLayerCount - keptIndices.Count;
                if (removedCount <= 0)
                    continue; // 无未使用层，跳过

                // Step 2: 重建 splatPrototypes
                var newSplats = new TerrainLayer[keptIndices.Count];
                for (int i = 0; i < keptIndices.Count; i++)
                {
                    newSplats[i] = oldSplats[keptIndices[i]];
                }

                // Step 3: 重建 alphamap（仅保留使用的 layer）
                var newAlpha = new float[res, res, keptIndices.Count];
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        for (int i = 0; i < keptIndices.Count; i++)
                        {
                            newAlpha[y, x, i] = oldAlpha[y, x, keptIndices[i]];
                        }
                    }
                }

                // Step 4: 应用新数据
                td.terrainLayers = newSplats;
                td.SetAlphamaps(0, 0, newAlpha);
                EditorUtility.SetDirty(td);

                totalRemovedLayers += removedCount;
                Debug.Log($"[CleanupTerrainLayers] Terrain '{terrain.name}' 移除了 {removedCount} 个未使用的 Splat 层");
            }

            string msg = totalRemovedLayers > 0
                ? $"✅ 已从受路径影响的 Terrain 中移除 {totalRemovedLayers} 个未使用图层"
                : "🚧 未发现未使用的图层（受路径影响的地形）";

            Debug.Log($"[CleanupTerrainLayers] {msg}");
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(msg));

            return Task.CompletedTask;
        }



    }
}