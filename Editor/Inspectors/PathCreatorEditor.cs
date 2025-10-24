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
        private VisualElement _profileEmbeddedContainer;
        private VisualElement _recipeEmbeddedContainer;
        private VisualElement _profileInspectorUI;
        private IMGUIContainer _recipeInspectorIMGUI;

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
            // 1. 销毁编辑器实例（安全模式）
            SafeDestroyEditor(ref _profileEmbeddedEditor);
            SafeDestroyEditor(ref _recipeEmbeddedEditor);

            // 2. 清理上下文引用
            if (_ctx != null)
            {
                _ctx.Dispose();
                _ctx = null;
            }

            // 3. 取消撤销/重做回调
            try
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
            }
            catch (System.ArgumentException e)
            {
                Debug.LogWarning($"Undo callback removal failed: {e.Message}");
            }

            // 4. 清理目标创建器事件
            if (_targetCreator != null)
            {
                try
                {
                    _targetCreator.CurveDefinitionChanged -= OnCurveDefinitionChanged;
                    _targetCreator.AppearanceChanged -= OnAppearanceChanged;
                    _targetCreator.TerrainInteractionChanged -= OnTerrainInteractionChanged;
                }
                catch (System.NullReferenceException e)
                {
                    Debug.LogError($"Event unsubscription failed: {e.Message}");
                }
            }

            // 5. 取消订阅Profile事件
            try
            {
                UnsubscribeFromLastProfile();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Profile cleanup failed: {e}");
            }

            // 6. 额外防御：确保所有委托被清除
            System.GC.Collect();
        }

        // 安全销毁编辑器的方法
        private void SafeDestroyEditor(ref UnityEditor.Editor editor)
        {
            if (editor != null && editor.target != null)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(editor);
                    editor = null;
                }
                catch (UnityException e)
                {
                    Debug.LogWarning($"Editor destroy failed: {e.Message}");
                }
            }
            else if (editor != null)
            {
                editor = null;
            }
        }

        #endregion

        #region GUI 绘制 (UI Toolkit)


        public override VisualElement CreateInspectorGUI()
        {

            _rootElement = UIResourceLoader.LoadAndCloneByName(nameof(PathCreatorEditor));
            // var _rootElement = UIResourceLoader.LoadAndClone<PathCreatorEditor>();






            // 自动将 SerializedObject 绑定到 UXML (PropertyField 会自动生效)
            _rootElement.Bind(serializedObject);

            // --- 查询 UXML 中的元素 ---
            _profileMissingWarning = _rootElement.Q<VisualElement>("profileMissingWarning");
            _createProfileButton = _rootElement.Q<Button>("createProfileButton");
            _profileEmbeddedContainer = _rootElement.Q<VisualElement>("profileEmbeddedContainer");
            _recipeEmbeddedContainer = _rootElement.Q<VisualElement>("recipeEmbeddedContainer");

            var profileField = _rootElement.Q<PropertyField>("profileProperty"); // UXML 中绑定的字段

            // --- 注册事件回调 ---

            // 1. 监听 "创建 Profile" 按钮点击
            _createProfileButton.clicked += CreateDefaultProfile;

            // 2. [关键] 监听 Profile 字段的变化，而不是在 OnInspectorGUI 中每帧检查
            profileField.RegisterValueChangeCallback(OnProfilePropertyChanged);

            // 3. 使用 UI Toolkit 构建内嵌检查器（在 UpdateEmbeddedEditorUI 中完成）

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
        /// 根据当前的 PathProfile 更新嵌套编辑器的UI和实例。
        /// </summary>
        private void UpdateEmbeddedEditorUI(PathProfile currentProfile)
        {
            // 1) 防御性检查：UI 尚未准备好则直接返回
            if (_rootElement == null || _profileMissingWarning == null || _profileEmbeddedContainer == null || _recipeEmbeddedContainer == null)
            {
                return;
            }

            // 2) 每次刷新前先清空容器
            _profileEmbeddedContainer.Clear();
            _recipeEmbeddedContainer.Clear();

            // 3) 处理 Profile 为空的情况
            if (currentProfile == null)
            {
                _profileMissingWarning.style.display = DisplayStyle.Flex;
                _profileEmbeddedContainer.style.display = DisplayStyle.None;
                _recipeEmbeddedContainer.style.display = DisplayStyle.None;

                SafeDestroyEditor(ref _profileEmbeddedEditor);
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
                return;
            }

            // 4) Profile 有效：展示 Profile 容器
            _profileMissingWarning.style.display = DisplayStyle.None;
            _profileEmbeddedContainer.style.display = DisplayStyle.Flex;

            // 创建或更新 Profile 编辑器
            if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != currentProfile)
            {
                SafeDestroyEditor(ref _profileEmbeddedEditor);
                _profileEmbeddedEditor = UnityEditor.Editor.CreateEditor(currentProfile);
            }

            // 尝试使用 UI Toolkit 生成嵌入界面
            VisualElement profileUI = null;
            try
            {
                profileUI = _profileEmbeddedEditor?.CreateInspectorGUI();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Profile UITK 嵌入失败，回退 IMGUI: {e.Message}");
            }

            if (profileUI != null)
            {
                _profileInspectorUI = profileUI;
                _profileEmbeddedContainer.Add(profileUI);
            }
            else
            {
                var imgui = new IMGUIContainer(() =>
                {
                    if (_profileEmbeddedEditor != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        _profileEmbeddedEditor.OnInspectorGUI();
                        if (EditorGUI.EndChangeCheck())
                        {
                            _targetCreator.NotifyProfileModified();
                        }
                    }
                });
                _profileEmbeddedContainer.Add(imgui);
            }

            // 5) Recipe：通常为 Odin IMGUI，仅在容器内以 IMGUIContainer 回退
            var targetRecipe = currentProfile.roadRecipe;
            _recipeEmbeddedContainer.style.display = (targetRecipe != null) ? DisplayStyle.Flex : DisplayStyle.None;

            if (targetRecipe != null)
            {
                if (_recipeEmbeddedEditor == null || _recipeEmbeddedEditor.target != targetRecipe)
                {
                    SafeDestroyEditor(ref _recipeEmbeddedEditor);
                    _recipeEmbeddedEditor = UnityEditor.Editor.CreateEditor(targetRecipe);
                }

                var recipeImgui = new IMGUIContainer(() =>
                {
                    if (_recipeEmbeddedEditor != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        _recipeEmbeddedEditor.OnInspectorGUI();
                        if (EditorGUI.EndChangeCheck())
                        {
                            _targetCreator.NotifyProfileModified();
                        }
                    }
                });
                _recipeInspectorIMGUI = recipeImgui;
                _recipeEmbeddedContainer.Add(recipeImgui);
            }
            else
            {
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
            }
        }

        /// <summary>
        /// 同步一个编辑器实例(editor)以匹配一个目标对象(targetObject)。
        /// 这个方法会处理所有的生命周期逻辑：创建、销毁、或在匹配时保留。
        /// </summary>
        /// <typeparam name="T">目标对象的类型 (必须是 UnityEngine.Object)</typeparam>
        /// <param name="editor">对要管理的编辑器字段的引用 (例如 _profileEmbeddedEditor)</param>
        /// <param name="targetObject">编辑器应该显示的目标对象 (如果为null，则会销毁编辑器)</param>
        /// <param name="editorName">用于调试日志的编辑器名称 (例如 "Profile" 或 "Recipe")</param>
        private void SyncEmbeddedEditor<T>(ref UnityEditor.Editor editor, T targetObject, string editorName) where T : UnityEngine.Object
        {
            // 检查编辑器是否需要更新
            // 需要更新的条件：
            // 1. 目标对象存在，但编辑器不存在 (editor == null)
            // 2. 目标对象存在，编辑器也存在，但编辑器的目标与新目标不匹配 (editor.target != targetObject)
            // 3. 目标对象为null，但编辑器仍然存在 (editor != null)

            if (editor != null && editor.target == targetObject)
            {
                // 状态正确：目标和编辑器都存在且匹配。
                // (可选) 为详细调试取消注释下一行
                // Debug.Log($"[{editorName} Editor] 实例已是最新，无需操作。");
                return;
            }

            // --- 如果状态不匹配，则需要执行操作 ---

            // 步骤 A: 如果旧编辑器存在，则销毁它
            if (editor != null)
            {
                // 添加调试日志，说明销毁原因
                string oldTargetName = editor.target != null ? editor.target.name : "已失效的目标";
                Debug.LogWarning($"[{editorName} Editor] 销毁旧实例 (目标: {oldTargetName})。新目标: {targetObject?.name ?? "NULL"}");
                DestroyImmediate(editor);
                editor = null; // 立即设为null
            }

            // 步骤 B: 如果新目标存在，则创建新编辑器
            if (targetObject != null)
            {
                Debug.Log($"[{editorName} Editor] 为目标 '{targetObject.name}' 创建新实例。");
                editor = UnityEditor.Editor.CreateEditor(targetObject);
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