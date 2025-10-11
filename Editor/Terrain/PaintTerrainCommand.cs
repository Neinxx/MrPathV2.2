// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/PaintTerrainCommand.cs (最终统一版)
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using System.Threading;
using MrPathV2;

namespace MrPathV2
{
    public class PaintTerrainCommand : TerrainCommandBase
    {
        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider) : base(creator, heightProvider) { }
        public override string GetCommandName() => "绘制纹理 (Paint Textures)";
        
        protected override async Task ProcessTerrainsAsync(List<Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            var handles = new NativeList<JobHandle>(Allocator.Temp);
            var workItems = new List<(Terrain t, float[,,] a3D, NativeArray<float> an)>();
            var profileDataList = new List<PathJobsUtility.ProfileData>();
            var recipeDataList = new List<RecipeData>();

            RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
            // 优先使用外部提供的预览包围盒；若不可用，再回退到基于脊线+Profile 的二维 AABB
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                contourBounds = new Unity.Mathematics.float4(pb.x, pb.y, pb.z, pb.w);
            }
            else if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fallback = TerrainCommandBase.GetExpandedXZBounds(spine, Creator.profile);
                contourBounds = new Unity.Mathematics.float4(fallback.x, fallback.y, fallback.z, fallback.w);
            }
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);

            try
            {
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());
                    var td = terrain.terrainData;

                    var recipeAsset = Creator.profile.roadRecipe;
                    if (recipeAsset == null)
                    {
                        Debug.LogWarning("未配置道路配方(StylizedRoadRecipe)，跳过该地形的纹理绘制。");
                        continue;
                    }

                    // 确保地形包含配方中的 TerrainLayer（自动添加缺失图层）
                    var layerMap = LayerResolver.Resolve(terrain, recipeAsset);
                    // 重新读取图层数量，避免在添加前因0而提前跳过
                    if (td.alphamapLayers == 0)
                    {
                        Debug.LogWarning($"地形 \"{terrain.name}\" 当前不含任何 TerrainLayer，且配方未能添加有效图层，跳过纹理绘制。");
                        continue;
                    }

                    var a3D = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
                    var an = new NativeArray<float>(a3D.Length, Allocator.TempJob);
                    Copy3DTo1D(a3D, an);

                    var pData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
                    profileDataList.Add(pData);
                    var rData = new RecipeData(recipeAsset, layerMap, Allocator.Persistent);
                    recipeDataList.Add(rData);

                    var job = new PaintSplatmapJob
                    {
                        spine = spineData,
                        profile = pData,
                        recipe = rData,
                        terrainPos = terrain.GetPosition(),
                        terrainSize = td.size,
                        alphamapResolution = td.alphamapResolution,
                        alphamapLayerCount = td.alphamapLayers,
                        alphamaps = an,
                        roadContour = roadContour,
                        contourBounds = contourBounds
                    };
                    handles.Add(job.Schedule(td.alphamapResolution * td.alphamapResolution, 256));
                    workItems.Add((terrain, a3D, an));
                }

                var combined = JobHandle.CombineDependencies(handles.AsArray());
                while (!combined.IsCompleted) { await Task.Yield(); token.ThrowIfCancellationRequested(); }
                combined.Complete();
                token.ThrowIfCancellationRequested();

                foreach (var item in workItems)
                {
                    Copy1DTo3D(item.an, item.a3D);
                    item.t.terrainData.SetAlphamaps(0, 0, item.a3D);
                }
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                foreach (var item in workItems) { if (item.an.IsCreated) item.an.Dispose(); }
                foreach (var p in profileDataList) { if (p.IsCreated) p.Dispose(); }
                foreach (var r in recipeDataList) { r.Dispose(); }
                if (roadContour.IsCreated) roadContour.Dispose();
                if (spineData.IsCreated) spineData.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }
        private void Copy3DTo1D(float[,,]s,NativeArray<float>d){int h=s.GetLength(0),w=s.GetLength(1),dp=s.GetLength(2);for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int z=0;z<dp;z++)d[y*w*dp+x*dp+z]=s[y,x,z];}
        private void Copy1DTo3D(NativeArray<float>s,float[,,]d){int h=d.GetLength(0),w=d.GetLength(1),dp=d.GetLength(2);for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int z=0;z<dp;z++)d[y,x,z]=s[y*w*dp+x*dp+z];}
    }
}