#if UNITY_EDITOR
using System;
using MrPathV2.Editor.Tools;
using MrPathV2;

using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
using MrPathV2.Runtime.Settings;
using MrPathV2.Runtime.Strategies;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditorTools = UnityEditor.Tools;

namespace MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : UnityEditor.Editor
    {
        #region 字段

        private PathCreator _targetCreator;

        // --- 序列化属性缓存 ---
        private SerializedProperty _profileProperty;

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

        // --- UI Toolkit 元素 ---
        private VisualElement _rootElement;
        private VisualElement _profileMissingWarning;
        private Button _createProfileButton;
        private VisualElement _profileEmbeddedContainer;
        private VisualElement _recipeEmbeddedContainer;
        private VisualElement _profileInspectorUI;
        private IMGUIContainer _recipeInspectorIMGUI;
        public PathCreatorEditor(PathCreator targetCreator)
        {
            _targetCreator = targetCreator;
        }

        #endregion

        #region 生命周期 (OnEnable / OnDisable)

        private void OnEnable()
        {
            _targetCreator = target as PathCreator;
            if (!_targetCreator) return;

            // 缓存 SerializedProperty
            _profileProperty = serializedObject.FindProperty(nameof(PathCreator.profile));
            serializedObject.FindProperty(nameof(PathCreator.pathData));

            // 初始化上下文
            _ctx = new PathEditorContext(_targetCreator);
            _ctx.Initialize(_targetCreator);

            // 订阅核心事件
            Undo.undoRedoPerformed += OnUndoRedo;
            _targetCreator.CurveDefinitionChanged += OnCurveDefinitionChanged;
            _targetCreator.AppearanceChanged += OnAppearanceChanged;


            // 订阅初始 Profile 的修改事件
            SubscribeToProfile(_targetCreator.profile);

            // 标记为脏以进行初始刷新（防抖延迟，避免加载瞬间卡顿）
           MarkPathAsDirtyDebounced();
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
            catch (ArgumentException e)
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

                }
                catch (NullReferenceException e)
                {
                    Debug.LogError($"Event unsubscription failed: {e.Message}");
                }
            }

            // 5. 取消订阅Profile事件
            try
            {
                UnsubscribeFromLastProfile();
            }
            catch (Exception e)
            {
                Debug.LogError($"Profile cleanup failed: {e}");
            }

            // 6. 额外防御：确保所有委托被清除
           // GC.Collect();
            // 避免在选择切换时强制 GC，防止卡顿；让 Unity 自己调度。
        }

        // 优化版：高效销毁编辑器的方法
        private static void SafeDestroyEditor(ref UnityEditor.Editor editor)
        {
            // 只保留必要的null检查，移除try-catch以提高性能
            // Unity的DestroyImmediate在传入null时是安全的，不会抛出异常
            if (editor)
            {
                DestroyImmediate(editor);
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

            // --- 初始化 UI 状态（延迟到下一帧，避免加载瞬间阻塞） ---
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (this == null || _rootElement == null) return;
                    UpdateEmbeddedEditorUI(_targetCreator ? _targetCreator.profile : null);
                }
                catch (Exception e)
                {
                   Debug.LogWarning($"[PathCreatorEditor] Delayed UI build failed: {e.Message}");
                }
            };

            return _rootElement;
        }

        /// <summary>
        ///     当 Profile 属性在 Inspector 中被更改时调用
        /// </summary>
        private void OnProfilePropertyChanged(SerializedPropertyChangeEvent evt)
        {
            var newProfile = evt.changedProperty.objectReferenceValue as PathProfile;

            // 重新订阅事件
            UnsubscribeFromLastProfile();
            SubscribeToProfile(newProfile);

            // 更新 UI
            UpdateEmbeddedEditorUI(newProfile);

            // 立即刷新 -> 改为防抖以降低切换卡顿
            MarkPathAsDirtyDebounced();
        }


        /// <summary>
        ///     根据当前的 Path Profile 更新嵌套编辑器的UI和实例。
        /// </summary>
        private void UpdateEmbeddedEditorUI(PathProfile currentProfile)
        {
            // 仅按区域更新，避免整面板重建
            UpdateProfileEmbeddedArea(currentProfile);
            UpdateRecipeEmbeddedArea(currentProfile);
        }

        private void UpdateProfileEmbeddedArea(PathProfile currentProfile)
        {
            // 提前检查核心组件，避免后续重复判断
            if (_rootElement == null || _profileMissingWarning == null || _profileEmbeddedContainer == null)
                return;

            bool hasValidProfile = currentProfile;

            // 状态切换时才更新显示状态，减少布局计算
            if (hasValidProfile != (_profileEmbeddedContainer.style.display == DisplayStyle.Flex))
            {
                _profileMissingWarning.style.display = hasValidProfile ? DisplayStyle.None : DisplayStyle.Flex;
                _profileEmbeddedContainer.style.display = hasValidProfile ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!hasValidProfile)
            {
                // 清理资源，使用短路逻辑减少判断
                if (_profileEmbeddedEditor)
                {
                    SafeDestroyEditor(ref _profileEmbeddedEditor);
                    ClearProfileUI();
                }
                return;
            }

            // 仅在需要时更新编辑器（使用直接比较替代逻辑判断）
            if (_profileEmbeddedEditor?.target != currentProfile)
            {
                SafeDestroyEditor(ref _profileEmbeddedEditor);
                _profileEmbeddedEditor = CreateEditor(currentProfile);
                RebuildProfileUI();
            }
        }

// 提取清理UI的逻辑，避免代码重复
        private void ClearProfileUI()
        {
            if (_profileInspectorUI != null)
            {
                _profileInspectorUI.RemoveFromHierarchy();
                _profileInspectorUI = null;
            }
        }

// 提取重建UI的逻辑，提高可读性
        private void RebuildProfileUI()
        {
            ClearProfileUI();

            VisualElement profileUI = null;
            try
            {
                profileUI = _profileEmbeddedEditor?.CreateInspectorGUI();
            }
            catch (Exception e)
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
                // 缓存目标引用，避免lambda中捕获变量
                var editorRef = _profileEmbeddedEditor;
                var imgui = new IMGUIContainer(() =>
                {
                    if (!editorRef) return;
                    EditorGUI.BeginChangeCheck();
                    editorRef.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        _targetCreator.NotifyProfileModified();
                    }
                });
                _profileInspectorUI = imgui;
                _profileEmbeddedContainer.Add(imgui);
            }
        }

        private void UpdateRecipeEmbeddedArea(PathProfile currentProfile)
        {
            if (_rootElement == null || _recipeEmbeddedContainer == null)
            {
                return;
            }

            var currentRecipe = currentProfile ? currentProfile.roadRecipe : null;
            var recipeAlreadyEmbeddedInProfile = _profileInspectorUI != null && _profileInspectorUI.Q<VisualElement>("preview-content") != null;

            if (!currentProfile)
            {
                _recipeEmbeddedContainer.style.display = DisplayStyle.None;
                if (_recipeInspectorIMGUI != null)
                {
                    _recipeInspectorIMGUI.RemoveFromHierarchy();
                    _recipeInspectorIMGUI = null;
                }
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
                return;
            }

            if (recipeAlreadyEmbeddedInProfile)
            {
                // Profile 检查器已内嵌 Recipe：隐藏独立区域并清理实例
                _recipeEmbeddedContainer.style.display = DisplayStyle.None;
                if (_recipeInspectorIMGUI != null)
                {
                    _recipeInspectorIMGUI.RemoveFromHierarchy();
                    _recipeInspectorIMGUI = null;
                }
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
                return;
            }

            _recipeEmbeddedContainer.style.display = currentRecipe ? DisplayStyle.Flex : DisplayStyle.None;

            if (!currentRecipe)
            {
                if (_recipeInspectorIMGUI != null)
                {
                    _recipeInspectorIMGUI.RemoveFromHierarchy();
                    _recipeInspectorIMGUI = null;
                }
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
                return;
            }

            var editorNeedsUpdate = !_recipeEmbeddedEditor || _recipeEmbeddedEditor.target != currentRecipe;
            if (editorNeedsUpdate)
            {
                SafeDestroyEditor(ref _recipeEmbeddedEditor);
                _recipeEmbeddedEditor = CreateEditor(currentRecipe);

                if (_recipeInspectorIMGUI != null)
                {
                    _recipeInspectorIMGUI.RemoveFromHierarchy();
                    _recipeInspectorIMGUI = null;
                }

                var recipeImgui = new IMGUIContainer(() =>
                {
                    if (!_recipeEmbeddedEditor) return;
                    EditorGUI.BeginChangeCheck();
                    _recipeEmbeddedEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        _targetCreator.NotifyProfileModified();
                    }
                });
                _recipeInspectorIMGUI = recipeImgui;
                _recipeEmbeddedContainer.Add(recipeImgui);
            }

        }

        #endregion

        #region 场景 GUI (OnSceneGUI) - [模块化]

        /// <summary>
        ///     在场景中绘制的调度中心
        /// </summary>
        private void OnSceneGUI()
        {
            _targetCreator = target as PathCreator;

            // 仍保留原有的上下文检查与全局预览状态复位逻辑
           if (!ContextIsValid())
            {
                _ctx?.PreviewManager?.SetActive(false);
                Preview.MultiPathPreviewRenderer.ActiveEditingId = 0;
                Preview.MultiPathPreviewRenderer.IsDraggingActive = false;
                return;
           }

            // 仅在使用本地预览时才激活预览管理器，避免空引用并减少不必要的状态切换
           if (!Preview.MultiPathPreviewRenderer.PreferGlobalOnly && _ctx.PreviewManager != null)
            {
               _ctx.PreviewManager.SetActive(true);
           }

            // 3. 检查是否有其他工具处于活动状态
            if (IsOtherToolActive())
            {
                // 同步全局预览状态，避免残留拖拽标记
                Preview.MultiPathPreviewRenderer.ActiveEditingId = 0;
                Preview.MultiPathPreviewRenderer.IsDraggingActive = false;
                return;
            }

            var currentEvent = Event.current;

            // 4. 清理上帧的辅助线
            CleanupPreviewLines();

            // 5. 绘制句柄并处理输入
            ProcessSceneHandlesAndInput(currentEvent);

            // 5.1 将拖拽状态同步到全局多路径预览
            // ... 同步拖拽状态给全局渲染器（全局优先模式下不设置，避免跳过当前对象）


                        if (!Preview.MultiPathPreviewRenderer.PreferGlobalOnly)
                        {
                            Preview.MultiPathPreviewRenderer.IsDraggingActive = _ctx.IsDraggingHandle;
                            if (_targetCreator) Preview.MultiPathPreviewRenderer.ActiveEditingId = _ctx.IsDraggingHandle ? _targetCreator.GetInstanceID() : 0;
                        }
                        else
                        {
                            Preview.MultiPathPreviewRenderer.IsDraggingActive = false;
                            Preview.MultiPathPreviewRenderer.ActiveEditingId = 0;
                        }

            // 6. 更新预览网格
            // 启用全局预览时：拖拽中允许本地更新，仅避免重复绘制
            // 未启用全局预览时：始终本地更新
            if (!_targetCreator) return;

                        if (!Preview.MultiPathPreviewRenderer.IsEnabled ||
                            (!Preview.MultiPathPreviewRenderer.PreferGlobalOnly && _ctx.IsDraggingHandle))
                        {
                            _ctx.PreviewManager?.Update(_targetCreator, _ctx.HeightProvider);
                        }

            // 7. 处理场景重绘
            HandleSceneRepainting(currentEvent);
        }

        // --- OnSceneGUI 辅助方法 ---

        private bool ContextIsValid() => _targetCreator && _ctx != null && _ctx.IsPathValid();

        private bool IsOtherToolActive() =>
            // [策略] 和平共存
            ToolManager.activeToolType != typeof(PathCreatorTool) && UnityEditorTools.current != Tool.Move;

        private void CleanupPreviewLines()
        {
            var context = _ctx.CreateHandleContext();
            if (context.LineRenderer == null) return;

            var currentStrategy = PathStrategyRegistry.Instance.GetStrategy(_targetCreator.profile.curveType);

            // 如果当前为贝塞尔曲线策略，则清除上一帧可能遗留的 Catmull-Rom 路径曲线
            context.LineRenderer.Clear(currentStrategy is BezierStrategy
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
            _ctx.InputHandler.HandleInputEvents(currentEvent, _targetCreator, context.HoveredPathT, context.HoveredPointIndex);
        }

        private static void HandleSceneRepainting(Event currentEvent)
        {
            // [性能优化] 仅在必要时重绘
            if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.MouseDrag)
            {
                HandleUtility.Repaint();
            }
        }

        #endregion

        #region 事件处理器

        // private void OnPathModified() => MarkPathAsDirty(); // 似乎已被其他事件涵盖
        private void OnCurveDefinitionChanged() => MarkPathAsDirty();

        private void OnAppearanceChanged()
        {
            _ctx?.PreviewManager?.MarkMaterialsDirty();
            _ctx?.RequestPreviewRefresh(true);
            _ctx?.RequestSceneViewRefresh(true);
        }
        private void OnUndoRedo() => MarkPathAsDirty();

        /// <summary>
        ///     当 Profile 资产本身被修改时调用
        /// </summary>
        private void OnProfileModified()
        {
            if (!_targetCreator.profile) return;

            // 将重建逻辑下沉至具体区域方法，编辑器本身只进行轻量刷新
            UpdateEmbeddedEditorUI(_targetCreator.profile);
            Repaint();

            // 预览和场景刷新保持不变
            _ctx?.PreviewManager?.MarkMaterialsDirty();
            _ctx?.RequestSceneViewRefresh(true);
            MarkPathAsDirty();
        }

        #endregion

        #region 逻辑与辅助方法

        private void MarkPathAsDirty()
        {
            _ctx?.MarkDirty();
        }

        // 新增：防抖版本，避免加载与切换时的即时重计算
        private void MarkPathAsDirtyDebounced()
        {
            _ctx?.MarkDirty(false);
        }

        private void CacheTransform()
        { }

        private void SubscribeToProfile(PathProfile profile)
        {
            if (!profile) return;

            _lastSubscribedProfile = profile;
            _lastSubscribedProfile.ProfileModified += OnProfileModified;
        }

        private void UnsubscribeFromLastProfile()
        {
            if (_lastSubscribedProfile)
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
