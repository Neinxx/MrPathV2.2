using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

/// <summary>
/// 【圆融如意版】路径编辑的核心工具与总控制器。
/// 职责高度集中，只负责协调和调度，将所有复杂工作委托给专门的子系统。
/// </summary>
[EditorTool ("Path Editor Tool")]
public class PathEditorTool : EditorTool
{
    #region 核心组件与状态

    private PreviewMeshController m_MeshController;
    private Material m_TerrainPreviewTemplate;

    // 状态变量
    private int m_HoveredPointIndex = -1;
    private int m_HoveredSegmentIndex = -1;
    private bool m_IsDraggingHandle = false;
    private bool m_PathIsDirty = true;

    // 材质管理
    private readonly List<Material> m_InstancedMaterials = new List<Material> ();
    private readonly List<Material> m_CurrentFrameMaterials = new List<Material> ();

    #endregion

    #region EditorTool 生命周期

    void OnEnable ()
    {
        m_MeshController = new PreviewMeshController ();
        Undo.undoRedoPerformed += MarkPathAsDirty;
        m_TerrainPreviewTemplate = Resources.Load<Material> ("PathPreviewMaterial");
        if (m_TerrainPreviewTemplate == null)
        {
            Debug.LogError ("Path Tool Error: Cannot find 'PathPreviewMaterial.mat' in any 'Editor/Resources' folder.");
        }
    }

    void OnDisable ()
    {
        m_MeshController?.Dispose ();
        ClearInstancedMaterials ();
        Undo.undoRedoPerformed -= MarkPathAsDirty;
    }

    #endregion

    #region GUI 与主循环

    public override GUIContent toolbarIcon =>
        new GUIContent (EditorGUIUtility.IconContent ("d_CurveEditorTool").image, "Path Editor Tool");

    public override void OnToolGUI (EditorWindow window)
    {
        if (!(window is SceneView sceneView)) return;

        var pathObject = target as GameObject;
        if (pathObject == null) return;
        var creator = pathObject.GetComponent<PathCreator> ();

        if (creator == null || !HandleDiagnostics (creator)) return;

        Event e = Event.current;
        creator.PathChanged -= OnPathDataChanged;
        creator.PathChanged += OnPathDataChanged;

        // --- 帅帐中的四大指令 ---

        // 1. **场景交互**: 委托给 PathEditorHandles，处理绘制与悬停感知
        PathEditorHandles.Draw (creator, ref m_HoveredPointIndex, ref m_HoveredSegmentIndex, m_IsDraggingHandle);

        // 2. **用户输入**: 处理增、删、插等核心指令
        HandleInput (e, creator, GUIUtility.GetControlID (FocusType.Passive));

        // 3. **网格生成**: 调度“锻造炉”在后台工作
        HandleMeshGeneration (sceneView, creator);

        // 4. **网格渲染**: 将锻造成型的“剑刃”呈现出来
        DrawPreviewMesh (creator);

        // 实时刷新界面
        sceneView.Repaint ();
    }

    #endregion

    #region 诊断与辅助

    /// <summary>
    /// 检查路径数据的有效性，并在无效时提供引导信息。
    /// </summary>
    /// <returns>如果数据有效，返回true。</returns>
    private bool HandleDiagnostics (PathCreator creator)
    {
        var style = new GUIStyle { normal = { textColor = Color.yellow }, fontSize = 14, alignment = TextAnchor.MiddleCenter };

        if (creator.Path == null)
        {
            Handles.Label (creator.transform.position + Vector3.up, "Path data is missing!", style);
            return false;
        }
        if (creator.profile == null)
        {
            Handles.Label (creator.transform.position + Vector3.up, "Path Profile is not assigned.", style);
            return false;
        }
        return true;
    }

    #endregion

    #region 交互与数据生成

    private void HandleInput (Event e, PathCreator creator, int controlID)
    {
        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0)
                {
                    if (e.shift && m_HoveredSegmentIndex != -1)
                    {
                        GUIUtility.hotControl = controlID;
                        Vector3 pointToInsert = creator.GetPointAt (m_HoveredSegmentIndex + 0.5f);
                        creator.InsertSegment (m_HoveredSegmentIndex, pointToInsert);
                        e.Use ();
                    }
                    else if (e.control)
                    {
                        GUIUtility.hotControl = controlID;
                        Ray worldRay = HandleUtility.GUIPointToWorldRay (e.mousePosition);
                        if (Physics.Raycast (worldRay, out RaycastHit hitInfo)) creator.AddSegment (hitInfo.point);
                        e.Use ();
                    }
                }
                else if (e.button == 1 && m_HoveredPointIndex != -1)
                {
                    GUIUtility.hotControl = controlID;
                    creator.DeleteSegment (m_HoveredPointIndex);
                    m_HoveredPointIndex = -1;
                    e.Use ();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlID)
                {
                    GUIUtility.hotControl = 0;
                    m_IsDraggingHandle = false;
                    e.Use ();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlID && e.button == 0) m_IsDraggingHandle = true;
                break;
        }
    }
    /// <summary>
    /// 调度网格的生成与最终化。
    /// </summary>
    /// <summary>
    /// 调度网格的生成与最终化。
    /// </summary>
    private void HandleMeshGeneration (SceneView sceneView, PathCreator creator) // <-- 接收 creator
    {
        if (m_MeshController.TryFinalizeMesh ())
        {
            sceneView.Repaint ();
        }

        if (m_PathIsDirty)
        {
            // 【修正】直接使用传入的 creator，不再有强制类型转换
            StartMeshGeneration (creator);
        }
    }

    private void StartMeshGeneration (PathCreator creator)
    {
        PathSpine spine = PathSampler.SamplePath (creator, creator.profile.minVertexSpacing);
        m_MeshController.StartMeshGeneration (spine, creator.profile.layers);
        m_PathIsDirty = false;
    }

    #endregion

    #region 渲染逻辑 (Rendering Logic)

    /// <summary>
    /// 渲染预览网格的主方法。
    /// </summary>
    private void DrawPreviewMesh (PathCreator creator)
    {
        var mesh = m_MeshController.PreviewMesh;
        if (mesh == null || mesh.vertexCount == 0) return;

        // 1. 保证我们拥有最新、最正确的材质列表
        UpdateMaterials (creator);

        // 2. 循环绘制所有子网格，每个子网格对应一个PathLayer
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (i < m_CurrentFrameMaterials.Count && m_CurrentFrameMaterials[i] != null)
            {
                Graphics.DrawMesh (mesh, Matrix4x4.identity, m_CurrentFrameMaterials[i], 0, null, i);
            }
        }
    }

    /// <summary>
    /// 智能地更新用于当帧渲染的材质列表。
    /// </summary>
    private void UpdateMaterials (PathCreator creator)
    {
        var layers = creator.profile.layers;

        // 如果图层数量变化，则进行一次彻底的重建
        if (layers.Count != m_CurrentFrameMaterials.Count)
        {
            ClearInstancedMaterials ();
            m_CurrentFrameMaterials.Clear ();
        }

        // 获取内部的模板材质
        Material terrainTemplate = m_TerrainPreviewTemplate;
        if (terrainTemplate == null) return; // 如果模板材质加载失败，则不进行任何操作

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            Material currentMat = (i < m_CurrentFrameMaterials.Count) ? m_CurrentFrameMaterials[i] : null;

            var firstBlendLayer = (layer.terrainPaintingRecipe.blendLayers.Count > 0) ? layer.terrainPaintingRecipe.blendLayers[0] : null;
            Texture diffuse = firstBlendLayer?.terrainLayer?.diffuseTexture;

            // 判断是否需要创建一个新的材质实例
            // 条件：1.当前材质为空；2.当前材质不是我们创建的实例；3.纹理已发生变化
            bool needsNewInstance = currentMat == null || !m_InstancedMaterials.Contains (currentMat) || currentMat.mainTexture != diffuse;

            if (needsNewInstance)
            {
                // 如果旧材质是我们的实例，先将其销毁
                if (m_InstancedMaterials.Contains (currentMat))
                {
                    m_InstancedMaterials.Remove (currentMat);
                    Object.DestroyImmediate (currentMat);
                }

                // 创建新的实例
                var newMat = CreateTerrainMaterialInstance (layer, terrainTemplate);

                // 更新当帧使用的材质列表
                if (i >= m_CurrentFrameMaterials.Count)
                {
                    m_CurrentFrameMaterials.Add (newMat);
                }
                else
                {
                    m_CurrentFrameMaterials[i] = newMat;
                }

                // 将新实例加入我们的“实例池”中，以便日后管理
                if (newMat != null)
                {
                    m_InstancedMaterials.Add (newMat);
                }
            }
        }
    }

    /// <summary>
    /// 创建一个用于地形预览的材质实例。
    /// </summary>
    private Material CreateTerrainMaterialInstance (PathLayer layer, Material terrainTemplate)
    {
        var firstBlendLayer = (layer.terrainPaintingRecipe.blendLayers.Count > 0) ? layer.terrainPaintingRecipe.blendLayers[0] : null;
        if (terrainTemplate != null && firstBlendLayer?.terrainLayer?.diffuseTexture != null)
        {
            var matInstance = new Material (terrainTemplate);
            matInstance.mainTexture = firstBlendLayer.terrainLayer.diffuseTexture;
            return matInstance;
        }
        return null; // 如果数据不完整，则返回null
    }

    /// <summary>
    /// 清理所有由我们手动创建的材质实例，防止内存泄漏。
    /// </summary>
    private void ClearInstancedMaterials ()
    {
        foreach (var mat in m_InstancedMaterials)
        {
            if (mat != null)
            {
                Object.DestroyImmediate (mat);
            }
        }
        m_InstancedMaterials.Clear ();
    }

    #endregion

    #region 事件回调

    private void MarkPathAsDirty () => m_PathIsDirty = true;
    private void OnPathDataChanged (object sender, PathChangedEventArgs e) => MarkPathAsDirty ();

    #endregion
}
