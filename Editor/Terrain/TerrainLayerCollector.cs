#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MrPathV2.Editor.Terrain
{
    /// <summary>
    ///     仅负责枚举/去重 TerrainLayer 列表的工具类（单一职责）。
    /// </summary>
    public static class TerrainLayerCollector
    {
        public static List<TerrainLayer> GetLayersFromCurrentTerrain()
        {
            var t = UnityEngine.Terrain.activeTerrain;
            if (!t || !t.terrainData) return new List<TerrainLayer>();
            return t.terrainData.terrainLayers?.Where(l => l).ToList() ?? new List<TerrainLayer>();
        }

        public static List<TerrainLayer> GetLayersFromAllActiveTerrains()
        {
            var result = new List<TerrainLayer>();
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return result;
            foreach (var terrain in terrains)
            {
                if (!terrain || !terrain.terrainData) continue;
                var layers = terrain.terrainData.terrainLayers;
                if (layers == null) continue;
                foreach (var l in layers)
                {
                    if (l) result.Add(l);
                }
            }
            return DistinctByInstance(result);
        }

        private static List<TerrainLayer> DistinctByInstance(List<TerrainLayer> layers)
        {
            var seen = new HashSet<int>();
            var distinct = new List<TerrainLayer>();
            foreach (var l in layers)
            {
                var id = l.GetInstanceID();
                if (seen.Add(id)) distinct.Add(l);
            }
            return distinct;
        }
    }
}
#endif