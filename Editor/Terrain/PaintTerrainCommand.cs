// PaintTerrainCommand.cs (已修复内存泄漏)
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
namespace MrPathV2
{
    public class PaintTerrainCommand : TerrainCommandBase
    {
        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }

        public override string GetCommandName() => "绘制纹理 (Paint Textures)";

        protected override async Task ProcessTerrains(List<Terrain> terrains, PathJobsUtility.SpineData spineData, NativeArray<float2> roadContour, float4 contourBounds)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain terrain, float[,,] alphamaps3D, NativeArray<float> alphamapsNative)>();

            // 【修正】移除 disposables 列表
            var profileDataList = new List<PathJobsUtility.ProfileData>();

            try
            {
                foreach (var terrain in terrains)
                {
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var terrainData = terrain.terrainData;
                    if (terrainData.alphamapLayers == 0) continue;

                    var alphamaps3D = terrainData.GetAlphamaps(0, 0, terrainData.alphamapResolution, terrainData.alphamapResolution);
                    var alphamapsNative = new NativeArray<float>(alphamaps3D.Length, Allocator.TempJob);
                    Copy3DTo1D(alphamaps3D, alphamapsNative);

                    var layerMap = BuildTerrainLayerMap(terrain);
                    // 注意：这里我们为每个地形都创建了一个 profileData，因为 layerMap 不同
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
                while (!combinedHandle.IsCompleted) await Task.Yield();
                combinedHandle.Complete();

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

                // 【修正】直接、安全地释放所有 profileData
                foreach (var pd in profileDataList) { if (pd.IsCreated) pd.Dispose(); }

                // 修改地形后，标记高度缓存为脏，以便下次重建
                HeightProvider?.MarkAsDirty();
            }
        }

        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathJobsUtility.SpineData spineData, NativeArray<float2> roadContour, float4 contourBounds, System.Threading.CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain terrain, float[,,] alphamaps3D, NativeArray<float> alphamapsNative)>();
            var profileDataList = new List<PathJobsUtility.ProfileData>();
            try
            {
                foreach (var terrain in terrains)
                {
                    if (token.IsCancellationRequested) return;
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
                        alphamaps = alphamapsNative,
                        roadContour = roadContour,
                        contourBounds = contourBounds
                    };
                    var handle = job.Schedule(alphamapsNative.Length, 256);
                    handles.Add(handle);
                    workItems.Add((terrain, alphamaps3D, alphamapsNative));
                }

                var combinedHandle = JobHandle.CombineDependencies(handles.AsArray());
                while (!combinedHandle.IsCompleted)
                {
                    if (token.IsCancellationRequested)
                    {
                        // 仍需等待完成，避免资源释放冲突
                        break;
                    }
                    await Task.Yield();
                }
                combinedHandle.Complete();

                if (!token.IsCancellationRequested)
                {
                    foreach (var item in workItems)
                    {
                        Copy1DTo3D(item.alphamapsNative, item.alphamaps3D);
                        item.terrain.terrainData.SetAlphamaps(0, 0, item.alphamaps3D);
                    }
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems) { if (item.alphamapsNative.IsCreated) item.alphamapsNative.Dispose(); }
                foreach (var p in profileDataList) { if (p.IsCreated) p.Dispose(); }
                HeightProvider?.MarkAsDirty();
            }
        }

        private void Copy3DTo1D(float[,,] s, NativeArray<float> d) { int h = s.GetLength(0), w = s.GetLength(1), dp = s.GetLength(2); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int z = 0; z < dp; z++) d[y * w * dp + x * dp + z] = s[y, x, z]; }
        private void Copy1DTo3D(NativeArray<float> s, float[,,] d) { int h = d.GetLength(0), w = d.GetLength(1), dp = d.GetLength(2); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int z = 0; z < dp; z++) d[y, x, z] = s[y * w * dp + x * dp + z]; }
    }
}