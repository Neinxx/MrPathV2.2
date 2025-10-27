// ReSharper disable InconsistentNaming
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// 确保引用了 PathProfile
using objiect = UnityEngine.Object;
// 确保引用了 UIResourceLoader
// 引入刷新管理器

namespace MrPathV2.Editor.Inspectors
{

    [CustomEditor(typeof(PathProfile))]
    public class PathProfileEditor : UnityEditor.Editor
    {

        private StylizedRoadRecipe _currentRecipeRef;

        // 从 SetupEventHandlers 移入的
        private Toggle _enableDepthTestToggle;
        // --- UI Toolkit 元素引用 ---

        // 在 CreateInspectorGUI 中查询的
        private FloatField _heightOffsetField;
        private FloatField _meshWidthField; // 对应 "MeshWithField"
        private Toggle _opaquePreviewToggle;
        private VisualElement _previewContent;
        // 复合视图
        private CompositeCurveView _compositeView;

        // --- 内嵌 Recipe 编辑器 ---
        private IMGUIContainer _recipeContainer;
        private UnityEditor.Editor _recipeEditor;
        private ObjectField _recipeField;
        private VisualElement _rootElement;
        private Toggle _showMeshToggle;
        private SliderInt _smoothnessSlider;
        private Toggle _snappingToggle;

        // 订阅的 Profile 实例引用，用于解除订阅
        private PathProfile _subscribedProfile;

        // 在启用时订阅 ProfileModified，确保 OnValidate 引发的事件以防抖方式稳定刷新
        private void OnEnable()
        {
            _subscribedProfile = target as PathProfile;
            if (_subscribedProfile != null)
            {
                _subscribedProfile.ProfileModified += OnProfileAssetModified;
            }
        }

        // --- 清理 ---
        private void OnDisable()
        {
            // 取消订阅，避免多次触发或泄漏
            if (_subscribedProfile != null)
            {
                _subscribedProfile.ProfileModified -= OnProfileAssetModified;
                _subscribedProfile = null;
            }

            // 清理可能存在的嵌入式编辑器
            if (_recipeEditor != null)
            {
                DestroyImmediate(_recipeEditor);
                _recipeEditor = null;
            }

            // 移除并清空容器，避免残留引用
            if (_recipeContainer != null)
            {
                _recipeContainer.RemoveFromHierarchy();
                _recipeContainer = null;
            }

            // 清理复合视图
            if (_compositeView != null)
            {
                _compositeView.RemoveFromHierarchy();
                _compositeView = null;
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            //  Debug.Log("[PathProfileEditor] Loaded UXML template.");

            // 1. 加载 UXML 模板
            var root = UIResourceLoader.LoadAndClone<PathProfileEditor>();
            if (root == null)
            {
                ErrorHandler.LogError("[PathProfileEditor]  Failed to load UXML template.");
                return new Label("Error loading UI. Check UXML file and UIResourceLoader.");
            }
            _rootElement = root;

            // 2. 集中查询所有需要的控件
            QueryUIElements();

            // 3. 修正 UXML 中潜在的 binding-path 不匹配
            FixBindingPaths();

            // 4. 核心：将 SerializedObject 绑定到 VisualElement 树
            root.Bind(serializedObject);

            // 5. 设置 ObjectField 类型 (Bind 之后)
            SetupObjectFieldTypes();

            // 6. 注册事件处理器（仅 UI 联动，不做刷新）
            SetupEventHandlers();

            // 6.5 初始化复合视图（曲线叠加 + 宽度联动）
            InitializeCompositeView();

            // 7. 初始化内嵌编辑器
            InitializeRecipeEditor();

            // 8. 根据绑定后的初始值设置控件状态（仅 UI 联动）
            InitializeControlStates();

            return root;
        }

        /// <summary>
        ///     步骤 2: 集中查询所有需要的UI元素。
        /// </summary>
        private void QueryUIElements()
        {
            _heightOffsetField = _rootElement.Q<FloatField>("HeightOffsetField");
            _smoothnessSlider = _rootElement.Q<SliderInt>("SmoothnessField");
            _showMeshToggle = _rootElement.Q<Toggle>("ShowMeshToggleField");
            _opaquePreviewToggle = _rootElement.Q<Toggle>("OpaquePreviewToggleField");
            _rootElement.Q<Toggle>("ForceHorizontalField");
            _enableDepthTestToggle = _rootElement.Q<Toggle>("EnableDepthTestToggleField");
            _snappingToggle = _rootElement.Q<Toggle>("SnappingToggleField");
            _recipeField = _rootElement.Q<ObjectField>("RecipeField");
            _previewContent = _rootElement.Q<VisualElement>("preview-content");

            // 从原 SetupEventHandlers 中移入的查询
            _rootElement.Q<EnumField>("CurveTypeField");
            _meshWidthField = _rootElement.Q<FloatField>("MeshWithField");
            _rootElement.Q<SliderInt>("CrossSectionSegmentsField");
            _rootElement.Q<FloatField>("FalloffWidthField");
            _rootElement.Q<CurveField>("CrossSectionCurveField");
            _rootElement.Q<CurveField>("FalloffShapeCurveField");
        }

        /// <summary>
        ///     步骤 3: 修正部分控件的 bindingPath（UXML 可能存在拼写差异）
        /// </summary>
        private void FixBindingPaths()
        {
            // 现在使用已查询的字段，不再执行 Q<T>
            if (_meshWidthField != null) _meshWidthField.bindingPath = nameof(PathProfile.roadWidth);
            if (_snappingToggle != null) _snappingToggle.bindingPath = nameof(PathProfile.snapToTerrain);
            if (_smoothnessSlider != null) _smoothnessSlider.bindingPath = nameof(PathProfile.smoothness);
            if (_heightOffsetField != null) _heightOffsetField.bindingPath = nameof(PathProfile.heightOffset);
            if (_showMeshToggle != null) _showMeshToggle.bindingPath = nameof(PathProfile.showPreviewMesh);
            if (_enableDepthTestToggle != null) _enableDepthTestToggle.bindingPath = nameof(PathProfile.enableDepthTest);
            if (_opaquePreviewToggle != null) _opaquePreviewToggle.bindingPath = nameof(PathProfile.opaquePreview);
            if (_recipeField != null) _recipeField.bindingPath = nameof(PathProfile.roadRecipe);
        }

        /// <summary>
        ///     步骤 5: 设置特定控件的属性 (如 ObjectField 类型)
        /// </summary>
        private void SetupObjectFieldTypes()
        {
            if (_recipeField != null)
            {
                _recipeField.objectType = typeof(StylizedRoadRecipe);
                _recipeField.allowSceneObjects = false; // 通常 ScriptableObject 不允许场景引用
            }
        }

        /// <summary>
        ///     步骤 6: 设置所有UI元素的事件处理器（仅 UI 联动）。
        /// </summary>
        private void SetupEventHandlers()
        {
            RegisterSimpleRefreshEvents();
            RegisterComplexEvents();
        }

        /// <summary>
        ///     仅做 UI 联动，不处理刷新（依赖数据层 OnValidate）。
        /// </summary>
        private void RegisterSimpleRefreshEvents()
        {
            // 不做任何刷新调用，序列化绑定会驱动数据变更，PathProfile.OnValidate 负责刷新
        }

        /// <summary>
        ///     注册那些有特殊交互逻辑的控件（纯 UI 联动）。
        /// </summary>
        private void RegisterComplexEvents()
        {
            // Recipe 字段：只更新内嵌编辑器
            _recipeField?.RegisterValueChangedCallback(OnRecipeChanged);

            // 地形吸附 Toggle：控制其他控件的启用状态
            _snappingToggle?.RegisterValueChangedCallback(OnSnappingToggled);
        }

        // --- 具体的事件处理方法 (Event Handlers) ---

        private void OnRecipeChanged(ChangeEvent<objiect> evt)
        {
            UpdateRecipeEditor(evt.newValue as StylizedRoadRecipe);
            // 不做刷新；Recipe 的变更将由 PathProfile 订阅并触发 ProfileModified
        }

        private void OnSnappingToggled(ChangeEvent<bool> evt)
        {
            var enabled = evt.newValue;
            _heightOffsetField?.SetEnabled(enabled);
            _smoothnessSlider?.SetEnabled(enabled);
        }
        // --- 复合视图集成 ---
        private void InitializeCompositeView()
        {
            var profile = target as PathProfile;
            if (profile == null) return;

            _compositeView = new CompositeCurveView();
            _compositeView.SetProfile(profile);
            _compositeView.style.flexGrow = 1;
            _compositeView.style.minHeight = 180;

            // 优先挂载到 UXML 中名为 "CompositeCurveView" 的容器
            var uxmlMount = _rootElement.Q<VisualElement>("CompositeCurveView");
            if (uxmlMount != null)
            {
                uxmlMount.Clear();
                uxmlMount.Add(_compositeView);
            }
            else if (_previewContent != null)
            {
                // 兜底：插入到 preview-content 顶部，避免无容器时丢失视图
                _previewContent.Insert(0, _compositeView);
            }
        }

        // 根据绑定后的初始值设置控件状态（仅 UI 联动）
        private void InitializeControlStates()
        {
            if (_snappingToggle != null)
            {
                var isSnappingEnabled = _snappingToggle.value;
                _heightOffsetField?.SetEnabled(isSnappingEnabled);
                _smoothnessSlider?.SetEnabled(isSnappingEnabled);
            }
        }

        // 当 ProfileModified 被触发（来自 OnValidate 或 Recipe 变更）时，仅重绘
        private void OnProfileAssetModified()
        {
            // 不执行 Inspector 重绑定或 SetDirty，避免打断交互
            Repaint();
        }

        // 初始化 Recipe 编辑器 (基于 SerializedProperty 的当前值)
        private void InitializeRecipeEditor()
        {
            var recipeProp = serializedObject.FindProperty("roadRecipe");
            if (recipeProp != null)
            {
                UpdateRecipeEditor(recipeProp.objectReferenceValue as StylizedRoadRecipe);
            }
        }

        // 更新 Recipe 编辑器 (避免重复创建，稳健刷新)
        private void UpdateRecipeEditor(StylizedRoadRecipe recipe)
        {
            if (_currentRecipeRef == recipe && _recipeEditor && _recipeContainer != null)
            {
                return; // 无变化，跳过
            }
            _currentRecipeRef = recipe;

            if (_recipeEditor)
            {
                DestroyImmediate(_recipeEditor);
                _recipeEditor = null;
            }
            _recipeContainer?.RemoveFromHierarchy();
            _recipeContainer = null;

            if (!recipe) return;

            _recipeEditor = CreateEditor(recipe);
            if (!_recipeEditor)
            {
                ErrorHandler.LogError($"[PathProfileEditor] Failed to create editor for Recipe: {recipe.name}");
                return;
            }

            _recipeContainer = new IMGUIContainer(() =>
            {
                if (_recipeEditor && _recipeEditor.target)
                {
                    EditorGUILayout.LabelField("Stylized Road Recipe", EditorStyles.boldLabel);
                    _recipeEditor.OnInspectorGUI();
                }
            })
            {
                style =
                {
                    marginTop = 10,
                    minHeight = 120,
                    flexGrow = 1
                }
            };

            // 挂载到预览内容区（保持原有布局）
            _previewContent?.Add(_recipeContainer);
        }
    }
}
