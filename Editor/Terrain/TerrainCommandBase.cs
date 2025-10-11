// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/TerrainCommandBase.cs (最终统一版)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

namespace MrPathV2
{
    public abstract class TerrainCommandBase
    {
        protected readonly PathCreator Creator;
        protected readonly IHeightProvider HeightProvider;
        // 可选：来自预览网格的首选 XZ 包围盒 (minX, minZ, maxX, maxZ)
        public Vector4? PreferredBoundsXZ { get; private set; }

        protected TerrainCommandBase(PathCreator creator, IHeightProvider heightProvider)
        {
            Creator = creator;
            HeightProvider = heightProvider;
        }

        public abstract string GetCommandName();
        public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

        public async Task ExecuteAsync(CancellationToken token)
        {
            if (!Validate(out var spine, out var terrains)) return;
            try
            {
                await ProcessTerrainsAsync(terrains, spine, token);
                token.ThrowIfCancellationRequested();
                StitchTerrains(terrains);
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[Mr.Path] 用户取消了 {GetCommandName()} 操作。");
            }
        }

        protected abstract Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spine, CancellationToken token);

        /// <summary>
        /// 设置首选的预览包围盒（XZ 平面），用于作业的粗剔除。
        /// </summary>
        public void SetPreviewBoundsXZ(Vector4 boundsXZ)
        {
            PreferredBoundsXZ = boundsXZ;
        }
        
        protected bool Validate(out PathSpine spine, out List<Terrain> affectedTerrains)
        {
            spine = default; affectedTerrains = null;
            if (Creator == null || Creator.profile == null || Creator.pathData.KnotCount < 2) { Debug.LogError("路径无效或未配置 Profile。"); return false; }
            spine = PathSampler.SamplePath(Creator, HeightProvider);
            if (spine.VertexCount < 2) { Debug.LogWarning("路径采样点不足，无法应用。"); return false; }
            affectedTerrains = FindAffectedTerrains(spine);
            if (affectedTerrains.Count == 0) { Debug.LogWarning("路径未影响任何活动地形。"); return false; }
            return true;
        }
        private List<Terrain> FindAffectedTerrains(PathSpine spine)
        {
            Bounds projectedBounds = GetProjectedSpineBounds(spine);
            var affectedTerrains = new List<Terrain>();
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                Bounds terrainBounds = new Bounds(terrain.GetPosition() + terrain.terrainData.size / 2f, terrain.terrainData.size);
                if (projectedBounds.Intersects(terrainBounds)) { affectedTerrains.Add(terrain); }
            }
            return affectedTerrains;
        }
        private Bounds GetProjectedSpineBounds(PathSpine spine)
        {
            if (spine.VertexCount == 0) return new Bounds();
            var pathBounds = new Bounds(spine.points[0], Vector3.zero);
            for (int i = 1; i < spine.VertexCount; i++) { pathBounds.Encapsulate(spine.points[i]); }
            float maxExtent = Creator.profile != null ? Creator.profile.roadWidth / 2f + Creator.profile.falloffWidth : 0;
            pathBounds.Expand(new Vector3(maxExtent * 2, 0, maxExtent * 2));
            return new Bounds(new Vector3(pathBounds.center.x, pathBounds.center.y, pathBounds.center.z), new Vector3(pathBounds.size.x, float.MaxValue, pathBounds.size.z));
        }

        /// <summary>
        /// 计算二维展开的 AABB（XZ 平面），用于作业的粗剔除或轮廓不可用时的退化。
        /// </summary>
        protected static Vector4 GetExpandedXZBounds(PathSpine spine, PathProfile profile)
        {
            // PathSpine is a struct; it cannot be null. Only check vertex count.
            if (spine.VertexCount == 0)
                return new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

            float halfWidth = (profile != null ? profile.roadWidth * 0.5f + profile.falloffWidth : 0f);
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < spine.VertexCount; i++)
            {
                var p = spine.points[i];
                minX = Mathf.Min(minX, p.x - halfWidth);
                minZ = Mathf.Min(minZ, p.z - halfWidth);
                maxX = Mathf.Max(maxX, p.x + halfWidth);
                maxZ = Mathf.Max(maxZ, p.z + halfWidth);
            }
            return new Vector4(minX, minZ, maxX, maxZ);
        }
        protected Dictionary<TerrainLayer, int> BuildTerrainLayerMap(Terrain terrain)
        {
            var terrainLayers = terrain.terrainData.terrainLayers;
            var layerToIndexMap = new Dictionary<TerrainLayer, int>();
            for (int i = 0; i < terrainLayers.Length; i++) { if (terrainLayers[i] != null) layerToIndexMap[terrainLayers[i]] = i; }
            return layerToIndexMap;
        }
        private void StitchTerrains(List<Terrain> terrains)
        {
            foreach (var t in terrains) t.Flush();
            foreach (var t in terrains) t.SetNeighbors(t.leftNeighbor, t.topNeighbor, t.rightNeighbor, t.bottomNeighbor);
        }
    }
}