using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Jobs;
using MrPathV2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using NativeArrayExtensions = MrPathV2.Runtime.Jobs.Extensions.NativeArrayExtensions;

// 新增：使用 CreateTracked 扩展

namespace MrPathV2.Editor.Terrain
{
    public class FlattenTerrainCommand : TerrainCommandBase
    {
        public FlattenTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }
        public override string GetCommandName() => "压平地形 (Flatten Terrain)";

        protected override async Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.TempJob);
            var workItems = new List<(UnityEngine.Terrain terrain, float[,] h, NativeArray<float> hn, NativeArray<float> ohn)>();

            // 直接分配NativeArray，不再使用对象池
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
            var profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
            RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
            // 优先使用外部提供的预览包围盒；若不可用，再回退到基于脊线+Profile 的二维 AABB
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                contourBounds = new float4(pb.x, pb.y, pb.z, pb.w);
            }
            else if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fallback = GetExpandedXZBounds(spine, Creator.profile);
                contourBounds = new float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }

            try
            {
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var td = terrain.terrainData;
                    var h2D = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
                    var hn = NativeArrayExtensions.CreateTracked<float>(h2D.Length, Allocator.Persistent);
                    var ohn = NativeArrayExtensions.CreateTracked<float>(h2D.Length, Allocator.Persistent);
                    Copy2DTo1D(h2D, hn, td.heightmapResolution);
                    Copy2DTo1D(h2D, ohn, td.heightmapResolution);

                    var job = new ModifyHeightsJob
                    {
                        Spine = spineData,
                        Profile = profileData,
                        TerrainPos = terrain.GetPosition(),
                        TerrainSize = td.size,
                        HeightmapResolution = td.heightmapResolution,
                        Heights = hn,
                        OriginalHeights = ohn,
                        RoadContour = roadContour,
                        ContourBounds = contourBounds
                    };
                    var h = job.Schedule(hn.Length, 256);
                    handles.Add(h);
                    workItems.Add((terrain, h2D, hn, ohn));
                }

                var combinedHandle = JobHandle.CombineDependencies(handles.AsArray());
                while (!combinedHandle.IsCompleted)
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                }
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
                if (handles.IsCreated) handles.SafeDispose();
                foreach (var item in workItems)
                {
                    if (item.hn.IsCreated) item.hn.SafeDispose();
                    if (item.ohn.IsCreated) item.ohn.SafeDispose();
                }
                if (spineData.IsCreated) spineData.Dispose();
                if (profileData.IsCreated) profileData.Dispose();
                if (roadContour.IsCreated) roadContour.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }
        private static void Copy2DTo1D(float[,] s, NativeArray<float> d, int r)
        {
            for (var y = 0; y < r; y++)
            for (var x = 0; x < r; x++)
            {
                d[y * r + x] = s[y, x];
            }
        }
        private static void Copy1DTo2D(NativeArray<float> s, float[,] d, int r)
        {
            for (var y = 0; y < r; y++)
            for (var x = 0; x < r; x++)
            {
                d[y, x] = s[y * r + x];
            }
        }
    }
}
