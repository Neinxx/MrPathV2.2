using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 负责管理路径预览网格的生命周期、异步生成和渲染。
/// V2.7 (WYSIWYG & Sub-mesh):
/// - 动态管理材质列表，为每个分段应用不同材质。
/// - 支持子网格渲染，实现了真正的“所见即所得”。
/// </summary>
public class PreviewMeshController : System.IDisposable
{
    private Mesh m_PreviewMesh;
    private List<Material> m_PreviewMaterials = new List<Material> ();
    private List<Material> m_InstancedMaterials = new List<Material> ();

    private bool m_IsPathDirty = true;
    private bool m_IsJobRunning = false;
    private JobHandle m_MeshUpdateJobHandle;

    private NativeArray<Vector3> m_SpinePoints;
    private NativeArray<Vector3> m_SpineTangents;
    private NativeArray<Vector3> m_SpineNormals;
    private NativeArray<float> m_SpineTimestamps;
    private NativeArray<ProfileSegmentData> m_SegmentDataArray;
    private NativeList<Vector3> m_JobVertices;
    private NativeList<int> m_JobAllTriangles;
    private NativeList<Vector2> m_JobUVs;
    private NativeArray<int> m_SubMeshTriangleCounts;

    public PreviewMeshController ()
    {
        m_PreviewMesh = new Mesh { name = "Path Preview Mesh" };
        m_PreviewMesh.hideFlags = HideFlags.HideAndDontSave;
    }

    public void Dispose ()
    {
        m_MeshUpdateJobHandle.Complete ();
        DisposeAllNativeCollections ();

        if (m_PreviewMesh != null) Object.DestroyImmediate (m_PreviewMesh);

        // 清理所有我们手动实例化的材质
        foreach (var mat in m_InstancedMaterials)
        {
            if (mat != null) Object.DestroyImmediate (mat);
        }
        m_InstancedMaterials.Clear ();
    }

    public void Update (PathCreator creator)
    {
        UpdateMaterials (creator);
        HandleJobCompletion ();
        HandleJobScheduling (creator);
        DrawPreviewMesh ();
    }

    public void MarkAsDirty ()
    {
        m_IsPathDirty = true;
    }

    private void UpdateMaterials (PathCreator creator)
    {
        if (creator.profile == null) return;

        var segments = creator.profile.segments;

        // 1. 如果分段数量发生变化，这是最激烈的变化，需要完全重建材质列表
        if (segments.Count != m_PreviewMaterials.Count)
        {
            // 清理所有旧的材质实例
            foreach (var mat in m_InstancedMaterials)
            {
                if (mat != null) Object.DestroyImmediate (mat);
            }
            m_InstancedMaterials.Clear ();
            m_PreviewMaterials.Clear ();

            // 重新创建所有材质
            Material terrainTemplate = PathToolSettings.Instance.terrainPreviewTemplate;
            foreach (var segment in segments)
            {
                if (segment.outputMode == SegmentOutputMode.StandaloneMesh)
                {
                    m_PreviewMaterials.Add (segment.standaloneMeshMaterial);
                }
                else // 地形绘制模式
                {
                    Material matInstance = CreateTerrainMaterialInstance (segment, terrainTemplate);
                    m_PreviewMaterials.Add (matInstance);
                    if (matInstance != null) m_InstancedMaterials.Add (matInstance);
                }
            }
        }
        else // 2. 如果分段数量没变，我们只检查并更新需要改变的材质
        {
            Material terrainTemplate = PathToolSettings.Instance.terrainPreviewTemplate;
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var currentMat = m_PreviewMaterials[i];

                if (segment.outputMode == SegmentOutputMode.StandaloneMesh)
                {
                    // 如果材质引用变了，就更新
                    if (currentMat != segment.standaloneMeshMaterial)
                    {
                        m_PreviewMaterials[i] = segment.standaloneMeshMaterial;
                    }
                }
                else // 地形绘制模式
                {
                    Texture newTex = (segment.terrainPaintingRecipe.blendLayers.Count > 0 && segment.terrainPaintingRecipe.blendLayers[0].terrainLayer != null) ?
                        segment.terrainPaintingRecipe.blendLayers[0].terrainLayer.diffuseTexture :
                        null;

                    // 如果当前材质的纹理与需要的不符，只更新纹理，不创建新材质
                    if (currentMat == null || currentMat.mainTexture != newTex)
                    {
                        Material newMatInstance = CreateTerrainMaterialInstance (segment, terrainTemplate);
                        m_PreviewMaterials[i] = newMatInstance;

                        // 更新我们持有的实例列表
                        if (m_InstancedMaterials.Contains (currentMat))
                        {
                            m_InstancedMaterials.Remove (currentMat);
                            Object.DestroyImmediate (currentMat);
                        }
                        if (newMatInstance != null) m_InstancedMaterials.Add (newMatInstance);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 辅助方法：创建一个用于地形预览的材质实例
    /// </summary>
    private Material CreateTerrainMaterialInstance (ProfileSegment segment, Material terrainTemplate)
    {
        if (terrainTemplate != null &&
            segment.terrainPaintingRecipe.blendLayers.Count > 0 &&
            segment.terrainPaintingRecipe.blendLayers[0].terrainLayer != null &&
            segment.terrainPaintingRecipe.blendLayers[0].terrainLayer.diffuseTexture != null)
        {
            Material matInstance = new Material (terrainTemplate);
            matInstance.mainTexture = segment.terrainPaintingRecipe.blendLayers[0].terrainLayer.diffuseTexture;
            return matInstance;
        }
        return null;
    }
    private void HandleJobCompletion ()
    {
        if (m_IsJobRunning && m_MeshUpdateJobHandle.IsCompleted)
        {
            m_MeshUpdateJobHandle.Complete ();
            m_PreviewMesh.Clear ();

            if (m_JobVertices.Length > 0)
            {
                m_PreviewMesh.SetVertices (m_JobVertices.ToArray ());
                m_PreviewMesh.SetUVs (0, m_JobUVs.ToArray ());

                m_PreviewMesh.subMeshCount = m_SubMeshTriangleCounts.Length;
                int triangleStartIndex = 0;
                for (int i = 0; i < m_SubMeshTriangleCounts.Length; i++)
                {
                    int count = m_SubMeshTriangleCounts[i];
                    if (count > 0)
                    {
                        var triangles = m_JobAllTriangles.AsArray ().GetSubArray (triangleStartIndex, count);
                        m_PreviewMesh.SetTriangles (triangles.ToArray (), i);
                    }
                    triangleStartIndex += count;
                }

                m_PreviewMesh.RecalculateNormals ();
                m_PreviewMesh.RecalculateBounds ();
            }

            DisposeAllNativeCollections ();
            m_IsJobRunning = false;
        }
    }

    private void HandleJobScheduling (PathCreator creator)
    {
        if (m_IsPathDirty && !m_IsJobRunning && creator.profile != null)
        {
            PathSpine spine = PathSampler.SamplePath (creator, creator.profile.minVertexSpacing);

            // 检查路径骨架是否足够生成网格
            if (spine.points.Length < 2)
            {
                // 如果路径太短，我们只清空现有网格然后返回。
                // 关键：不要重置 m_IsPathDirty 标记！
                // 这样，下一帧如果路径变长了，系统会再次尝试调度Job。
                if (m_PreviewMesh.vertexCount > 0)
                {
                    m_PreviewMesh.Clear ();
                }
                return; // 提前退出，等待下一次机会
            }

            // 只有在确定可以调度Job时，才继续执行
            AllocateAndPrepareJobData (spine, creator.profile);

            var job = new GenerateMeshJob
            {
                spine = new PathSpineForJob (m_SpinePoints, m_SpineTangents, m_SpineNormals, m_SpineTimestamps),
                segments = m_SegmentDataArray,
                vertices = m_JobVertices,
                uvs = m_JobUVs,
                allTriangles = m_JobAllTriangles,
                subMeshTriangleCounts = m_SubMeshTriangleCounts
            };
            m_MeshUpdateJobHandle = job.Schedule ();

            m_IsJobRunning = true;
            // 只有在Job成功调度后，才重置脏标记
            m_IsPathDirty = false;
        }
    }

    private void DrawPreviewMesh ()
    {
        if (m_PreviewMesh.vertexCount > 0)
        {
            for (int i = 0; i < m_PreviewMesh.subMeshCount; i++)
            {
                if (i < m_PreviewMaterials.Count && m_PreviewMaterials[i] != null)
                {
                    Graphics.DrawMesh (m_PreviewMesh, Matrix4x4.identity, m_PreviewMaterials[i], 0, null, i);
                }
            }
        }
    }

    private void AllocateAndPrepareJobData (PathSpine spine, PathProfile profile)
    {
        m_SpinePoints = new NativeArray<Vector3> (spine.points, Allocator.Persistent);
        m_SpineTangents = new NativeArray<Vector3> (spine.tangents, Allocator.Persistent);
        m_SpineNormals = new NativeArray<Vector3> (spine.normals, Allocator.Persistent);
        m_SpineTimestamps = new NativeArray<float> (spine.timestamps, Allocator.Persistent);

        m_SegmentDataArray = new NativeArray<ProfileSegmentData> (profile.segments.Count, Allocator.Persistent);
        for (int i = 0; i < profile.segments.Count; i++)
        {
            var segment = profile.segments[i];
            m_SegmentDataArray[i] = new ProfileSegmentData
            {
                width = segment.width,
                horizontalOffset = segment.horizontalOffset,
                verticalOffset = segment.verticalOffset
            };
        }

        m_JobVertices = new NativeList<Vector3> (Allocator.Persistent);
        m_JobAllTriangles = new NativeList<int> (Allocator.Persistent);
        m_JobUVs = new NativeList<Vector2> (Allocator.Persistent);
        m_SubMeshTriangleCounts = new NativeArray<int> (profile.segments.Count, Allocator.Persistent);
    }

    private void DisposeAllNativeCollections ()
    {
        // 检查 IsCreated 是一个好习惯，可以防止对已释放或未分配的集合进行操作
        if (m_SpinePoints.IsCreated) m_SpinePoints.Dispose ();
        if (m_SpineTangents.IsCreated) m_SpineTangents.Dispose ();
        if (m_SpineNormals.IsCreated) m_SpineNormals.Dispose ();
        if (m_SpineTimestamps.IsCreated) m_SpineTimestamps.Dispose ();
        if (m_SegmentDataArray.IsCreated) m_SegmentDataArray.Dispose ();
        if (m_JobVertices.IsCreated) m_JobVertices.Dispose ();
        if (m_JobAllTriangles.IsCreated) m_JobAllTriangles.Dispose ();
        if (m_JobUVs.IsCreated) m_JobUVs.Dispose ();
        if (m_SubMeshTriangleCounts.IsCreated) m_SubMeshTriangleCounts.Dispose ();
    }
}
