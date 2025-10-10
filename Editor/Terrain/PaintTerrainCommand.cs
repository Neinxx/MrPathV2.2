// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/PaintTerrainCommand.cs (最终修正版)
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using System.Threading;

namespace MrPathV2
{
    public class PaintTerrainCommand : TerrainCommandBase
    {
        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }

        public override string GetCommandName() => "绘制纹理 (Paint Textures)";

        /// <summary>
        /// 【最终修正】实现与基类完全匹配的新方法。
        /// </summary>
        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spineForContour, NativeArray<float3> meshVertices, NativeArray<int> meshTriangles, CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain terrain, float[,,] alphamaps3D, NativeArray<float> alphamapsNative)>();
            var profileDataList = new List<PathJobsUtility.ProfileData>();

            RoadContourGenerator.GenerateContour(spineForContour, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
            var spineData = new PathJobsUtility.SpineData(spineForContour, Allocator.Persistent); // Paint Job 仍需要 SpineData

            try
            {
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var terrainData = terrain.terrainData;
                    if (terrainData.alphamapLayers == 0) continue;

                    var alphamaps3D = terrainData.GetAlphamaps(0, 0, terrainData.alphamapResolution, terrainData.alphamapResolution);
                    var alphamapsNative = new NativeArray<float>(alphamaps3D.Length, Allocator.TempJob);
                    Copy3DTo1D(alphamaps3D, alphamapsNative);

                    var layerMap = BuildTerrainLayerMap(terrain);
                    var profileData = new PathJobsUtility.ProfileData(Creator.profile.layers, layerMap, Creator.profile, Allocator.Persistent);
                    profileDataList.Add(profileData);

                    var job = new ModifyAlphamapsJob
                    {
                        spine = spineData,
                        profile = profileData,
                        terrainPos = terrain.GetPosition(),
                        terrainSize = terrainData.size,
                        alphamapResolution = terrainData.alphamapResolution,
                        alphamapLayerCount = terrainData.alphamapLayers,
                        alphamaps = alphamapsNative,
                        roadContour = roadContour,
                        contourBounds = contourBounds
                    };
                    var handle = job.Schedule(terrainData.alphamapResolution * terrainData.alphamapResolution, 256);
                    handles.Add(handle);
                    workItems.Add((terrain, alphamaps3D, alphamapsNative));
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
                    Copy1DTo3D(item.alphamapsNative, item.alphamaps3D);
                    item.terrain.terrainData.SetAlphamaps(0, 0, item.alphamaps3D);
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems) { if (item.alphamapsNative.IsCreated) item.alphamapsNative.Dispose(); }
                foreach (var p in profileDataList) { if (p.IsCreated) p.Dispose(); }
                if (roadContour.IsCreated) roadContour.Dispose();
                if (spineData.IsCreated) spineData.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }

        private void Copy3DTo1D(float[,,] s, NativeArray<float> d) { int h = s.GetLength(0), w = s.GetLength(1), dp = s.GetLength(2); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int z = 0; z < dp; z++) d[y * w * dp + x * dp + z] = s[y, x, z]; }
        private void Copy1DTo3D(NativeArray<float> s, float[,,] d) { int h = d.GetLength(0), w = d.GetLength(1), dp = d.GetLength(2); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int z = 0; z < dp; z++) d[y, x, z] = s[y * w * dp + x * dp + z]; }
    }
}