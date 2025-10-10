// PreviewMeshController.cs (终极健壮、工整版)
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using System;
namespace MrPathV2
{

    public class PreviewMeshController : System.IDisposable
    {
        private struct JobData : IDisposable
        {
            public PathJobsUtility.SpineData spine;
            public PathJobsUtility.ProfileData profile;
            public NativeList<float3> vertices;
            public NativeList<int> triangles;
            public NativeList<float2> uvs;
            public NativeList<Color32> colors;
            public NativeList<float3> normals;

            public JobData(PathSpine worldSpine, PathProfile profile, Allocator allocator)
            {
                this.profile = new PathJobsUtility.ProfileData(profile.layers, null, profile, allocator);
                spine = new PathJobsUtility.SpineData(worldSpine, allocator);
                vertices = new NativeList<float3>(allocator);
                triangles = new NativeList<int>(allocator);
                uvs = new NativeList<float2>(allocator);
                colors = new NativeList<Color32>(allocator);
                normals = new NativeList<float3>(allocator);
            }

            public void Dispose()
            {
                if (spine.IsCreated) spine.Dispose();
                if (profile.IsCreated) profile.Dispose();
                if (vertices.IsCreated) vertices.Dispose();
                if (triangles.IsCreated) triangles.Dispose();
                if (uvs.IsCreated) uvs.Dispose();
                if (colors.IsCreated) colors.Dispose();
                if (normals.IsCreated) normals.Dispose();
            }
        }

        public Mesh PreviewMesh { get; private set; }

        private JobHandle m_MeshUpdateJobHandle;
        private bool m_IsJobRunning = false;
        private JobData? m_CurrentJobData; // 【核心】使用可空结构体

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
                m_CurrentJobData = null; // 清理后，设置为null
            }
        }

        public void StartMeshGeneration(PathSpine worldSpine, PathProfile profile)
        {
            CompleteAndDisposeJob();

            if (worldSpine.VertexCount < 2 || profile?.layers == null || profile.layers.Count == 0)
            {
                PreviewMesh.Clear();
                return;
            }

            m_CurrentJobData = new JobData(worldSpine, profile, Allocator.Persistent);
            var jobData = m_CurrentJobData.Value;

            var job = new GenerateMeshJob
            {
                spine = jobData.spine,
                profile = jobData.profile,
                vertices = jobData.vertices,
                uvs = jobData.uvs,
                colors = jobData.colors,
                triangles = jobData.triangles,
                normals = jobData.normals
            };
            m_MeshUpdateJobHandle = job.Schedule();
            m_IsJobRunning = true;
        }

        public bool TryFinalizeMesh()
        {
            if (m_IsJobRunning && m_MeshUpdateJobHandle.IsCompleted)
            {
                m_MeshUpdateJobHandle.Complete();
                m_IsJobRunning = false;

                PreviewMesh.Clear();

                if (m_CurrentJobData.HasValue)
                {
                    var jobData = m_CurrentJobData.Value;
                    if (jobData.vertices.Length > 0 && jobData.triangles.Length > 0)
                    {
                        PreviewMesh.SetVertices(jobData.vertices.AsArray());
                        PreviewMesh.SetUVs(0, jobData.uvs.AsArray());
                        PreviewMesh.SetColors(jobData.colors.AsArray());
                        PreviewMesh.SetNormals(jobData.normals.AsArray());
                        PreviewMesh.SetTriangles(jobData.triangles.ToArray(), 0, false);
                        PreviewMesh.RecalculateBounds();
                    }
                }

                return true;
            }
            return false;
        }

        public void Dispose()
        {
            CompleteAndDisposeJob();
            if (PreviewMesh != null) UnityEngine.Object.DestroyImmediate(PreviewMesh);
        }
    }
}