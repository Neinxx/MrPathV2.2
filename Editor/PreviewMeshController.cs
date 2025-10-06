using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 【至臻纯净版】纯粹的、可复用的异步网格生成器。
/// 它与路径系统完全解耦，只负责将路径骨架和分段几何数据转换为网格。
/// </summary>
public class PreviewMeshController : System.IDisposable
{
    #region 公共接口 (Public Interface)

    /// <summary>
    /// 获取最终生成的预览网格。
    /// </summary>
    public Mesh PreviewMesh { get; private set; }

    private JobHandle m_MeshUpdateJobHandle;
    private bool m_IsJobRunning = false;

    public PreviewMeshController()
    {
        PreviewMesh = new Mesh { name = "Path Preview Mesh" };
        PreviewMesh.hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>
    /// 核心公共接口：接收几何数据，开始异步生成网格。
    /// </summary>
    /// <param name="spine">路径骨架数据。</param>
    /// <param name="layers">所有图层的几何数据。</param>
    public void StartMeshGeneration(PathSpine spine, List<PathTool.Data.PathLayer> layers)
    {
        if (m_IsJobRunning) return;

        if (spine.VertexCount < 2 || layers.Count == 0)
        {
            PreviewMesh.Clear();
            return;
        }

        AllocateAndPrepareJobData(spine, layers);

        var job = new GenerateMeshJob
        {
            spine = new PathSpineForJob(m_SpinePoints, m_SpineTangents, m_SpineNormals, m_SpineTimestamps),
            segments = m_SegmentDataArray,
            vertices = m_JobVertices,
            uvs = m_JobUVs,
            allTriangles = m_JobAllTriangles,
            subMeshTriangleCounts = m_SubMeshTriangleCounts
        };
        m_MeshUpdateJobHandle = job.Schedule();
        m_IsJobRunning = true;
    }

    /// <summary>
    /// 检查Job是否完成，如果完成，则将数据应用到Mesh上。
    /// </summary>
    /// <returns>如果Job在本帧完成，则返回true。</returns>
    public bool TryFinalizeMesh()
    {
        if (m_IsJobRunning && m_MeshUpdateJobHandle.IsCompleted)
        {
            m_MeshUpdateJobHandle.Complete();
            PreviewMesh.Clear();

            if (m_JobVertices.Length > 0)
            {
                PreviewMesh.SetVertices(m_JobVertices.AsArray());
                PreviewMesh.SetUVs(0, m_JobUVs.AsArray());
                PreviewMesh.subMeshCount = m_SubMeshTriangleCounts.Length;

                int triangleStartIndex = 0;
                for (int i = 0; i < m_SubMeshTriangleCounts.Length; i++)
                {
                    int count = m_SubMeshTriangleCounts[i];
                    if (count > 0)
                    {
                        var triangles = m_JobAllTriangles.AsArray().GetSubArray(triangleStartIndex, count);
                        PreviewMesh.SetTriangles(triangles.ToArray(), i, false);
                    }
                    triangleStartIndex += count;
                }
                PreviewMesh.RecalculateBounds();
                PreviewMesh.RecalculateNormals();
            }

            DisposeAllNativeCollections();
            m_IsJobRunning = false;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        m_MeshUpdateJobHandle.Complete();
        DisposeAllNativeCollections();
        if (PreviewMesh != null) Object.DestroyImmediate(PreviewMesh);
    }

    #endregion

    #region 内部Job管理 (Internal Job Management)

    private NativeArray<Vector3> m_SpinePoints;
    private NativeArray<Vector3> m_SpineTangents;
    private NativeArray<Vector3> m_SpineNormals;
    private NativeArray<float> m_SpineTimestamps;
    private NativeArray<ProfileSegmentData> m_SegmentDataArray;
    private NativeList<Vector3> m_JobVertices;
    private NativeList<int> m_JobAllTriangles;
    private NativeList<Vector2> m_JobUVs;
    private NativeArray<int> m_SubMeshTriangleCounts;

    private void AllocateAndPrepareJobData(PathSpine spine, List<PathTool.Data.PathLayer> layers)
    {
        m_SpinePoints = new NativeArray<Vector3>(spine.points, Allocator.Persistent);
        m_SpineTangents = new NativeArray<Vector3>(spine.tangents, Allocator.Persistent);
        m_SpineNormals = new NativeArray<Vector3>(spine.surfaceNormals, Allocator.Persistent);
        m_SpineTimestamps = new NativeArray<float>(spine.timestamps, Allocator.Persistent);

        m_SegmentDataArray = new NativeArray<ProfileSegmentData>(layers.Count, Allocator.Persistent);
        for (int i = 0; i < layers.Count; i++)
        {
            m_SegmentDataArray[i] = new ProfileSegmentData
            {
                width = layers[i].width,
                horizontalOffset = layers[i].horizontalOffset,
                verticalOffset = layers[i].verticalOffset
            };
        }

        m_JobVertices = new NativeList<Vector3>(Allocator.Persistent);
        m_JobAllTriangles = new NativeList<int>(Allocator.Persistent);
        m_JobUVs = new NativeList<Vector2>(Allocator.Persistent);
        m_SubMeshTriangleCounts = new NativeArray<int>(layers.Count, Allocator.Persistent);
    }

    private void DisposeAllNativeCollections()
    {
        if (m_SpinePoints.IsCreated) m_SpinePoints.Dispose();
        if (m_SpineTangents.IsCreated) m_SpineTangents.Dispose();
        if (m_SpineNormals.IsCreated) m_SpineNormals.Dispose();
        if (m_SpineTimestamps.IsCreated) m_SpineTimestamps.Dispose();
        if (m_SegmentDataArray.IsCreated) m_SegmentDataArray.Dispose();
        if (m_JobVertices.IsCreated) m_JobVertices.Dispose();
        if (m_JobAllTriangles.IsCreated) m_JobAllTriangles.Dispose();
        if (m_JobUVs.IsCreated) m_JobUVs.Dispose();
        if (m_SubMeshTriangleCounts.IsCreated) m_SubMeshTriangleCounts.Dispose();
    }

    #endregion
}
