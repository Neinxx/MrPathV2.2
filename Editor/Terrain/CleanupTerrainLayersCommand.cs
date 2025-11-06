using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Terrain
{
    /// <summary>
    ///     清理受路径影响的地形中未使用的 Splat 图层。
    ///     遵循单一职责：仅负责层清理；路径采样与地形选择由基类处理。
    /// </summary>
    public class CleanupTerrainLayersCommand : TerrainCommandBase
    {
        public CleanupTerrainLayersCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider) { }

        public override string GetCommandName() => "CleanupTerrainLayers";

        /// <summary>
        ///     处理地形清理，采用提前返回风格和单一职责原则
        /// </summary>
        protected override Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            var totalRemovedLayers = ProcessAllTerrains(terrains, token);
            ShowCompletionMessage(totalRemovedLayers);

            return Task.CompletedTask;
        }

        /// <summary>
        ///     处理所有地形，返回移除的图层总数
        /// </summary>
        private int ProcessAllTerrains(List<UnityEngine.Terrain> terrains, CancellationToken token)
        {
            var totalRemovedLayers = 0;

            foreach (var terrain in terrains)
            {
                token.ThrowIfCancellationRequested();

                var removedCount = ProcessSingleTerrain(terrain);
                totalRemovedLayers += removedCount;
            }

            return totalRemovedLayers;
        }

        /// <summary>
        ///     处理单个地形，返回移除的图层数量
        /// </summary>
        private int ProcessSingleTerrain(UnityEngine.Terrain terrain)
        {
            // 提前返回：检查地形数据有效性
            var terrainData = terrain.terrainData;
            if (!IsTerrainDataValid(terrainData))
            {
                return 0;
            }

            // 获取地形数据
            var resolution = terrainData.alphamapResolution;
            var oldAlphaMaps = terrainData.GetAlphamaps(0, 0, resolution, resolution);
            var oldTerrainLayers = terrainData.terrainLayers;

            // 查找需要保留的图层索引
            var keptIndices = FindUsedLayerIndices(oldAlphaMaps, resolution, terrainData.alphamapLayers);

            // 提前返回：没有需要移除的图层
            var removedCount = terrainData.alphamapLayers - keptIndices.Count;
            if (removedCount <= 0)
            {
                return 0;
            }

            // 重建地形数据
            RebuildTerrainData(terrainData, oldTerrainLayers, oldAlphaMaps, keptIndices, resolution);

            // 记录日志
            LogTerrainCleanup(terrain.name, removedCount);

            return removedCount;
        }

        /// <summary>
        ///     检查地形数据是否有效
        /// </summary>
        private static bool IsTerrainDataValid(TerrainData terrainData) => terrainData != null &&
                                                                           terrainData.alphamapLayers > 0 &&
                                                                           terrainData.terrainLayers != null;

        /// <summary>
        ///     查找被使用的图层索引
        /// </summary>
        private List<int> FindUsedLayerIndices(float[,,] alphaMaps, int resolution, int layerCount)
        {
            const float threshold = 1e-4f; // 判断"非零"的最小值
            var keptIndices = new List<int>();

            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                if (IsLayerUsed(alphaMaps, resolution, layerIndex, threshold))
                {
                    keptIndices.Add(layerIndex);
                }
            }

            return keptIndices;
        }

        /// <summary>
        ///     检查图层是否被使用
        /// </summary>
        private static bool IsLayerUsed(float[,,] alphaMaps, int resolution, int layerIndex, float threshold)
        {
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    if (alphaMaps[y, x, layerIndex] > threshold)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     重建地形数据
        /// </summary>
        private static void RebuildTerrainData(
            TerrainData terrainData,
            TerrainLayer[] oldTerrainLayers,
            float[,,] oldAlphaMaps,
            List<int> keptIndices,
            int resolution)
        {
            // 重建地形图层
            var newTerrainLayers = CreateNewTerrainLayers(oldTerrainLayers, keptIndices);

            // 重建Alpha贴图
            var newAlphaMaps = CreateNewAlphaMaps(oldAlphaMaps, keptIndices, resolution);

            // 应用新数据
            terrainData.terrainLayers = newTerrainLayers;
            terrainData.SetAlphamaps(0, 0, newAlphaMaps);
            EditorUtility.SetDirty(terrainData);
        }

        /// <summary>
        ///     创建新的地形图层数组
        /// </summary>
        private static TerrainLayer[] CreateNewTerrainLayers(TerrainLayer[] oldTerrainLayers, List<int> keptIndices)
        {
            var newTerrainLayers = new TerrainLayer[keptIndices.Count];

            for (var i = 0; i < keptIndices.Count; i++)
            {
                newTerrainLayers[i] = oldTerrainLayers[keptIndices[i]];
            }

            return newTerrainLayers;
        }

        /// <summary>
        ///     创建新的Alpha贴图数组
        /// </summary>
        private static float[,,] CreateNewAlphaMaps(float[,,] oldAlphaMaps, List<int> keptIndices, int resolution)
        {
            var newAlphaMaps = new float[resolution, resolution, keptIndices.Count];

            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    for (var i = 0; i < keptIndices.Count; i++)
                    {
                        newAlphaMaps[y, x, i] = oldAlphaMaps[y, x, keptIndices[i]];
                    }
                }
            }

            return newAlphaMaps;
        }

        /// <summary>
        ///     记录地形清理日志
        /// </summary>
        private static void LogTerrainCleanup(string terrainName, int removedCount)
        {
            ErrorHandler.LogInfo($"[CleanupTerrainLayers] Terrain '{terrainName}' 移除了 {removedCount} 个未使用的 Splat 层");
        }

        /// <summary>
        ///     显示完成消息
        /// </summary>
        private static void ShowCompletionMessage(int totalRemovedLayers)
        {
            var message = totalRemovedLayers > 0
                ? $" 已从受路径影响的 Terrain 中移除 {totalRemovedLayers} 个未使用图层"
                : " 未发现未使用的图层（受路径影响的地形）";

            ErrorHandler.LogInfo($"[CleanupTerrainLayers] {message}");
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message));
        }
    }
}
