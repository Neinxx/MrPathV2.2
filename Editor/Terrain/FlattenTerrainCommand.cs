// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/FlattenTerrainCommand.cs (最终修正版)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;

namespace MrPathV2
{
    public class FlattenTerrainCommand : TerrainCommandBase
    {
        public FlattenTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }

        public override string GetCommandName() => "压平地形 (Flatten Terrain)";

        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spineForContour, NativeArray<float3> meshVertices, NativeArray<int> meshTriangles, CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain terrain, float[,] heights2D, NativeArray<float> heightsNative)>();

            RoadContourGenerator.GenerateContour(spineForContour, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);

            try
            {
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());

                    var terrainData = terrain.terrainData;
                    var heights2D = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
                    var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.TempJob);
                    Copy2DTo1D(heights2D, heightsNative, terrainData.heightmapResolution);

                    var job = new ModifyHeightsJob
                    {
                        meshVertices = meshVertices,
                        meshTriangles = meshTriangles,
                        terrainPos = terrain.GetPosition(),
                        terrainSize = terrainData.size,
                        heightmapResolution = terrainData.heightmapResolution,
                        heights = heightsNative,
                        roadContour = roadContour,
                        contourBounds = contourBounds
                    };

                    var handle = job.Schedule(heightsNative.Length, 256);
                    handles.Add(handle);
                    workItems.Add((terrain, heights2D, heightsNative));
                }

                var combinedHandle = JobHandle.CombineDependencies(handles.AsArray());
                while (!combinedHandle.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                combinedHandle.Complete();

                token.ThrowIfCancellationRequested();

                foreach (var item in workItems)
                {
                    Copy1DTo2D(item.heightsNative, item.heights2D, item.terrain.terrainData.heightmapResolution);
                    item.terrain.terrainData.SetHeights(0, 0, item.heights2D);
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems) { if (item.heightsNative.IsCreated) item.heightsNative.Dispose(); }
                if (roadContour.IsCreated) roadContour.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }

        private void Copy2DTo1D(float[,] source, NativeArray<float> dest, int res) { for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) dest[y * res + x] = source[y, x]; }
        private void Copy1DTo2D(NativeArray<float> source, float[,] dest, int res) { for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) dest[y, x] = source[y * res + x]; }
    }
}