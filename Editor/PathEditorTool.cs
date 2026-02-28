

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using System.Collections.Generic;

[EditorTool("Path Editor Tool", typeof(PathCreator))]
public class PathEditorTool : EditorTool
{
    #region 状态与护法
    private PreviewMeshController _meshController;
    private TerrainHeightProvider _heightProvider;
    private PreviewMaterialManager _materialManager;
    private PathInputHandler _inputHandler;
    private Material _terrainMatTemplate;

    private int _hoveredPointIdx = -1;
    private int _hoveredSegmentIdx = -1;
    private bool _isDraggingHandle;
    private bool _isPathDirty = true;
    private PathSpine? _latestPathSpine;

    // 【【【 化虚为实 • 核心法宝 】】】
    private GameObject _previewObject;
    private MeshFilter _previewMeshFilter;
    private MeshRenderer _previewMeshRenderer;
    #endregion



    #region 生命周期 (Lifecycle)
    void OnEnable()
    {
        _meshController = new PreviewMeshController();
        _heightProvider = new TerrainHeightProvider();
        _materialManager = new PreviewMaterialManager();
        _inputHandler = new PathInputHandler();
        _terrainMatTemplate = Resources.Load<Material>("PathPreviewMaterial");

        // 步骤 1: 铸造法宝
        CreatePreviewObject();

        Undo.undoRedoPerformed += MarkPathAsDirty;
        // 确保初次激活时，路径会被刷新
        MarkPathAsDirty();

        // 我们需要在target变化时，重新订阅事件
        ToolManager.activeToolChanged += OnActiveToolChanged;
        if (target is PathCreator creator)
        {
            creator.PathModified += OnPathModified; ;
        }
    }

    void OnDisable()
    {
        _meshController?.Dispose();
        _heightProvider?.Dispose();
        _materialManager?.Dispose();
        Undo.undoRedoPerformed -= MarkPathAsDirty;

        // 步骤 2: 销毁法宝
        DestroyPreviewObject();
        ToolManager.activeToolChanged -= OnActiveToolChanged;
        if (target is PathCreator creator)
        {
            creator.PathModified -= OnPathModified;
        }
    }
    #endregion

    #region EditorTool 核心
    public override GUIContent toolbarIcon => new(EditorGUIUtility.IconContent("Animator icon").image, "路径编辑器");

    public override void OnToolGUI(EditorWindow window)
    {
        if (window is not SceneView sceneView) return;
        var creator = target as PathCreator;
        if (!IsPathValid(creator))
        {
            _previewObject?.SetActive(false);
            return;
        }
        else
        {
            _previewObject?.SetActive(true);
        }



        if (_isPathDirty)
        {
            _latestPathSpine = PathSampler.SamplePath(creator, _heightProvider);
            _meshController.StartMeshGeneration(_latestPathSpine.Value, creator.profile.layers);
            _isPathDirty = false;
        }

        var context = CreateHandleContext(creator);
        PathEditorHandles.Draw(ref context);
        UpdateHoverStateFromContext(context);
        _inputHandler.HandleInputEvents(Event.current, creator, context.hoveredPathT, context.hoveredPointIndex);


        // 【【【 御使法宝 • 顺天应时 】】】
        // 检查网格是否已炼制完成
        if (_meshController.TryFinalizeMesh())
        {
            // 若已完成，则更新法宝之骨架与皮囊
            UpdatePreviewObject(creator);
        }

        // 确保材质总是最新的 (例如，当用户在Inspector中调整颜色时)
        UpdatePreviewMaterials(creator);

        sceneView.Repaint();
    }
    #endregion

    #region 法宝操控核心 (Preview Object Management)
    private void CreatePreviewObject()
    {
        if (_previewObject == null)
        {
            _previewObject = new GameObject("Path_Preview_Object");
            // 此乃“无形”之真意，让此物存在，却不在场景中可见，亦不被保存
            _previewObject.hideFlags = HideFlags.HideAndDontSave;
            // 关闭碰撞，仅作显示
            var collider = _previewObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            _previewMeshFilter = _previewObject.AddComponent<MeshFilter>();
            _previewMeshRenderer = _previewObject.AddComponent<MeshRenderer>();
        }
    }

    private void DestroyPreviewObject()
    {
        if (_previewObject != null)
        {
            DestroyImmediate(_previewObject);
            _previewObject = null;
        }
    }

    /// <summary>
    /// 当网格数据更新时，更新预览对象的骨架 (Mesh)
    /// </summary>
    private void UpdatePreviewObject(PathCreator creator)
    {
        if (_previewObject == null || _meshController == null) return;
        Mesh previewMesh = _meshController.PreviewMesh;
        if (previewMesh == null) return;

        // 赋予骨架
        _previewMeshFilter.sharedMesh = previewMesh;
    }

    /// <summary>
    /// 每一帧都检查并更新预览对象的皮囊 (Materials)
    /// </summary>
    private void UpdatePreviewMaterials(PathCreator creator)
    {
        if (_previewMeshRenderer == null || creator?.profile == null) return;

        // 更新材质池
        _materialManager.UpdateMaterials(creator.profile, _terrainMatTemplate);
        // 获取最新的材质清单
        List<Material> renderMats = _materialManager.GetFrameRenderMaterials();
        // 赋予皮囊
        _previewMeshRenderer.sharedMaterials = renderMats.ToArray();
    }

    // 【【【 功法变更：移除旧法 】】】
    // 我们不再需要 DrawPreviewMesh 方法，因为它已被新的工作流取代
    // private void DrawPreviewMesh(PathCreator creator) { ... }

    #endregion

    #region 辅助方法 (Helpers)
    private bool IsPathValid(PathCreator c) => c != null && c.profile != null && c.pathData.KnotCount >= 2;
    private PathEditorHandles.HandleDrawContext CreateHandleContext(PathCreator creator) => new()
    {
        creator = creator,
        heightProvider = _heightProvider,
        latestSpine = _latestPathSpine,
        isDragging = _isDraggingHandle,
        hoveredPointIndex = _hoveredPointIdx,
        hoveredSegmentIndex = _hoveredSegmentIdx,
        // hoveredPathT 应该由 PathEditorHandles.Draw 内部计算并写回 context
    };
    private void UpdateHoverStateFromContext(PathEditorHandles.HandleDrawContext context)
    {
        _hoveredPointIdx = context.hoveredPointIndex;
        _hoveredSegmentIdx = context.hoveredSegmentIndex;
        // isDragging 的状态更新也应更稳健
        _isDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
    }

    /// <summary>
    /// 【核心动作】将路径标记为“脏”，触发下一帧的重绘。这是一个无参数的方法。
    /// </summary>
    public void MarkPathAsDirty()
    {
        _isPathDirty = true;
        _latestPathSpine = null;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// 【信使一号】专门用于响应 PathCreator.PathModified 事件。
    /// 它接收 PathCommand 参数（尽管在此我们用不到它），然后调用核心动作。
    /// </summary>
    private void OnPathModified(PathChangeCommand command)
    {
        MarkPathAsDirty();
    }

    /// <summary>
    /// 【信使二号】专门用于响应 ToolManager.activeToolChanged 事件。
    /// 它没有参数，负责处理工具切换时的事件订阅逻辑。
    /// </summary>
    private void OnActiveToolChanged()
    {
        var lastTarget = target as PathCreator;
        if (lastTarget != null)
        {
            lastTarget.PathModified -= OnPathModified;
        }

        if (ToolManager.IsActiveTool(this) && target is PathCreator newCreator)
        {
            newCreator.PathModified += OnPathModified;
            MarkPathAsDirty();
        }
    }
    #endregion
}