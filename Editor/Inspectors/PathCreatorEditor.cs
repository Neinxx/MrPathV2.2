// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Inspectors/PathCreatorEditor.cs
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// [最终整合版] PathCreator 的自定义编辑器。
    /// 它是工具的核心交互界面，集成了 Inspector 面板、场景路径绘制、用户输入处理
    /// 以及数据驱动的地形操作UI面板，遵循单一职责和最佳用户体验原则。
    /// [优化版] 遵循 Unity 最佳实践，提升性能与可维护性。
    /// </summary>
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : Editor
    {
        #region 字段

        private PathCreator _targetCreator;

        // --- 序列化属性缓存 ---
        // [优化] 缓存 SerializedProperty 以避免在 OnInspectorGUI 中重复查找，提升性能。
        private SerializedProperty _profileProperty;
        private SerializedProperty _pathDataProperty;

        // --- 内嵌编辑器 ---
        private Editor _profileEmbeddedEditor;
        private bool _profileLocalExpanded = true;

        // --- 新的上下文与面板 ---
        private PathEditorContext _ctx;
        private TerrainOperationsPanel _terrainPanel;

        // [新增] 为场景UI中的常量值定义，避免魔法数字。
        private const float TOOLTIP_OFFSET_X = 12f;
        private const float TOOLTIP_OFFSET_Y = 12f;
        private const float TOOLTIP_WIDTH = 200f;
        private const float TOOLTIP_HEIGHT = 22f;

        #endregion

        #region 生命周期

        private void OnEnable()
        {


            _targetCreator = target as PathCreator;
            // Debug.Log("PathCreatorEditor OnEnable called for " + target.name);
            if (_targetCreator == null) return;

            // [优化] 在 OnEnable 中一次性查找并缓存 SerializedProperty是是。
            _profileProperty = serializedObject.FindProperty(nameof(PathCreator.profile));
            _pathDataProperty = serializedObject.FindProperty(nameof(PathCreator.pathData));

            // 初始化 Profile 引用
            if (_targetCreator.profile != null)
            {
                InitProfileEmbeddedEditor(_targetCreator.profile);
            }

            // 初始化上下文与面板
            _ctx = new PathEditorContext();
            _ctx.Initialize(_targetCreator);
            _terrainPanel = new TerrainOperationsPanel(_ctx);

            // 订阅事件
            Undo.undoRedoPerformed += OnUndoRedo;
            _targetCreator.PathModified += OnPathModified;

            MarkPathAsDirty(); // 首次启用时强制刷新
        }

        private void OnDisable()
        {
            // Debug.Log("PathCreatorEditor OnDisable called for " + target.name);
            // [优化] 增加空值检查，使清理逻辑更健壮。
            if (_profileEmbeddedEditor != null)
            {
                DestroyImmediate(_profileEmbeddedEditor);
                _profileEmbeddedEditor = null;
            }

            _ctx?.Dispose();
            _ctx = null;
            _terrainPanel = null;

            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_targetCreator != null)
            {
                _targetCreator.PathModified -= OnPathModified;
            }
        }

        #endregion

        #region GUI 绘制

        public override void OnInspectorGUI()
        {
            _targetCreator = target as PathCreator;
            if (_targetCreator == null) return;


            // [优化] 总是先调用 Update，最后调用 ApplyModifiedProperties，这是标准做法。
            serializedObject.Update();
            // [策略] 和平共存：添加UI提示
            if (Tools.current != Tool.Move && Tools.current != Tool.None) // 简化了判断条件
            {
                EditorGUILayout.HelpBox("另一个场景工具当前处于激活状态。\n请在场景左上角工具栏选择“移动工具 (W)”来编辑路径。", MessageType.Info);
            }

            DrawCoreProperties();

            // [优化] 检查 profileProperty.objectReferenceValue 而不是直接访问 _targetCreator.profile，
            // 这样可以更好地与序列化系统协同工作。
            if (_profileProperty.objectReferenceValue != null)
            {
                DrawEmbeddedProfileUI();
            }
            else
            {
                DrawProfileMissingUI();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            // [策略] 和平共存：如果当前有其他自定义工具处于激活状态，则本工具不进行绘制。
            if (Tools.current != Tool.Move &&
                Tools.current != Tool.Rotate &&
                Tools.current != Tool.Scale &&
                Tools.current != Tool.Rect &&
                Tools.current != Tool.Transform &&
                Tools.current != Tool.None) // Tool.None 意味着没有工具被激活，应该允许绘制
            {
                return; // 退出，不绘制任何 Handles
            }
            _targetCreator = target as PathCreator;
            if (_targetCreator == null) return;
            if (_ctx == null || !_ctx.IsPathValid())
            {
                _ctx?.PreviewManager?.SetActive(false);
                return;
            }
            _ctx.PreviewManager.SetActive(true);

            _ctx.PreviewManager.RefreshIfDirty(_targetCreator, _ctx.HeightProvider);

            // [优化] 将 Event.current 缓存到局部变量，轻微提升可读性和性能。
            Event currentEvent = Event.current;

            var context = _ctx.CreateHandleContext();

            // [优化] 使用 EditorGUI.EndChangeCheck 来检测句柄是否被拖动，仅在发生变化时重绘。
            EditorGUI.BeginChangeCheck();
            PathEditorHandles.Draw(ref context);
            if (EditorGUI.EndChangeCheck())
            {
                // 如果句柄被修改，标记路径为脏以触发更新。
                MarkPathAsDirty();
            }

            _ctx.UpdateHoverState(context);
            _ctx.InputHandler.HandleInputEvents(currentEvent, _targetCreator, context.hoveredPathT, context.hoveredPointIndex);

            _terrainPanel?.Draw();

            DrawCoordinateTooltip(_targetCreator, context);

            // [性能优化] 关键改动：移除 SceneView.RepaintAll()。
            // RepaintAll() 会强制重绘所有场景视图，非常耗费性能。
            // Unity 的事件系统（如鼠标移动、点击）会自动触发重绘。
            // 如果需要手动触发，也应使用更精确的 HandleUtility.Repaint()。
            // 由于 HandleInputEvents 和 PathEditorHandles 内部的交互已经能触发重绘，这里通常不需要手动调用。
            if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.MouseDrag)
            {
                HandleUtility.Repaint();

            }
        }

        #endregion

        #region UI 绘制辅助方法

        private void DrawCoreProperties()
        {
            // [优化] 使用缓存的 SerializedProperty 进行绘制。
            EditorGUILayout.PropertyField(_profileProperty);
            EditorGUILayout.PropertyField(_pathDataProperty, true);
            EditorGUILayout.Space();
        }

        private void DrawProfileMissingUI()
        {
            EditorGUILayout.HelpBox("未指定路径配置文件 (Profile)。请先创建或指定一个 Profile 资产。", MessageType.Warning);
            if (GUILayout.Button("快速创建默认 Profile"))
            {
                CreateDefaultProfile();
            }
        }

        private void DrawEmbeddedProfileUI()
        {
            var currentProfile = _profileProperty.objectReferenceValue as PathProfile;

            // [优化] 检查内嵌编辑器的目标对象是否与当前 Profile 一致。
            if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != currentProfile)
            {
                InitProfileEmbeddedEditor(currentProfile);
            }

            if (_profileEmbeddedEditor == null) return;

            using (new EditorGUILayout.VerticalScope("Box"))
            {
                _profileLocalExpanded = EditorGUILayout.Foldout(_profileLocalExpanded, "路径配置文件 (Profile)", true, EditorStyles.foldoutHeader);
                if (_profileLocalExpanded)
                {
                    EditorGUI.indentLevel++;

                    // [优化] 对内嵌编辑器的修改也使用 BeginChangeCheck/EndChangeCheck
                    EditorGUI.BeginChangeCheck();

                    _profileEmbeddedEditor.OnInspectorGUI();

                    if (EditorGUI.EndChangeCheck())
                    {
                        // 如果 Profile 被修改，通知 targetCreator
                        _targetCreator?.NotifyProfileModified();
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        #endregion

        #region 逻辑与辅助方法

        private void OnPathModified(PathChangeCommand command) => MarkPathAsDirty();
        private void OnUndoRedo() => MarkPathAsDirty();

        public void MarkPathAsDirty()
        {
            _ctx?.MarkDirty();
        }

        // --- 辅助方法 ---

        private void InitProfileEmbeddedEditor(PathProfile profile)
        {
            if (_profileEmbeddedEditor != null)
            {
                DestroyImmediate(_profileEmbeddedEditor);
            }
            _profileEmbeddedEditor = CreateEditor(profile);
        }

        private void CreateDefaultProfile()
        {
            // [优化] 使用 EditorUtility.SaveFilePanelInProject 让用户选择保存路径，而不是硬编码。
            // 这是创建新资产的标准做法，更灵活、更健壮。
            string path = EditorUtility.SaveFilePanelInProject(
                "创建新的路径配置文件",
                "New PathProfile.asset",
                "asset",
                "请输入要保存的配置文件名"
            );

            if (string.IsNullOrEmpty(path))
            {
                // 用户取消了保存操作
                return;
            }

            var newProfile = CreateInstance<PathProfile>();
            AssetDatabase.CreateAsset(newProfile, path);
            AssetDatabase.SaveAssets();

            // [优化] 使用缓存的属性来赋值。
            _profileProperty.objectReferenceValue = newProfile;
            serializedObject.ApplyModifiedProperties(); // 立即应用更改

            EditorGUIUtility.PingObject(newProfile);
        }

        private void DrawCoordinateTooltip(PathCreator creator, PathEditorHandles.HandleDrawContext context)
        {
            if (creator != null && context.hoveredPathT > -1)
            {
                Vector3 worldPos = creator.GetPointAt(context.hoveredPathT);
                Handles.BeginGUI();
                Vector2 screen = HandleUtility.WorldToGUIPoint(worldPos);

                // [优化] 使用预定义的常量，避免魔法数字。
                Rect rect = new Rect(
                    screen.x + TOOLTIP_OFFSET_X,
                    screen.y + TOOLTIP_OFFSET_Y,
                    TOOLTIP_WIDTH,
                    TOOLTIP_HEIGHT
                );

                GUI.Label(rect, $"Pos: {worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}", EditorStyles.helpBox);
                Handles.EndGUI();
            }
        }

        // [移除] 下方旧方法不再需要，其功能已整合或被替代。
        // private void DrawApplyToTerrainUI() { ... }
        // private void ExecuteOperation(PathTerrainOperation op) { ... }
        // private void InitProfileReferences(PathProfile profile) { ... }

        #endregion
    }
}