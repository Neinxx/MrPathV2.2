// FlattenTerrainCommand.cs (已修复内存泄漏)
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
namespace MrPathV2
{
    public class FlattenTerrainCommand : TerrainCommandBase
    {
        public FlattenTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }

        public override string GetCommandName() => "压平地形 (Flatten Terrain)";

        protected override async Task ProcessTerrains(List<Terrain> terrains, PathJobsUtility.SpineData spineData, NativeArray<float2> roadContour, float4 contourBounds)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp); // 短期使用，改为Temp
            var workItems = new List<(Terrain terrain, float[,] heights2D, NativeArray<float> heightsNative)>();

            // 【修正】移除 disposables 列表，直接管理 profileData
            var profileData = new PathJobsUtility.ProfileData(Creator.profile.layers, null, Creator.profile, Allocator.Persistent);

            try
            {
                foreach (var terrain in terrains)
                {
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var terrainData = terrain.terrainData;
                    var heights2D = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
                    var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.TempJob); // Job内使用，改为TempJob
                    Copy2DTo1D(heights2D, heightsNative, terrainData.heightmapResolution);

                    var job = new ModifyHeightsJob
                    {
                        spine = spineData,
                        profile = profileData,
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
                while (!combinedHandle.IsCompleted) await Task.Yield();
                combinedHandle.Complete();

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

                // 【修正】直接、安全地释放 profileData
                if (profileData.IsCreated) profileData.Dispose();

                // 修改地形后，标记高度缓存为脏，以便下次重建
                HeightProvider?.MarkAsDirty();
            }
        }

        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathJobsUtility.SpineData spineData, NativeArray<float2> roadContour, float4 contourBounds, System.Threading.CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain terrain, float[,] heights2D, NativeArray<float> heightsNative)>();
            var profileData = new PathJobsUtility.ProfileData(Creator.profile.layers, null, Creator.profile, Allocator.Persistent);
            try
            {
                foreach (var terrain in terrains)
                {
                    if (token.IsCancellationRequested) return;
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var terrainData = terrain.terrainData;
                    var heights2D = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
                    var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.TempJob);
                    Copy2DTo1D(heights2D, heightsNative, terrainData.heightmapResolution);

                    var job = new ModifyHeightsJob
                    {
                        spine = spineData,
                        profile = profileData,
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
                        Copy1DTo2D(item.heightsNative, item.heights2D, item.terrain.terrainData.heightmapResolution);
                        item.terrain.terrainData.SetHeights(0, 0, item.heights2D);
                    }
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems) { if (item.heightsNative.IsCreated) item.heightsNative.Dispose(); }
                if (profileData.IsCreated) profileData.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }

        private void Copy2DTo1D(float[,] source, NativeArray<float> dest, int res) { for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) dest[y * res + x] = source[y, x]; }
        private void Copy1DTo2D(NativeArray<float> source, float[,] dest, int res) { for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) dest[y, x] = source[y * res + x]; }
    }
}