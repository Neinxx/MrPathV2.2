// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/TerrainCommandBase.cs (WYSIWYG 最终版)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System;

namespace MrPathV2
{
    public abstract class TerrainCommandBase
    {
        protected readonly PathCreator Creator;
        protected readonly IHeightProvider HeightProvider;

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

            token.ThrowIfCancellationRequested();

            GenerateMeshGeometry(spine, Creator.profile, out var meshVertices, out var meshTriangles, Allocator.Persistent);

            try
            {
                // 传递原始的 'spine' (PathSpine类型)，子类将根据需要使用它
                await ProcessTerrainsAsync(terrains, spine, meshVertices, meshTriangles, token);

                token.ThrowIfCancellationRequested();
                StitchTerrains(terrains);
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[Mr.Path] 用户取消了 {GetCommandName()} 操作。");
            }
            finally
            {
                if (meshVertices.IsCreated) meshVertices.Dispose();
                if (meshTriangles.IsCreated) meshTriangles.Dispose();
            }
        }

        /// <summary>
        /// 【最终修正】这是唯一需要子类实现的抽象方法。它接收 'PathSpine' 类型用于轮廓生成，并接收精确的网格几何数据。
        /// </summary>
        protected abstract Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spineForContour, NativeArray<float3> meshVertices, NativeArray<int> meshTriangles, CancellationToken token);

        /// <summary>
        /// 【最终修正】此方法的签名现在是正确的，它接收 'PathSpine' 类型。
        /// </summary>
        private void GenerateMeshGeometry(PathSpine spine, PathProfile profile, out NativeArray<float3> vertices, out NativeArray<int> triangles, Allocator allocator)
        {
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.TempJob);
            var profileData = new PathJobsUtility.ProfileData(profile.layers, null, profile, Allocator.TempJob);

            using var tempVertices = new NativeList<float3>(Allocator.TempJob);
            using var tempTriangles = new NativeList<int>(Allocator.TempJob);
            using var tempUvs = new NativeList<float2>(Allocator.TempJob);
            using var tempColors = new NativeList<Color32>(Allocator.TempJob);
            using var tempNormals = new NativeList<float3>(Allocator.TempJob);

            var job = new GenerateMeshJob
            {
                spine = spineData,
                profile = profileData,
                vertices = tempVertices,
                triangles = tempTriangles,
                uvs = tempUvs,
                colors = tempColors,
                normals = tempNormals
            };

            job.Run();

            vertices = new NativeArray<float3>(tempVertices.AsArray(), allocator);
            triangles = new NativeArray<int>(tempTriangles.AsArray(), allocator);

            spineData.Dispose();
            profileData.Dispose();
        }

        // --- 以下所有辅助方法保持不变 ---
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
            float maxExtent = Creator.profile != null && Creator.profile.layers.Count > 0 ? Creator.profile.layers.Max(l => Mathf.Abs(l.horizontalOffset) + l.width / 2f) : 0;
            pathBounds.Expand(new Vector3(maxExtent * 2, 0, maxExtent * 2));
            return new Bounds(new Vector3(pathBounds.center.x, pathBounds.center.y, pathBounds.center.z), new Vector3(pathBounds.size.x, float.MaxValue, pathBounds.size.z));
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