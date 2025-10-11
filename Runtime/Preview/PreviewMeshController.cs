// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Preview/PreviewMeshController.cs (最终统一版)
using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace MrPathV2
{
    public class PreviewMeshController : IDisposable
    {
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

            public JobData(PathSpine worldSpine, PathProfile profile, Allocator allocator)
            {
                this.profile = new PathJobsUtility.ProfileData(profile,  allocator);
                this.spine = new PathJobsUtility.SpineData(worldSpine, allocator);
                // 高性能带状网格：强制使用2段（左/右）
                this.segments = 2;
                int spineLen = this.spine.Length;
                if (spineLen < 2 || this.segments < 2)
                {
                    this.vertices = default;
                    this.uvs = default;
                    this.colors = default;
                    this.indices = default;
                    this.recipe = default;
                    return;
                }
                int totalVertices = spineLen * this.segments;
                int totalQuads = (spineLen - 1) * (this.segments - 1);
                int totalIndices = totalQuads * 6;
                this.vertices = new NativeArray<float3>(totalVertices, allocator, NativeArrayOptions.UninitializedMemory);
                this.uvs = new NativeArray<float2>(totalVertices, allocator, NativeArrayOptions.UninitializedMemory);
                this.colors = new NativeArray<float4>(totalVertices, allocator, NativeArrayOptions.UninitializedMemory);
                this.indices = new NativeArray<int>(totalIndices, allocator, NativeArrayOptions.UninitializedMemory);

                // 烘焙配方为共享数据结构（预览材质不需要 TerrainLayer 映射，填 -1 即可）
                var recipeSO = profile?.roadRecipe;
                var terrainMap = new System.Collections.Generic.Dictionary<TerrainLayer, int>();
                this.recipe = new RecipeData(recipeSO, terrainMap, allocator);
            }

            public void Dispose()
            {
                if (spine.IsCreated) spine.Dispose();
                if (profile.IsCreated) profile.Dispose();
                if (vertices.IsCreated) vertices.Dispose();
                if (uvs.IsCreated) uvs.Dispose();
                if (colors.IsCreated) colors.Dispose();
                if (indices.IsCreated) indices.Dispose();
                recipe.Dispose();
            }
        }

        public Mesh PreviewMesh { get; private set; }

        private JobHandle m_MeshUpdateJobHandle;
        private bool m_IsJobRunning = false;
        private JobData? m_CurrentJobData;

        public PreviewMeshController()
        {
            PreviewMesh = new Mesh { name = "Path Preview Mesh" };
            PreviewMesh.MarkDynamic();
            PreviewMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        private void CompleteAndDisposeJob()
        {
            if (m_IsJobRunning)
            {
                m_MeshUpdateJobHandle.Complete();
                m_IsJobRunning = false;
            }
            if (m_CurrentJobData.HasValue)
            {
                m_CurrentJobData.Value.Dispose();
                m_CurrentJobData = null;
            }
        }

        public void StartMeshGeneration(PathSpine worldSpine, PathProfile profile)
        {
            CompleteAndDisposeJob();
            if (worldSpine.VertexCount < 2 || profile == null)
            {
                PreviewMesh.Clear();
                return;
            }

            m_CurrentJobData = new JobData(worldSpine, profile, Allocator.Persistent);
            var jobData = m_CurrentJobData.Value;
            if (!jobData.spine.IsCreated || jobData.segments < 2 || jobData.spine.Length < 2)
            {
                PreviewMesh.Clear();
                return;
            }

            var vJob = new GenerateVerticesJob
            {
                spine = jobData.spine,
                profile = jobData.profile,
                vertices = jobData.vertices,
                uvs = jobData.uvs,
                segments = jobData.segments
            };
            var iJob = new GenerateIndicesJob
            {
                indices = jobData.indices,
                segments = jobData.segments,
                spineLength = jobData.spine.Length
            };
            var cJob = new GenerateVertexColorsJob
            {
                spine = jobData.spine,
                segments = jobData.segments,
                recipe = jobData.recipe,
                colors = jobData.colors
            };
            var handleV = vJob.Schedule(jobData.vertices.Length, 64);
            var handleI = iJob.Schedule(jobData.indices.Length / 6, 64);
            var handleC = cJob.Schedule(jobData.colors.Length, 64);
            m_MeshUpdateJobHandle = JobHandle.CombineDependencies(handleV, handleI, handleC);
            m_IsJobRunning = true;
        }

        public bool TryFinalizeMesh()
        {
            if (m_IsJobRunning && m_MeshUpdateJobHandle.IsCompleted)
            {
                m_MeshUpdateJobHandle.Complete();
                m_IsJobRunning = false;

                PreviewMesh.Clear(false);
                if (m_CurrentJobData.HasValue)
                {
                    var jobData = m_CurrentJobData.Value;
                    if (jobData.vertices.IsCreated && jobData.indices.IsCreated && jobData.vertices.Length > 0 && jobData.indices.Length > 0)
                    {
                        int vertexCount = jobData.vertices.Length;
                        int indexCount = jobData.indices.Length;
                        var indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                        PreviewMesh.indexFormat = indexFormat;

                        try
                        {
                            // 顶点缓冲：Position(float3) + TexCoord0(float2)，双流
                            var layout = new VertexAttributeDescriptor[]
                            {
                                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream: 1),
                                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, stream: 2)
                            };
                            PreviewMesh.SetVertexBufferParams(vertexCount, layout);

                            // 写入 Position 到 stream0
                            PreviewMesh.SetVertexBufferData(jobData.vertices, 0, 0, vertexCount, stream: 0, flags: MeshUpdateFlags.DontRecalculateBounds);
                            // 写入 UV 到 stream1
                            PreviewMesh.SetVertexBufferData(jobData.uvs, 0, 0, vertexCount, stream: 1, flags: MeshUpdateFlags.DontRecalculateBounds);
                            // 写入 Color 到 stream2
                            PreviewMesh.SetVertexBufferData(jobData.colors, 0, 0, vertexCount, stream: 2, flags: MeshUpdateFlags.DontRecalculateBounds);

                            // 索引缓冲
                            PreviewMesh.SetIndexBufferParams(indexCount, indexFormat);
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                var indices16 = new NativeArray<ushort>(indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                                for (int k = 0; k < indexCount; k++) indices16[k] = (ushort)jobData.indices[k];
                                PreviewMesh.SetIndexBufferData(indices16, 0, 0, indexCount, flags: MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                                indices16.Dispose();
                            }
                            else
                            {
                                PreviewMesh.SetIndexBufferData(jobData.indices, 0, 0, indexCount, flags: MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                            }

                            // SubMesh 描述
                            PreviewMesh.subMeshCount = 1;
                            var subMeshDesc = new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
                            {
                                vertexCount = vertexCount
                            };
                            PreviewMesh.SetSubMesh(0, subMeshDesc, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

                            PreviewMesh.RecalculateBounds();
                            PreviewMesh.UploadMeshData(false);
                        }
                        catch (Exception)
                        {
                            // 兼容旧版本API：回退为 SetVertices/SetUVs/SetIndices
                            var verts = new System.Collections.Generic.List<Vector3>(vertexCount);
                            var uvsList = new System.Collections.Generic.List<Vector2>(vertexCount);
                            var colorsList = new System.Collections.Generic.List<Color>(vertexCount);
                            for (int i = 0; i < vertexCount; i++)
                            {
                                float3 v = jobData.vertices[i];
                                verts.Add(new Vector3(v.x, v.y, v.z));
                                float2 uv = jobData.uvs[i];
                                uvsList.Add(new Vector2(uv.x, uv.y));
                                float4 c = jobData.colors[i];
                                colorsList.Add(new Color(c.x, c.y, c.z, c.w));
                            }
                            var indicesManaged = new int[indexCount];
                            for (int i = 0; i < indexCount; i++)
                                indicesManaged[i] = jobData.indices[i];

                            PreviewMesh.Clear(false);
                            PreviewMesh.indexFormat = indexFormat;
                            PreviewMesh.SetVertices(verts);
                            PreviewMesh.SetUVs(0, uvsList);
                            PreviewMesh.SetColors(colorsList);
                            PreviewMesh.SetIndices(indicesManaged, MeshTopology.Triangles, 0, false);
                            PreviewMesh.RecalculateBounds();
                            PreviewMesh.UploadMeshData(false);
                        }
                    }
                }
                
                CompleteAndDisposeJob();
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            CompleteAndDisposeJob();
            if (PreviewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(PreviewMesh);
            }
        }
    }
}