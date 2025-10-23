#if UNITY_EDITOR
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Preview;
using __temp.MrPathV2._2.Runtime.Settings;
using __temp.MrPathV2._2.Runtime.Strategies;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements; // <--- 引入 UI Toolkit
using UnityEngine;
using UnityEngine.UIElements; // <--- 引入 UI Toolkit
// alias UnityEditor.Tools to avoid namespace conflict
using UnityEditorTools = UnityEditor.Tools;

namespace __temp.MrPathV2._2.Editor.Inspectors
{
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : UnityEditor.Editor
    {
        #region 字段

        private PathCreator _targetCreator;

        // --- 序列化属性缓存 ---
        private SerializedProperty _profileProperty;
        private SerializedProperty _pathDataProperty;

        // --- 内嵌编辑器 ---
        private UnityEditor.Editor _profileEmbeddedEditor;
        private bool _profileLocalExpanded = true;
        private UnityEditor.Editor _recipeEmbeddedEditor;
        private bool _recipeLocalExpanded = true;

        // --- 核心上下文 ---
        private PathEditorContext _ctx;

        // --- Profile 事件订阅跟踪 ---
        private PathProfile _lastSubscribedProfile;

        // --- Transform 变化跟踪 ---
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private Vector3 _lastScale;

        // --- UI Toolkit 元素 ---
        private VisualElement _rootElement;
        private VisualElement _profileMissingWarning;
        private Button _createProfileButton;
        private IMGUIContainer _profileEmbeddedContainer;
        private IMGUIContainer _recipeEmbeddedContainer;

        #endregion

        #region 生命周期 (OnEnable / OnDisable)

        private void OnEnable()
        {
            _targetCreator = target as PathCreator;
            if (!_targetCreator) return;

            // 缓存 SerializedProperty
            _profileProperty = serializedObject.FindProperty(nameof(PathCreator.profile));
            _pathDataProperty = serializedObject.FindProperty(nameof(PathCreator.pathData));

            // 初始化上下文
            _ctx = new PathEditorContext(_targetCreator);
            _ctx.Initialize(_targetCreator);

            // 订阅核心事件
            Undo.undoRedoPerformed += OnUndoRedo;
            _targetCreator.CurveDefinitionChanged += OnCurveDefinitionChanged;
            _targetCreator.AppearanceChanged += OnAppearanceChanged;
            _targetCreator.TerrainInteractionChanged += OnTerrainInteractionChanged;

            // 订阅初始 Profile 的修改事件
            SubscribeToProfile(_targetCreator.profile);

            // 标记为脏以进行初始刷新
            MarkPathAsDirty();
            CacheTransform();
        }

        private void OnDisable()
        {
            // [优化] 增加空值检查
            if (_profileEmbeddedEditor) DestroyImmediate(_profileEmbeddedEditor);
            if (_recipeEmbeddedEditor) DestroyImmediate(_recipeEmbeddedEditor);

            _ctx?.Dispose();
            _ctx = null;

            Undo.undoRedoPerformed -= OnUndoRedo;

            if (_targetCreator)
            {
                _targetCreator.CurveDefinitionChanged -= OnCurveDefinitionChanged;
                _targetCreator.AppearanceChanged -= OnAppearanceChanged;
                _targetCreator.TerrainInteractionChanged -= OnTerrainInteractionChanged;
            }

            // 取消订阅 Profile 事件
            UnsubscribeFromLastProfile();
        }

        #endregion

        #region GUI 绘制 (UI Toolkit)

        /// <summary>
        /// [重构] 使用 CreateInspectorGUI 替换 OnInspectorGUI
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            _rootElement = new VisualElement();
            //_rootElement = UIResourceLoader.LoadAndCloneByName(nameof(PathCreatorEditor));
            _rootElement = UIResourceLoader.LoadAndClone<PathCreatorEditor>();






            // 自动将 SerializedObject 绑定到 UXML (PropertyField 会自动生效)
            _rootElement.Bind(serializedObject);

            // --- 查询 UXML 中的元素 ---
            _profileMissingWarning = _rootElement.Q<VisualElement>("profileMissingWarning");
            _createProfileButton = _rootElement.Q<Button>("createProfileButton");
            _profileEmbeddedContainer = _rootElement.Q<IMGUIContainer>("profileEmbeddedContainer");
            _recipeEmbeddedContainer = _rootElement.Q<IMGUIContainer>("recipeEmbeddedContainer");

            var profileField = _rootElement.Q<PropertyField>("profileProperty"); // UXML 中绑定的字段

            // --- 注册事件回调 ---

            // 1. 监听 "创建 Profile" 按钮点击
            _createProfileButton.clicked += CreateDefaultProfile;

            // 2. [关键] 监听 Profile 字段的变化，而不是在 OnInspectorGUI 中每帧检查
            profileField.RegisterValueChangeCallback(OnProfilePropertyChanged);

            // 3. 为内嵌编辑器设置 IMGUI 绘制处理器
            _profileEmbeddedContainer.onGUIHandler = DrawEmbeddedProfileUI;
            _recipeEmbeddedContainer.onGUIHandler = DrawEmbeddedRecipeUI;

            // --- 初始化 UI 状态 ---
            UpdateEmbeddedEditorUI(_targetCreator.profile);

            return _rootElement;
        }

        /// <summary>
        /// 当 Profile 属性在 Inspector 中被更改时调用
        /// </summary>
        private void OnProfilePropertyChanged(SerializedPropertyChangeEvent evt)
        {
            var newProfile = evt.changedProperty.objectReferenceValue as PathProfile;

            // 重新订阅事件
            UnsubscribeFromLastProfile();
            SubscribeToProfile(newProfile);

            // 更新 UI
            UpdateEmbeddedEditorUI(newProfile);

            // 立即刷新
            MarkPathAsDirty();
        }

        /// <summary>
        /// 绘制 Profile 内嵌编辑器 (被 IMGUIContainer 调用)
        /// </summary>
        private void DrawEmbeddedProfileUI()
        {
            if (_targetCreator.profile == null) return;

            // [优雅] 重构为通用绘制方法
            DrawEmbeddedEditor(
                ref _profileEmbeddedEditor,
                _targetCreator.profile,
                ref _profileLocalExpanded,
                "路径配置文件 (Profile)",
                _targetCreator.NotifyProfileModified
            );
        }

        /// <summary>
        /// 绘制 Recipe 内嵌编辑器 (被 IMGUIContainer 调用)
        /// </summary>
        private void DrawEmbeddedRecipeUI()
        {
            if (_targetCreator.profile == null || _targetCreator.profile.roadRecipe == null) return;

            // [优雅] 重构为通用绘制方法
            DrawEmbeddedEditor(
                ref _recipeEmbeddedEditor,
                _targetCreator.profile.roadRecipe,
                ref _recipeLocalExpanded,
                "道路风格配方 (Stylized Road Recipe)",
                _targetCreator.NotifyProfileModified
            );
        }

        /// <summary>
        /// [优雅] 用于绘制内嵌编辑器的通用方法，减少代码重复
        /// </summary>
        private void DrawEmbeddedEditor(ref UnityEditor.Editor editor, Object targetAsset, ref bool foldoutState, string title, System.Action onEditAction)
        {
            if (targetAsset == null) return;

            // 检查编辑器是否需要重新创建 (例如切换了资产)
            if (!editor || editor.target != targetAsset)
            {
                if (editor) DestroyImmediate(editor);
                editor = CreateEditor(targetAsset);
            }

            if (!editor) return;

            // [优化] 使用 IMGUI 的 "Box" 风格
            using (new EditorGUILayout.VerticalScope("Box"))
            {
                foldoutState = EditorGUILayout.Foldout(foldoutState, title, true, EditorStyles.foldoutHeader);
                if (!foldoutState) return;

                EditorGUI.indentLevel++;

                EditorGUI.BeginChangeCheck();
                editor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    // 如果内嵌编辑器有修改，通知 targetCreator
                    onEditAction?.Invoke();
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// 根据当前 Profile 更新 Inspector UI 的可见性
        /// </summary>
        private void UpdateEmbeddedEditorUI(PathProfile currentProfile)
        {
            // 防御性编程：确保所有引用都不为null
            if (_profileMissingWarning == null || _profileEmbeddedContainer == null || _recipeEmbeddedContainer == null)
            {
                Debug.LogError("UI元素未正确初始化，请检查UXML加载逻辑");
                return;
            }

            // 检查当前Profile是否有效
            if (currentProfile == null)
            {
                // 显示Profile缺失警告
                _profileMissingWarning.style.display = DisplayStyle.Flex;
                _profileEmbeddedContainer.style.display = DisplayStyle.None;
                _recipeEmbeddedContainer.style.display = DisplayStyle.None;

                // 确保嵌套编辑器被清理
                if (_profileEmbeddedEditor != null)
                {
                    DestroyImmediate(_profileEmbeddedEditor);
                    _profileEmbeddedEditor = null;
                }

                if (_recipeEmbeddedEditor != null)
                {
                    DestroyImmediate(_recipeEmbeddedEditor);
                    _recipeEmbeddedEditor = null;
                }

                return;
            }

            // Profile存在的情况
            _profileMissingWarning.style.display = DisplayStyle.None;
            _profileEmbeddedContainer.style.display = DisplayStyle.Flex;

            // 检查Recipe是否存在并设置对应样式
            bool hasRecipe = currentProfile.roadRecipe != null;
            _recipeEmbeddedContainer.style.display = hasRecipe ? DisplayStyle.Flex : DisplayStyle.None;

            // 优化：在Profile变化时更新嵌套编辑器状态
            if (hasRecipe && _recipeEmbeddedEditor == null)
            {
                // 创建新的Recipe编辑器实例
                _recipeEmbeddedEditor = CreateEditor(currentProfile.roadRecipe);
            }
            else if (!hasRecipe && _recipeEmbeddedEditor != null)
            {
                // 销毁旧的Recipe编辑器实例
                DestroyImmediate(_recipeEmbeddedEditor);
                _recipeEmbeddedEditor = null;
            }

            // 确保Profile编辑器始终存在
            if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != currentProfile)
            {
                if (_profileEmbeddedEditor != null)
                {
                    DestroyImmediate(_profileEmbeddedEditor);
                }
                _profileEmbeddedEditor = CreateEditor(currentProfile);
            }
        }

        #endregion

        #region 场景 GUI (OnSceneGUI) - [模块化]

        /// <summary>
        /// 在场景中绘制的调度中心
        /// </summary>
        private void OnSceneGUI()
        {
            _targetCreator = target as PathCreator;

            // 1. 守卫与上下文检查
            if (!ContextIsValid())
            {
                _ctx?.PreviewManager?.SetActive(false);
                return;
            }

            // 2. 激活预览
            _ctx.PreviewManager.SetActive(true);

            // 3. 检查是否有其他工具处于活动状态
            if (IsOtherToolActive()) return;

            var currentEvent = Event.current;

            // 4. 清理上帧的辅助线
            CleanupPreviewLines();

            // 5. 绘制句柄并处理输入
            ProcessSceneHandlesAndInput(currentEvent);

            // 6. 更新预览网格
            _ctx.PreviewManager.Update(_targetCreator, _ctx.HeightProvider);

            // 7. 处理场景重绘
            HandleSceneRepainting(currentEvent);

            // 8. 检查 Transform 变化
            CheckForTransformChanges();
        }

        // --- OnSceneGUI 辅助方法 ---

        private bool ContextIsValid()
        {
            return _targetCreator != null && _ctx != null && _ctx.IsPathValid();
        }

        private bool IsOtherToolActive()
        {
            // [策略] 和平共存
            return ToolManager.activeToolType != typeof(Tools.PathCreatorTool) && UnityEditorTools.current != Tool.Move;
        }

        private void CleanupPreviewLines()
        {
            var context = _ctx.CreateHandleContext();
            if (context.lineRenderer == null) return;

            var currentStrategy = PathStrategyRegistry.Instance.GetStrategy(_targetCreator.profile.curveType);

            // 如果当前为贝塞尔曲线策略，则清除上一帧可能遗留的 Catmull-Rom 路径曲线
            context.lineRenderer.Clear(currentStrategy is BezierStrategy
                ? PreviewLineRenderer.LineType.PathCurve
                // 如果当前不是贝塞尔曲线策略，则清除上一帧可能遗留的贝塞尔控制线
                : PreviewLineRenderer.LineType.ControlLine);
        }

        private void ProcessSceneHandlesAndInput(Event currentEvent)
        {
            var context = _ctx.CreateHandleContext();

            EditorGUI.BeginChangeCheck();
            PathEditorHandles.Draw(ref context);
            if (EditorGUI.EndChangeCheck())
            {
                // 如果句柄被修改，标记路径为脏以触发更新。
                MarkPathAsDirty();
            }

            _ctx.UpdateHoverState(context);
            _ctx.InputHandler.HandleInputEvents(currentEvent, _targetCreator, context.hoveredPathT, context.hoveredPointIndex);
        }

        private void HandleSceneRepainting(Event currentEvent)
        {
            // [性能优化] 仅在必要时重绘
            if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.MouseDrag)
            {
                HandleUtility.Repaint();
            }
        }

        private void CheckForTransformChanges()
        {
            if (_targetCreator.transform.position == _lastPosition &&
                _targetCreator.transform.rotation == _lastRotation &&
                _targetCreator.transform.localScale == _lastScale)
            {
                return;
            }

            CacheTransform();
            MarkPathAsDirty();
        }

        #endregion

        #region 事件处理器

        // private void OnPathModified() => MarkPathAsDirty(); // 似乎已被其他事件涵盖
        private void OnCurveDefinitionChanged() => MarkPathAsDirty();
        private void OnTerrainInteractionChanged() => MarkPathAsDirty();
        private void OnAppearanceChanged()
        {
            _ctx?.PreviewManager?.MarkMaterialsDirty();
            _ctx?.RequestSceneViewRefresh();
        }
        private void OnUndoRedo() => MarkPathAsDirty();

        /// <summary>
        /// 当 Profile 资产本身被修改时调用
        /// </summary>
        private void OnProfileModified()
        {
            // 确保 Recipe 内嵌编辑器的 UI 状态也刷新
            UpdateEmbeddedEditorUI(_targetCreator.profile);

            _ctx?.PreviewManager?.MarkMaterialsDirty();
            _ctx?.RequestSceneViewRefresh(true); // 强制立即刷新
            MarkPathAsDirty();
        }



        #endregion

        #region 逻辑与辅助方法

        private void MarkPathAsDirty()
        {
            _ctx?.MarkDirty();
        }

        private void CacheTransform()
        {
            _lastPosition = _targetCreator.transform.position;
            _lastRotation = _targetCreator.transform.rotation;
            _lastScale = _targetCreator.transform.localScale;
        }

        private void SubscribeToProfile(PathProfile profile)
        {
            if (profile == null) return;

            _lastSubscribedProfile = profile;
            _lastSubscribedProfile.ProfileModified += OnProfileModified;
        }

        private void UnsubscribeFromLastProfile()
        {
            if (_lastSubscribedProfile != null)
            {
                _lastSubscribedProfile.ProfileModified -= OnProfileModified;
                _lastSubscribedProfile = null;
            }
        }

        private void CreateDefaultProfile()
        {
            // [优化] 使用 EditorUtility.SaveFilePanelInProject
            var path = EditorUtility.SaveFilePanelInProject(
                "创建新的路径配置文件",
                "New PathProfile.asset",
                "asset",
                "请输入要保存的配置文件名"
            );

            if (string.IsNullOrEmpty(path))
            {
                return; // 用户取消
            }

            var newProfile = CreateInstance<PathProfile>();
            AssetDatabase.CreateAsset(newProfile, path);
            AssetDatabase.SaveAssets();

            // [优化] 使用缓存的属性来赋值。
            _profileProperty.objectReferenceValue = newProfile;
            serializedObject.ApplyModifiedProperties(); // 立即应用更改

            EditorGUIUtility.PingObject(newProfile);
        }

        #endregion
    }
}
#endif