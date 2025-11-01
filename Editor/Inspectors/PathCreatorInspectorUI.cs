using System;
using __temp.MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.Inspectors
{
    /// <summary>
    /// 处理PathCreator的Inspector UI逻辑
    /// </summary>
    public class PathCreatorInspectorUI
    {
        private readonly PathCreatorEditor _editor;
        private readonly PathCreator _targetCreator;

        // UI元素引用
        private VisualElement _rootElement;
        private VisualElement _profileMissingWarning;
        private Button _createProfileButton;
        private VisualElement _profileEmbeddedContainer;
        private VisualElement _recipeEmbeddedContainer;
        private VisualElement _profileInspectorUI;
        private IMGUIContainer _recipeInspectorIMGUI;

        // 内嵌编辑器
        private UnityEditor.Editor _profileEmbeddedEditor;
        private UnityEditor.Editor _recipeEmbeddedEditor;

        public PathCreatorInspectorUI(PathCreatorEditor editor, PathCreator target)
        {
            _editor = editor;
            _targetCreator = target;
        }

        public VisualElement CreateInspectorGUI(SerializedObject serializedObject)
        {
            _rootElement = UIResourceLoader.LoadAndCloneByName(nameof(PathCreatorEditor));

            // 自动将 SerializedObject 绑定到 UXML
            _rootElement.Bind(serializedObject);

            // 查询 UXML 中的元素
            _profileMissingWarning = _rootElement.Q<VisualElement>("profileMissingWarning");
            _createProfileButton = _rootElement.Q<Button>("createProfileButton");
            _profileEmbeddedContainer = _rootElement.Q<VisualElement>("profileEmbeddedContainer");
            _recipeEmbeddedContainer = _rootElement.Q<VisualElement>("recipeEmbeddedContainer");

            var profileField = _rootElement.Q<PropertyField>("profileProperty");

            // 注册事件回调
            _createProfileButton.clicked += CreateDefaultProfile;
            profileField.RegisterValueChangeCallback(OnProfilePropertyChanged);

            // 立即初始化 UI 状态，避免延迟调用导致的闪烁
            try
            {
                UpdateEmbeddedEditorUI(_targetCreator ? _targetCreator.profile : null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PathCreatorEditor] UI initialization failed: {e.Message}");
                // 如果立即初始化失败，则使用延迟调用作为后备方案
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (_rootElement == null) return;
                        UpdateEmbeddedEditorUI(_targetCreator ? _targetCreator.profile : null);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PathCreatorEditor] Delayed UI build failed: {ex.Message}");
                    }
                };
            }

            return _rootElement;
        }

        /// <summary>
        /// 当 Profile 属性在 Inspector 中被更改时调用
        /// </summary>
        private void OnProfilePropertyChanged(SerializedPropertyChangeEvent evt)
        {
            var newProfile = evt.changedProperty.objectReferenceValue as PathProfile;

            // 重新订阅事件
            _editor.UnsubscribeFromLastProfile();
            _editor.SubscribeToProfile(newProfile);

            // 更新 UI
            UpdateEmbeddedEditorUI(newProfile);

            // 防抖刷新
            _editor.MarkPathAsDirtyDebounced();
        }

        /// <summary>
        /// 根据当前的 Path Profile 更新嵌套编辑器的UI和实例。
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
            bool isCurrentlyVisible = _profileEmbeddedContainer.style.display == DisplayStyle.Flex;

            // 只有在状态真正改变时才更新显示状态，避免不必要的布局重计算
            if (hasValidProfile != isCurrentlyVisible)
            {
                // 使用批量样式更新，减少重绘次数
                _profileMissingWarning.style.display = hasValidProfile ? DisplayStyle.None : DisplayStyle.Flex;
                _profileEmbeddedContainer.style.display = hasValidProfile ? DisplayStyle.Flex : DisplayStyle.None;

                // 强制立即应用样式变更，避免延迟导致的闪烁
                _rootElement.MarkDirtyRepaint();
            }

            if (!hasValidProfile)
            {
                // 清理资源，使用短路逻辑减少判断
                if (_profileEmbeddedEditor == null) return;
                _editor.SafeDestroyEditor(ref _profileEmbeddedEditor);
                ClearProfileUI();
                return;
            }

            // 仅在需要时更新编辑器（使用直接比较替代逻辑判断）
            if (_profileEmbeddedEditor?.target == currentProfile) return;
            _editor.SafeDestroyEditor(ref _profileEmbeddedEditor);
            _profileEmbeddedEditor = UnityEditor.Editor.CreateEditor(currentProfile);
            RebuildProfileUI();
        }

        // 提取清理UI的逻辑，避免代码重复
        private void ClearProfileUI()
        {
            if (_profileInspectorUI == null) return;
            _profileInspectorUI.RemoveFromHierarchy();
            _profileInspectorUI = null;
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
                    if (editorRef == null) return;
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
            // 提前返回：检查必要组件是否存在
            if (_rootElement == null || _recipeEmbeddedContainer == null) return;

            // 提前返回：如果当前没有配置文件，则隐藏容器并清理资源
            if (currentProfile == null)
            {
                if (_recipeEmbeddedContainer.style.display != DisplayStyle.None)
                {
                    _recipeEmbeddedContainer.style.display = DisplayStyle.None;
                    _rootElement.MarkDirtyRepaint();
                }
                CleanupRecipeEditor();
                return;
            }

            // 检查配方是否已经嵌入到配置文件检查器中
            var recipeAlreadyEmbeddedInProfile = _profileInspectorUI?.Q<VisualElement>("preview-content") != null;

            // 提前返回：如果配方已嵌入到配置文件检查器中，则隐藏独立区域并清理实例
            if (recipeAlreadyEmbeddedInProfile)
            {
                if (_recipeEmbeddedContainer.style.display != DisplayStyle.None)
                {
                    _recipeEmbeddedContainer.style.display = DisplayStyle.None;
                    _rootElement.MarkDirtyRepaint();
                }
                CleanupRecipeEditor();
                return;
            }

            // 获取当前配方
            var currentRecipe = currentProfile.roadRecipe;
            bool shouldShow = currentRecipe != null;
            bool isCurrentlyVisible = _recipeEmbeddedContainer.style.display == DisplayStyle.Flex;

            // 只有在显示状态真正改变时才更新，避免不必要的重绘
            if (shouldShow != isCurrentlyVisible)
            {
                _recipeEmbeddedContainer.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
                _rootElement.MarkDirtyRepaint();
            }

            // 提前返回：如果没有当前配方，则清理资源并退出
            if (currentRecipe == null)
            {
                CleanupRecipeEditor();
                return;
            }

            // 检查是否需要更新编辑器
            var editorNeedsUpdate = _recipeEmbeddedEditor == null || _recipeEmbeddedEditor.target != currentRecipe;
            if (!editorNeedsUpdate) return;

            // 更新编辑器
            SetupRecipeEditor(currentRecipe);
        }

        /// <summary>
        /// 清理配方编辑器资源
        /// </summary>
        private void CleanupRecipeEditor()
        {
            if (_recipeInspectorIMGUI != null)
            {
                _recipeInspectorIMGUI.RemoveFromHierarchy();
                _recipeInspectorIMGUI = null;
            }
            _editor.SafeDestroyEditor(ref _recipeEmbeddedEditor);
        }

        /// <summary>
        /// 设置配方编辑器
        /// </summary>
        /// <param name="recipe">要编辑的配方</param>
        private void SetupRecipeEditor(StylizedRoadRecipe recipe)
        {
            _editor.SafeDestroyEditor(ref _recipeEmbeddedEditor);
            _recipeEmbeddedEditor = UnityEditor.Editor.CreateEditor(recipe);

            if (_recipeInspectorIMGUI != null)
            {
                _recipeInspectorIMGUI.RemoveFromHierarchy();
                _recipeInspectorIMGUI = null;
            }

            var recipeImgui = new IMGUIContainer(() =>
            {
                if (_recipeEmbeddedEditor == null) return;
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

        private void CreateDefaultProfile()
        {
            // 使用 EditorUtility.SaveFilePanelInProject
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

            var newProfile = ScriptableObject.CreateInstance<PathProfile>();
            AssetDatabase.CreateAsset(newProfile, path);
            AssetDatabase.SaveAssets();

            // 通知编辑器更新
            _editor.OnProfileCreated(newProfile);

            EditorGUIUtility.PingObject(newProfile);
        }

        public void OnDestroy()
        {
            _editor.SafeDestroyEditor(ref _profileEmbeddedEditor);
            _editor.SafeDestroyEditor(ref _recipeEmbeddedEditor);

            if (_profileInspectorUI != null)
            {
                _profileInspectorUI.RemoveFromHierarchy();
                _profileInspectorUI = null;
            }

            if (_recipeInspectorIMGUI != null)
            {
                _recipeInspectorIMGUI.RemoveFromHierarchy();
                _recipeInspectorIMGUI = null;
            }
        }
    }
}
