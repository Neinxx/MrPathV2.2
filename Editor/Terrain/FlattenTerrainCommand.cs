// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/FlattenTerrainCommand.cs (最终统一版)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    public class FlattenTerrainCommand : TerrainCommandBase
    {
        public FlattenTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }
        public override string GetCommandName() => "压平地形 (Flatten Terrain)";

        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.TempJob);
            var workItems = new List<(Terrain terrain, float[,] h, NativeArray<float> hn, NativeArray<float> ohn)>();

            // 直接分配NativeArray，不再使用对象池
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
            var profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
            RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
            // 优先使用外部提供的预览包围盒；若不可用，再回退到基于脊线+Profile 的二维 AABB
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                contourBounds = new Unity.Mathematics.float4(pb.x, pb.y, pb.z, pb.w);
            }
            else if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fallback = GetExpandedXZBounds(spine, Creator.profile);
                contourBounds = new Unity.Mathematics.float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }

            try
            {
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var td = terrain.terrainData;
                    var h2D = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
                    var hn = new NativeArray<float>(h2D.Length, Allocator.Persistent);
                    var ohn = new NativeArray<float>(h2D.Length, Allocator.Persistent);
                    Copy2DTo1D(h2D, hn, td.heightmapResolution);
                    Copy2DTo1D(h2D, ohn, td.heightmapResolution);

                    var job = new ModifyHeightsJob
                    {
                        spine = spineData,
                        profile = profileData,
                        terrainPos = terrain.GetPosition(),
                        terrainSize = td.size,
                        heightmapResolution = td.heightmapResolution,
                        heights = hn,
                        originalHeights = ohn,
                        roadContour = roadContour,
                        contourBounds = contourBounds
                    };
                    var h = job.Schedule(hn.Length, 256);
                    handles.Add(h);
                    workItems.Add((terrain, h2D, hn, ohn));
                }

                var combinedHandle = JobHandle.CombineDependencies(handles.AsArray());
                while (!combinedHandle.IsCompleted) { await Task.Yield(); token.ThrowIfCancellationRequested(); }
                combinedHandle.Complete();
                token.ThrowIfCancellationRequested();

                foreach (var item in workItems)
                {
                    Copy1DTo2D(item.hn, item.h, item.terrain.terrainData.heightmapResolution);
                    item.terrain.terrainData.SetHeights(0, 0, item.h);
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems)
                {
                    if (item.hn.IsCreated) item.hn.Dispose();
                    if (item.ohn.IsCreated) item.ohn.Dispose();
                }
                if (spineData.IsCreated) spineData.Dispose();
                if (profileData.IsCreated) profileData.Dispose();
                if (roadContour.IsCreated) roadContour.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }
        private void Copy2DTo1D(float[,] s, NativeArray<float> d, int r) { for (int y=0;y<r;y++) for(int x=0;x<r;x++) d[y*r+x]=s[y,x]; }
        private void Copy1DTo2D(NativeArray<float> s, float[,] d, int r) { for (int y=0;y<r;y++) for(int x=0;x<r;x++) d[y,x]=s[y*r+x]; }
    }
}