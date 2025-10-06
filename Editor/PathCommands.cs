// 请用此最终、完美的完整代码，替换你的 PathCommands.cs

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Commands
{
    public interface ICommand { Task ExecuteAsync(); }

    public class ApplyPathToTerrainCommand : ICommand
    {
        private readonly PathCreator _creator;
        private readonly TerrainHeightProvider _heightProvider;

        public ApplyPathToTerrainCommand(PathCreator creator, TerrainHeightProvider heightProvider)
        {
            _creator = creator;
            _heightProvider = heightProvider;
        }

        public async Task ExecuteAsync()
        {
            if (_creator == null || _creator.profile == null || _creator.Path == null || _creator.NumPoints < 2) return;

            var spine = PathSampler.SamplePath(_creator, _heightProvider);
            if (spine.VertexCount < 2) return;

            var affectedTerrains = FindAffectedTerrains(spine);
            if (affectedTerrains.Count == 0) return;

            var spinePointsNative = new NativeArray<Vector3>(spine.points, Allocator.Persistent);
            var spineTangentsNative = new NativeArray<Vector3>(spine.tangents, Allocator.Persistent);
            var spineNormalsNative = new NativeArray<Vector3>(spine.surfaceNormals, Allocator.Persistent);
            var layerDataList = _creator.profile.layers;
            var layerDataNative = new NativeArray<ProfileSegmentData>(layerDataList.Count, Allocator.Persistent);
            for (int i = 0; i < layerDataList.Count; i++)
            {
                layerDataNative[i] = new ProfileSegmentData
                {
                    width = layerDataList[i].width,
                    horizontalOffset = layerDataList[i].horizontalOffset,
                    verticalOffset = layerDataList[i].verticalOffset
                };
            }

            try
            {
                // 步骤 1: 【分而治之】各自修改，但暂不拼接
                foreach (var terrain in affectedTerrains)
                {
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Apply Path To Terrain");
                    var terrainData = terrain.terrainData;
                    int resolution = terrainData.heightmapResolution;

                    var heights2D = terrainData.GetHeights(0, 0, resolution, resolution);
                    var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.Persistent);
                    try
                    {
                        for (int y = 0; y < resolution; y++)
                            for (int x = 0; x < resolution; x++)
                                heightsNative[y * resolution + x] = heights2D[y, x];

                        var heightJob = new ModifyHeightsJob
                        {
                            spinePoints = spinePointsNative,
                            spineTangents = spineTangentsNative,
                            spineNormals = spineNormalsNative,
                            pathLayers = layerDataNative,
                            terrainPos = terrain.GetPosition(),
                            heightmapRes = resolution,
                            heightmapSize = new Vector2(terrainData.size.x, terrainData.size.z),
                            terrainYSize = terrainData.size.y,
                            heights = heightsNative
                        };

                        await Task.Run(() =>
                        {
                            heightJob.Schedule(heightsNative.Length, 256).Complete();
                        });

                        for (int y = 0; y < resolution; y++)
                            for (int x = 0; x < resolution; x++)
                                heights2D[y, x] = heightsNative[y * resolution + x];

                        terrainData.SetHeights(0, 0, heights2D);
                        terrainData.SyncHeightmap();
                    }
                    finally
                    {
                        if (heightsNative.IsCreated) heightsNative.Dispose();
                    }
                }

                // 【【【 终 极 奥 义 】】】
                // 步骤 2: 号令天地，即刻固形！
                // 在设置邻居之前，强制所有受影响的地形将修改彻底应用。
                foreach (var terrain in affectedTerrains)
                {
                    terrain.Flush();
                }

                // 步骤 3: 【天地互联】此刻，再令其拼接，它们看到的将是彼此最完美的最终形态
                foreach (var terrain in affectedTerrains)
                {
                    terrain.SetNeighbors(
                        terrain.leftNeighbor, terrain.topNeighbor,
                        terrain.rightNeighbor, terrain.bottomNeighbor);
                }

                SceneView.RepaintAll();
            }
            finally
            {
                if (spinePointsNative.IsCreated) spinePointsNative.Dispose();
                if (spineTangentsNative.IsCreated) spineTangentsNative.Dispose();
                if (spineNormalsNative.IsCreated) spineNormalsNative.Dispose();
                if (layerDataNative.IsCreated) layerDataNative.Dispose();
            }
        }

        private List<Terrain> FindAffectedTerrains(PathSpine spine)
        {
            var terrains = new List<Terrain>();
            if (spine.VertexCount == 0) return terrains;

            Vector2 firstPoint2D = new Vector2(spine.points[0].x, spine.points[0].z);
            Rect pathBounds2D = new Rect(firstPoint2D, Vector2.zero);
            foreach (var point3D in spine.points)
            {
                pathBounds2D.xMin = Mathf.Min(pathBounds2D.xMin, point3D.x);
                pathBounds2D.yMin = Mathf.Min(pathBounds2D.yMin, point3D.z);
                pathBounds2D.xMax = Mathf.Max(pathBounds2D.xMax, point3D.x);
                pathBounds2D.yMax = Mathf.Max(pathBounds2D.yMax, point3D.z);
            }

            float maxWidth = 0;
            if (_creator.profile.layers.Count > 0)
                maxWidth = _creator.profile.layers.Max(l => l.width + Mathf.Abs(l.horizontalOffset) * 2);

            pathBounds2D = new Rect(
                pathBounds2D.x - maxWidth, pathBounds2D.y - maxWidth,
                pathBounds2D.width + maxWidth * 2, pathBounds2D.height + maxWidth * 2
            );

            foreach (var terrain in Terrain.activeTerrains)
            {
                var terrainData = terrain.terrainData;
                var terrainPos = terrain.GetPosition();
                var terrainBounds2D = new Rect(terrainPos.x, terrainPos.z, terrainData.size.x, terrainData.size.z);
                if (terrainBounds2D.Overlaps(pathBounds2D, true))
                {
                    terrains.Add(terrain);
                }
            }
            return terrains;
        }
    }
}