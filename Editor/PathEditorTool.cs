using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

/// <summary>
/// 路径编辑的核心工具。
/// V2.1 (Separation of Concerns):
/// - 预览网格的所有逻辑已移至独立的 PreviewMeshController。
/// - 此类只负责处理用户交互(Handles)和作为控制器之间的桥梁。
/// </summary>
[EditorTool ("Path Editor Tool", typeof (PathCreator))]
public class PathEditorTool : EditorTool
{
    private PreviewMeshController m_MeshController;

    #region EditorTool Lifecycle (OnEnable/OnDisable)

    private void OnEnable ()
    {
        m_MeshController = new PreviewMeshController ();

        PathCreator creator = target as PathCreator;
        if (creator != null) creator.OnPathChanged += OnPathChanged;
        Undo.undoRedoPerformed += OnPathChanged;
    }

    private void OnDisable ()
    {
        m_MeshController?.Dispose ();

        PathCreator creator = target as PathCreator;
        if (creator != null) creator.OnPathChanged -= OnPathChanged;
        Undo.undoRedoPerformed -= OnPathChanged;
    }

    #endregion

    #region GUI & Drawing

    public override GUIContent toolbarIcon
    {
        get
        {

            GUIContent content = EditorGUIUtility.IconContent ("settings");

            content.tooltip = "Path Tool";
            return content;
        }
    }
    public override void OnToolGUI (EditorWindow window)
    {
        if (window is not SceneView sceneView) return;
        PathCreator creator = target as PathCreator;
        if (creator == null || creator.Path == null) return;

        // 1. 绘制可交互的曲线编辑手柄
        creator.Path.DrawEditorHandles (creator);

        // 2. 处理在空白处添加新点的输入
        HandleInput (sceneView, creator);

        // 3. 将更新和绘制工作完全委托给Mesh控制器
        m_MeshController.Update (creator);

        // 4. 强制场景视图重绘，以确保手柄和网格的更新能够被立即看到
        sceneView.Repaint ();
    }

    #endregion

    #region Input Handling

    /// <summary>
    /// 封装所有处理用户键盘和鼠标输入的逻辑。
    /// </summary>
    private void HandleInput (SceneView sceneView, PathCreator pathCreator)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && e.control)
        {
            int controlID = GUIUtility.GetControlID (FocusType.Passive);
            GUIUtility.hotControl = controlID;

            Ray worldRay = HandleUtility.GUIPointToWorldRay (e.mousePosition);
            if (Physics.Raycast (worldRay, out RaycastHit hitInfo))
            {
                pathCreator.AddSegment (hitInfo.point);
            }

            e.Use ();
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (GUIUtility.hotControl != 0)
            {
                GUIUtility.hotControl = 0;
                e.Use ();
            }
        }
    }

    #endregion

    private void OnPathChanged ()
    {
        m_MeshController?.MarkAsDirty ();
    }

    #region Static Auto-Activation

    /// <summary>
    /// Unity编辑器启动或代码编译后，自动执行此方法来注册事件监听。
    /// </summary>
    [InitializeOnLoadMethod]
    private static void OnLoad ()
    {
        // 先移除旧的监听，防止重复注册
        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;
    }

    /// <summary>
    /// 当编辑器的选择发生变化时被调用。
    /// </summary>
    private static void OnSelectionChanged ()
    {
        // 如果新选中的对象带有PathCreator组件，则自动激活我们的工具
        if (Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<PathCreator> () != null)
        {
            ToolManager.SetActiveTool<PathEditorTool> ();
        }
    }
    #endregion

}
