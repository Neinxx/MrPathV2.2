// PathEditorTool.cs (最终升格版)
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

[EditorTool("Path Editor Tool", typeof(PathCreator))]
public class PathEditorTool : EditorTool
{
    #region 状态与护法 (State & Controllers)
    private PreviewMeshController _meshController;
    private TerrainHeightProvider _heightProvider;
    private PreviewMaterialManager _materialManager;
    private PathInputHandler _inputHandler;

    private Material _splatMaterialTemplate;

    private int _hoveredPointIdx = -1;
    private int _hoveredSegmentIdx = -1;
    private bool _isDraggingHandle;
    private bool _isPathDirty = true;
    private PathSpine? _latestPathSpine;

    private GameObject _previewObject;
    private MeshFilter _previewMeshFilter;
    private MeshRenderer _previewMeshRenderer;

    // ✨✨✨【升格】✨✨✨
    // 新增状态，用于锁定UI，防止重复执行
    private bool _isApplyingHeight;
    private bool _isApplyingPaint;
    #endregion

    public static PathEditorTool CurrentInstance { get; private set; }

    public PathEditorTool()
    {
        CurrentInstance = this;
    }

    #region 生命周期 (Lifecycle)
    void OnEnable()
    {
        _meshController = new PreviewMeshController();
        _heightProvider = new TerrainHeightProvider();
        _materialManager = new PreviewMaterialManager();
        _inputHandler = new PathInputHandler();

        _splatMaterialTemplate = Resources.Load<Material>("test");
        if (_splatMaterialTemplate == null)
        {
            Debug.LogError("未能从 Resources 文件夹加载 'PathSplatPreviewMaterial'。请确认该材质存在。");
        }

        CreatePreviewObject();

        Undo.undoRedoPerformed += MarkPathAsDirty;
        MarkPathAsDirty();

        ToolManager.activeToolChanged += OnActiveToolChanged;
        if (target is PathCreator creator)
        {
            creator.PathModified += OnPathModified;
        }
    }

    void OnDisable()
    {
        _meshController?.Dispose();
        _heightProvider?.Dispose();
        _materialManager?.Dispose();
        Undo.undoRedoPerformed -= MarkPathAsDirty;

        DestroyPreviewObject();
        ToolManager.activeToolChanged -= OnActiveToolChanged;
        if (target is PathCreator creator)
        {
            creator.PathModified -= OnPathModified;
        }

        // 确保在禁用工具时，应用状态被重置
        _isApplyingHeight = false;
        _isApplyingPaint = false;
    }
    #endregion

    #region EditorTool 核心 (Core GUI Loop)
    public override GUIContent toolbarIcon => new(EditorGUIUtility.IconContent("Terrain Icon").image, "路径编辑器");

    public override void OnToolGUI(EditorWindow window)
    {
        if (window is not SceneView sceneView) return;
        var creator = target as PathCreator;
        if (!IsPathValid(creator))
        {
            _previewObject?.SetActive(false);
            return;
        }

        _previewObject?.SetActive(true);

        if (_isPathDirty)
        {
            _latestPathSpine = PathSampler.SamplePath(creator, _heightProvider);
            if (_latestPathSpine.HasValue)
                _meshController.StartMeshGeneration(_latestPathSpine.Value, creator.profile);
            _isPathDirty = false;
        }

        var context = CreateHandleContext(creator);
        PathEditorHandles.Draw(ref context);
        UpdateHoverStateFromContext(context);
        _inputHandler.HandleInputEvents(Event.current, creator, context.hoveredPathT, context.hoveredPointIndex);

        if (_meshController.TryFinalizeMesh())
        {
            UpdatePreviewObject(creator);
        }
        UpdatePreviewMaterials(creator);

        DrawApplyToTerrainUI();

        sceneView.Repaint();
    }
    #endregion

    #region ✨✨✨【升格】✨✨✨ 场景UI 与 敕令分发

    private void DrawApplyToTerrainUI()
    {
        Handles.BeginGUI();

        // 获取当前激活的Scene视图（处理多视图情况）
        SceneView currentSceneView = SceneView.currentDrawingSceneView;
        if (currentSceneView == null)
        {
            Handles.EndGUI();
            return;
        }

        // 窗口尺寸（更贴近Unity原生工具的紧凑风格）
        float windowWidth = 180;
        float windowHeight = 110;

        // 计算右下角位置（基于Scene视图的实际客户区）
        Rect sceneViewClientRect = currentSceneView.position;
        // 减去窗口边框和标题栏高度，确保定位准确
        float xPos = sceneViewClientRect.xMax - windowWidth - 15; // 右侧边距
        float yPos = sceneViewClientRect.yMax - windowHeight - 40; // 底部边距（考虑编辑器顶部栏）
        Rect windowRect = new Rect(xPos, yPos, windowWidth, windowHeight);

        // 绘制窗口（使用原生窗口外观）
        GUILayout.Window(GetHashCode(), windowRect, id =>
        {
            // 内部间距调整
            GUILayout.BeginVertical();
            GUILayout.Space(6);

            // 标题（使用原生加粗标签）
            GUILayout.Label("路径应用工具", EditorStyles.boldLabel);


            GUILayout.Space(4);

            // 操作状态检查
            bool canExecute = !_isApplyingHeight && !_isApplyingPaint;

            using (new EditorGUI.DisabledScope(!canExecute))
            {
                // 压平地形按钮
                GUI.backgroundColor = _isApplyingHeight ? Color.yellow : new Color(0.6f, 0.85f, 1f);
                string flattenText = _isApplyingHeight ? "正在压平..." : "1. 压平地形";
                if (GUILayout.Button(flattenText, EditorStyles.toolbarButton, GUILayout.Height(26)))
                {
                    OnFlattenTerrainClicked();
                }

                GUILayout.Space(3);

                // 绘制纹理按钮
                GUI.backgroundColor = _isApplyingPaint ? Color.yellow : new Color(1f, 0.75f, 1f);
                string paintText = _isApplyingPaint ? "正在绘制..." : "2. 绘制纹理";
                if (GUILayout.Button(paintText, EditorStyles.toolbarButton, GUILayout.Height(26)))
                {
                    OnPaintTerrainClicked();
                }
            }

            GUILayout.Space(4);
            GUILayout.EndVertical();

            // 允许拖动窗口（标题栏区域）
            GUI.DragWindow(new Rect(0, 0, windowWidth, 20));

        }, "路径转地形"); // 窗口标题

        Handles.EndGUI();
        GUI.backgroundColor = Color.white; // 恢复默认背景色
    }

    private void OnFlattenTerrainClicked()
    {
        var creator = target as PathCreator;
        if (!IsPathValid(creator)) return;
        var command = new FlattenTerrainCommand(creator, _heightProvider);
        _ = ExecuteCommandAsync(command, b => _isApplyingHeight = b);
    }

    private void OnPaintTerrainClicked()
    {
        var creator = target as PathCreator;
        if (!IsPathValid(creator)) return;
        var command = new PaintTerrainCommand(creator, _heightProvider);
        _ = ExecuteCommandAsync(command, b => _isApplyingPaint = b);
    }

    private async Task ExecuteCommandAsync(TerrainCommandBase command, System.Action<bool> setIsApplying)
    {
        setIsApplying(true);
        try
        {
            EditorUtility.DisplayProgressBar("应用路径到地形", $"正在执行: {command.GetCommandName()}...", 0.3f);
            await command.ExecuteAsync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"执行 {command.GetCommandName()} 失败: {ex.Message}\n{ex.StackTrace}", target);
            EditorUtility.DisplayDialog("执行失败", $"操作 {command.GetCommandName()} 失败，详情请查看控制台日志。", "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            setIsApplying(false);
        }
    }

    #endregion

    #region 预览对象管理 (Preview Object Management)
    // ... 此区域代码保持不变 ...
    private void CreatePreviewObject()
    {
        if (_previewObject == null)
        {
            _previewObject = new GameObject("Path_Preview_Object");
            _previewObject.hideFlags = HideFlags.HideAndDontSave;
            var collider = _previewObject.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
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

    private void UpdatePreviewObject(PathCreator creator)
    {
        if (_previewObject == null || _meshController == null) return;
        Mesh previewMesh = _meshController.PreviewMesh;
        if (previewMesh == null) return;
        _previewMeshFilter.sharedMesh = previewMesh;
    }

    private void UpdatePreviewMaterials(PathCreator creator)
    {
        if (_previewMeshRenderer == null || creator?.profile == null || _splatMaterialTemplate == null) return;

        _materialManager.UpdateMaterials(creator.profile, _splatMaterialTemplate);
        List<Material> renderMats = _materialManager.GetFrameRenderMaterials();
        _previewMeshRenderer.sharedMaterials = renderMats.ToArray();
    }
    #endregion

    #region 辅助方法 (Helpers)
    // ... 此区域代码保持不变 ...
    private bool IsPathValid(PathCreator c) => c != null && c.profile != null && c.pathData.KnotCount >= 2;
    private PathEditorHandles.HandleDrawContext CreateHandleContext(PathCreator creator) => new()
    {
        creator = creator,
        heightProvider = _heightProvider,
        latestSpine = _latestPathSpine,
        isDragging = _isDraggingHandle,
        hoveredPointIndex = _hoveredPointIdx,
        hoveredSegmentIndex = _hoveredSegmentIdx,
    };
    private void UpdateHoverStateFromContext(PathEditorHandles.HandleDrawContext context)
    {
        _hoveredPointIdx = context.hoveredPointIndex;
        _hoveredSegmentIdx = context.hoveredSegmentIndex;
        _isDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
    }

    public void MarkPathAsDirty()
    {
        _isPathDirty = true;
        _latestPathSpine = null;
        SceneView.RepaintAll();
    }

    private void OnPathModified(PathChangeCommand command) => MarkPathAsDirty();
    private void OnActiveToolChanged()
    {
        if (target is PathCreator lastTarget) lastTarget.PathModified -= OnPathModified;
        if (ToolManager.IsActiveTool(this) && target is PathCreator newCreator)
        {
            newCreator.PathModified += OnPathModified;
            MarkPathAsDirty();
        }
    }
    #endregion
}