using System;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Jobs;
using __temp.MrPathV2._2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using NativeArrayExtensions = __temp.MrPathV2._2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace __temp.MrPathV2._2.Runtime.Preview
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
            public PathJobsUtility.SpineData Spine;
            public PathJobsUtility.ProfileData Profile;
            public NativeArray<float3> Vertices;
            public NativeArray<float2> Uvs;
            public NativeArray<float4> Colors;
            public NativeArray<int> Indices;
            public int Segments;
            public RecipeData Recipe;
            public float2 Tiling;
            public float4 BaseColor;
            public bool IsValid;

            public JobData(PathSpine worldSpine, PathProfile profile, Allocator allocator)
            {
                Spine = default;
                this.Profile = default;
                Vertices = default;
                Uvs = default;
                Colors = default;
                Indices = default;
                Recipe = default;
                Tiling = new float2(1, 1);
                BaseColor = new float4(1, 1, 1, 1);

                // 默认最小分段数，保证基本形态，两侧+中线
                Segments = 2;
                if (profile != null)
                {
                    // 允许用户在 PathProfile 中配置更高分段数，但做安全上限，防止误设导致性能问题
                    const int maxSegments = 64;
                    Segments = math.clamp(profile.crossSectionSegments, 2, maxSegments);
                }
                IsValid = false;

                if (profile == null || worldSpine.VertexCount < 2)
                    return;

                try
                {
                    Spine = new PathJobsUtility.SpineData(worldSpine, allocator);
                    this.Profile = new PathJobsUtility.ProfileData(profile, allocator);
                    if (!Spine.IsCreated || Spine.Length < 2)
                    {
                        Dispose();
                        return;
                    }

                    var spineLen = Spine.Length;
                    var totalVertices = spineLen * Segments;
                    var totalQuads = (spineLen - 1) * (Segments - 1);
                    var totalIndices = totalQuads * 6;

                    if (totalVertices <= 0 || totalIndices <= 0)
                    {
                        Dispose();
                        return;
                    }

                    Vertices = NativeArrayExtensions.CreateTracked<float3>(totalVertices, allocator);
                    Uvs = NativeArrayExtensions.CreateTracked<float2>(totalVertices, allocator);
                    Colors = NativeArrayExtensions.CreateTracked<float4>(totalVertices, allocator);
                    Indices = NativeArrayExtensions.CreateTracked<int>(totalIndices, allocator);

                    var recipeSo = profile.roadRecipe;
                    Recipe = recipeSo ? RecipeJobsUtility.BakeRecipe(recipeSo, allocator) : RecipeJobsUtility.CreateDefaultRecipe(allocator);

                    // 计算 UV Tiling，使预览网格与材质保持一致
                    try
                    {
                        var worldWidth = profile.roadWidth;
                        var tileSizeX = 1f;
                        var tileSizeY = 1f;
                        if (profile.roadRecipe && profile.roadRecipe.blendLayers is { Count: > 0 })
                        {
                            var firstLayer = profile.roadRecipe.blendLayers[0];
                            if (firstLayer != null && firstLayer.terrainLayer)
                            {
                                var ts = firstLayer.terrainLayer.tileSize;
                                tileSizeX = ts.x != 0 ? ts.x : 1f;
                                tileSizeY = ts.y != 0 ? ts.y : 1f;
                            }
                        }
                        // X 方向：道路宽度对应的纹理重复次数
                        var tilingX = worldWidth / tileSizeX;

                        // Y 方向：道路长度对应的纹理重复次数
                        var pathLength = 0f;
                        for (var i = 1; i < worldSpine.VertexCount; i++)
                        {
                            pathLength += Vector3.Distance(worldSpine.points[i - 1], worldSpine.points[i]);
                        }
                        var tilingY = pathLength / tileSizeY;
                        if (tilingY <= 0f) tilingY = 1f;
                        if (tilingX <= 0f) tilingX = 1f;
                        Tiling = new float2(tilingX, tilingY);
                    }
                    catch { /* 安全兜底，保持默认 tiling=(1,1) */ }

                    IsValid = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"RoadPreviewMeshGenerator.JobData 创建失败: {ex.Message}");
                    Dispose();
                }
            }

            public void Dispose()
            {
                Vertices.SafeDispose();
                Uvs.SafeDispose();
                Colors.SafeDispose();
                Indices.SafeDispose();
                Spine.Dispose();
                Profile.Dispose();
                Recipe.Dispose();
                IsValid = false;
            }
        }

        // NativeCollectionManager 已弃用。当前类中的数据由 UnifiedMemory 与 NativeArrayExtensions 自动管理。
        // 保留占位以防后续需要显式资源管理器。
        // private readonly JobResourceManager _resourceMgr = new JobResourceManager();
        private JobData? _jobData;
        private JobHandle _combinedHandle;

        public GenerationState State { get; private set; } = GenerationState.Idle;

        public bool Start(PathSpine spine, PathProfile profile)
        {
            DisposeJob();
            _jobData = new JobData(spine, profile, Allocator.Persistent);
            if (!_jobData.Value.IsValid)
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
                    Spine = jd.Spine,
                    Profile = jd.Profile,
                    Vertices = jd.Vertices,
                    Uvs = jd.Uvs,
                    Segments = jd.Segments,
                    Tiling = jd.Tiling
                };
                var iJob = new GenerateIndicesJob
                {
                    Indices = jd.Indices,
                    Segments = jd.Segments,
                    SpineLength = jd.Spine.Length
                };
                var cJob = new GenerateVertexColorsJob
                {
                    Spine = jd.Spine,
                    Segments = jd.Segments,
                    Recipe = jd.Recipe,
                    Colors = jd.Colors,
                    BaseColor = jd.BaseColor
                };
                var hV = vJob.Schedule(jd.Vertices.Length, 64);
                var hI = iJob.Schedule(jd.Indices.Length / 6, 64);
                var hC = cJob.Schedule(jd.Colors.Length, 64);
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

        public NativeArray<float3> Vertices => _jobData?.Vertices ?? default;
        public NativeArray<float2> UVs => _jobData?.Uvs ?? default;
        public NativeArray<float4> Colors => _jobData?.Colors ?? default;
        public NativeArray<int> Indices => _jobData?.Indices ?? default;
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
            DisposeJob();
            DisposeJob();
            // _memMgr no longer used after deprecation of NativeCollectionManager
            DisposeJob();
        }
    }
}