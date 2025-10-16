using System;
using MrPathV2.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 负责生成道路预览网格数据的纯 Job 调度器，仅负责数据计算，不涉及 Mesh 对象或渲染。
    /// 生成完成后，可通过 Vertices / Indices 等 NativeArray 读取结果。
    /// 使用者需调用 Dispose 以释放底层 NativeArray 资源。
    /// </summary>
    public sealed class RoadPreviewMeshGenerator : IDisposable
    {
        public enum GenerationState
        {
            Idle,
            Generating,
            Ready,
            Failed
        }

        private struct JobData : IDisposable
        {
            public PathJobsUtility.SpineData spine;
            public PathJobsUtility.ProfileData profile;
            public NativeArray<float3> vertices;
            public NativeArray<float2> uvs;
            public NativeArray<float4> colors;
            public NativeArray<int> indices;
            public int segments;
            public RecipeData recipe;
            public float2 tiling;
            public float4 baseColor;
            public bool isValid;

            public JobData(PathSpine worldSpine, PathProfile profile, Allocator allocator, NativeCollectionManager memMgr)
            {
                spine = default;
                this.profile = default;
                vertices = default;
                uvs = default;
                colors = default;
                indices = default;
                recipe = default;
                tiling = new float2(1, 1);
                baseColor = new float4(1, 1, 1, 1);

                // 默认最小分段数，保证基本形态，两侧+中线
                segments = 2;
                if (profile != null)
                {
                    // 允许用户在 PathProfile 中配置更高分段数，但做安全上限，防止误设导致性能问题
                    const int MaxSegments = 64;
                    segments = math.clamp(profile.crossSectionSegments, 2, MaxSegments);
                }
                isValid = false;

                if (profile == null || worldSpine.VertexCount < 2)
                    return;

                try
                {
                    spine = new PathJobsUtility.SpineData(worldSpine, allocator);
                    this.profile = new PathJobsUtility.ProfileData(profile, allocator);
                    if (!spine.IsCreated || spine.Length < 2)
                    {
                        Dispose();
                        return;
                    }

                    int spineLen = spine.Length;
                    int totalVertices = spineLen * segments;
                    int totalQuads = (spineLen - 1) * (segments - 1);
                    int totalIndices = totalQuads * 6;

                    if (totalVertices <= 0 || totalIndices <= 0)
                    {
                        Dispose();
                        return;
                    }

                    vertices = memMgr.CreateNativeArray<float3>(totalVertices, allocator, "MeshVertices");
                    uvs = memMgr.CreateNativeArray<float2>(totalVertices, allocator, "MeshUVs");
                    colors = memMgr.CreateNativeArray<float4>(totalVertices, allocator, "MeshColors");
                    indices = memMgr.CreateNativeArray<int>(totalIndices, allocator, "MeshIndices");

                    var recipeSO = profile?.roadRecipe;
                    if (recipeSO != null)
                        recipe = RecipeJobsUtility.BakeRecipe(recipeSO, allocator, -1);
                    else
                        recipe = RecipeJobsUtility.CreateDefaultRecipe(allocator);

                    // 计算 UV Tiling，使预览网格与材质保持一致
                    try
                    {
                        float worldWidth = profile.roadWidth;
                        float tileSizeX = 1f;
                        float tileSizeY = 1f;
                        if (profile.roadRecipe != null && profile.roadRecipe.blendLayers != null && profile.roadRecipe.blendLayers.Count > 0)
                        {
                            var firstLayer = profile.roadRecipe.blendLayers[0];
                            if (firstLayer != null && firstLayer.terrainLayer != null)
                            {
                                var ts = firstLayer.terrainLayer.tileSize;
                                tileSizeX = ts.x != 0 ? ts.x : 1f;
                                tileSizeY = ts.y != 0 ? ts.y : 1f;
                            }
                        }
                        // X 方向：道路宽度对应的纹理重复次数
                        float tilingX = worldWidth / tileSizeX;

                        // Y 方向：道路长度对应的纹理重复次数
                        float pathLength = 0f;
                        for (int i = 1; i < worldSpine.VertexCount; i++)
                        {
                            pathLength += UnityEngine.Vector3.Distance(worldSpine.points[i - 1], worldSpine.points[i]);
                        }
                        float tilingY = pathLength / tileSizeY;
                        if (tilingY <= 0f) tilingY = 1f;
                        if (tilingX <= 0f) tilingX = 1f;
                        tiling = new float2(tilingX, tilingY);
                    }
                    catch { /* 安全兜底，保持默认 tiling=(1,1) */ }

                    isValid = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"RoadPreviewMeshGenerator.JobData 创建失败: {ex.Message}");
                    Dispose();
                }
            }

            public void Dispose()
            {
                vertices.SafeDispose();
                uvs.SafeDispose();
                colors.SafeDispose();
                indices.SafeDispose();
                spine.Dispose();
                profile.Dispose();
                recipe.Dispose();
                isValid = false;
            }
        }

        private readonly NativeCollectionManager _memMgr = new NativeCollectionManager();
        private JobData? _jobData;
        private JobHandle _combinedHandle;

        public GenerationState State { get; private set; } = GenerationState.Idle;

        public bool Start(PathSpine spine, PathProfile profile)
        {
            DisposeJob();
            _jobData = new JobData(spine, profile, Allocator.Persistent, _memMgr);
            if (!_jobData.Value.isValid)
            {
                State = GenerationState.Failed;
                _jobData.Value.Dispose();
                _jobData = null;
                return false;
            }

            try
            {
                var jd = _jobData.Value;
                var vJob = new GenerateVerticesJob
                {
                    spine = jd.spine,
                    profile = jd.profile,
                    vertices = jd.vertices,
                    uvs = jd.uvs,
                    segments = jd.segments,
                    tiling = jd.tiling
                };
                var iJob = new GenerateIndicesJob
                {
                    indices = jd.indices,
                    segments = jd.segments,
                    spineLength = jd.spine.Length
                };
                var cJob = new GenerateVertexColorsJob
                {
                    spine = jd.spine,
                    segments = jd.segments,
                    recipe = jd.recipe,
                    colors = jd.colors,
                    baseColor = jd.baseColor
                };
                var hV = vJob.Schedule(jd.vertices.Length, 64);
                var hI = iJob.Schedule(jd.indices.Length / 6, 64);
                var hC = cJob.Schedule(jd.colors.Length, 64);
                _combinedHandle = JobHandle.CombineDependencies(hV, hI, hC);
                State = GenerationState.Generating;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"RoadPreviewMeshGenerator 调度失败: {ex.Message}");
                State = GenerationState.Failed;
                DisposeJob();
                return false;
            }
        }

        public bool TryComplete()
        {
            if (State == GenerationState.Generating)
            {
                if (_combinedHandle.IsCompleted)
                {
                    _combinedHandle.Complete();
                    State = GenerationState.Ready;
                }
                else return false;
            }
            return State == GenerationState.Ready;
        }

        public bool ForceComplete()
        {
            if (State == GenerationState.Generating)
            {
                try
                {
                    _combinedHandle.Complete();
                    State = GenerationState.Ready;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"RoadPreviewMeshGenerator 强制完成失败: {ex.Message}");
                    State = GenerationState.Failed;
                    DisposeJob();
                    return false;
                }
            }
            return State == GenerationState.Ready;
        }

        public NativeArray<float3> Vertices => _jobData?.vertices ?? default;
        public NativeArray<float2> UVs => _jobData?.uvs ?? default;
        public NativeArray<float4> Colors => _jobData?.colors ?? default;
        public NativeArray<int> Indices => _jobData?.indices ?? default;
        public int VertexCount => Vertices.IsCreated ? Vertices.Length : 0;
        public int IndexCount => Indices.IsCreated ? Indices.Length : 0;

        private void DisposeJob()
        {
            if (_jobData.HasValue)
            {
                if (State == GenerationState.Generating)
                    _combinedHandle.Complete();
                _jobData.Value.Dispose();
                _jobData = null;
            }
            State = GenerationState.Idle;
        }

        public void Dispose()
        {
            DisposeJob();
            _memMgr.Dispose();
        }
    }
}