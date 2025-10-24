using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.Collections;
using __temp.MrPathV2._2.Runtime.Core; // 确保引用了 PathProfile
using UnityEditor.UIElements;
using __temp.MrPathV2._2.Editor; // 确保引用了 UIResourceLoader
using System.Linq;
using Unity.EditorCoroutines.Editor;
using System;
using Unity.Collections; // 用于 Min/Max

[CustomEditor(typeof(PathProfile))]
public class PathProfileEditor : UnityEditor.Editor
{
    // --- UI Toolkit 元素引用 (仅保留需要交互的) ---
    private Toggle _snappingToggle;
    private FloatField _heightOffsetField;
    private SliderInt _smoothnessSlider;
    private Toggle _showMeshToggle;
    private Toggle _enableDepthTestToggle;
    private ObjectField _recipeField;
    private VisualElement _previewContent; // 用于添加预览图和 Recipe 编辑器

    // --- 内嵌 Recipe 编辑器 ---
    private IMGUIContainer _recipeContainer;
    private Editor _recipeEditor;
    private VisualElement _rootElement;

    // --- 预览相关 ---
    private Texture2D _previewTexture;
    private Image _previewImage;
    private EditorCoroutine _animationCoroutine; // 使用 EditorCoroutine 类型
    private float _currentOpacity = 1f;
    private const float FadeDuration = 0.2f; // 稍快一点的动画

    // --- UXML 资源 ---
    // 保持 UXML/USS 文件名与类名一致是好习惯
    // private VisualTreeAsset _visualTree;
    // private StyleSheet _styleSheet; // 如果 USS 在 UXML 中引用了，这里就不需要了

    public override VisualElement CreateInspectorGUI()
    {
        Debug.Log("[PathProfileEditor] Loaded UXML template.");
        // 1. 加载 UXML 模板
        // 假设 UIResourceLoader 能正确加载与类名同名的 uxml
        VisualElement root = UIResourceLoader.LoadAndClone<PathProfileEditor>();

        if (root == null)
        {
            Debug.LogError("[PathProfileEditor] Failed to load UXML template.");
            return new Label("Error loading UI. Check UXML file and UIResourceLoader.");
        }
        _rootElement = root;
        // 2. 查询必要的控件 (使用 UXML 中定义的 name)
        //    注意: 查询名称应与 UXML 中的 name="" 属性匹配
        _snappingToggle = root.Q<Toggle>("SnappingToggle");
        _heightOffsetField = root.Q<FloatField>("HeightOffsetField");
        _smoothnessSlider = root.Q<SliderInt>("SmoothnessSlider");
        _showMeshToggle = root.Q<Toggle>("ShowMeshToggle");
        _enableDepthTestToggle = root.Q<Toggle>("EnableDepthTestToggle");
        _recipeField = root.Q<ObjectField>("RecipeField");
        _previewContent = root.Q<VisualElement>("preview-content");

        // 简单验证查询结果
        if (!ValidateQueriedControls())
        {
            // 即使部分查询失败，也继续绑定，核心绑定可能仍然有效
            Debug.LogWarning("[PathProfileEditor] Some UI elements could not be found. Check UXML names.");
        }

        // 3. 核心：将 SerializedObject 绑定到 VisualElement 树
        //    这会自动将 UXML 中带有 binding-path 的控件与 SerializedProperty 关联
        root.Bind(serializedObject);

        // 4. 设置 ObjectField 类型 (如果在 UXML 中未指定)
        if (_recipeField != null)
        {
            _recipeField.objectType = typeof(StylizedRoadRecipe);
            _recipeField.allowSceneObjects = false; // 通常 ScriptableObject 不允许场景引用
        }

        SetupEventHandlers(); // 第一步：绑定事件
        InitializePreview(); // 第二步：初始化预览
        InitializeRecipeEditor(); // 第三步：初始化 Recipe 编辑器
        InitializeControlStates(); // 第四步：设置初始状态（此时事件已绑定，状态变化可触发回调）

        return root;
    }

    // 验证通过 name 查询的控件是否存在
    private bool ValidateQueriedControls()
    {
        bool allValid = _snappingToggle != null && _heightOffsetField != null &&
                    _smoothnessSlider != null && _showMeshToggle != null && _enableDepthTestToggle != null &&
                    _recipeField != null && _previewContent != null;
        if (!allValid)
        {
            // 打印具体哪个控件为 null
            if (_snappingToggle == null) Debug.LogError("SnappingToggle not found!");
            if (_heightOffsetField == null) Debug.LogError("HeightOffsetField not found!");
            // 其他控件同理...
        }
        return allValid;
    }

    private void SetupEventHandlers()
    {
        // --- 地形吸附联动：直接使用 _snappingToggle（已在 CreateInspectorGUI 中查询）---
        _snappingToggle?.RegisterValueChangedCallback(evt =>
        {
            Debug.Log($"Snapping enabled: {evt.newValue}"); // 新增日志
            bool enabled = evt.newValue;
            _heightOffsetField?.SetEnabled(enabled);
            _smoothnessSlider?.SetEnabled(enabled);
            RefreshSceneView();
        });

        // --- Recipe 更改时更新内嵌编辑器：直接使用 _recipeField ---
        _recipeField?.RegisterValueChangedCallback(evt =>
        {
            UpdateRecipeEditor(evt.newValue as StylizedRoadRecipe);
            RefreshSceneView();
        });

        // --- 预览开关动画：直接使用 _showMeshToggle ---
        _showMeshToggle?.RegisterValueChangedCallback(evt =>
        {
            if (_animationCoroutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
            }
            _animationCoroutine = EditorCoroutineUtility.StartCoroutine(FadePreview(evt.newValue ? 1f : 0f), this);
            RefreshSceneView();
        });

        // --- 监听其他控件变化：基于 _rootElement 查询（此时 _rootElement 已初始化）---
        _rootElement.Q<EnumField>("CurveTypeField")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<FloatField>("RoadWidthField")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<SliderInt>("CrossSectionSegmentsSlider")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<FloatField>("FalloffWidthField")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<CurveField>("CrossSectionCurveField")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<CurveField>("FalloffShapeCurveField")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        // 无需重复查询 HeightOffset 和 SmoothnessSlider，直接用已有变量（若需监听其变化）
        _heightOffsetField?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _smoothnessSlider?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<Toggle>("EnableDepthTestToggle")?.RegisterValueChangedCallback(_ => RefreshSceneView());
        _rootElement.Q<Toggle>("AlphaPreviewToggle")?.RegisterValueChangedCallback(_ => RefreshSceneView());
    }


    // 根据绑定后的初始值设置控件状态
    private void InitializeControlStates()
    {
        // 确保控件已查询到
        if (_snappingToggle != null)
        {
            // 直接读取当前值来设置依赖控件的状态
            bool isSnappingEnabled = _snappingToggle.value;
            _heightOffsetField?.SetEnabled(isSnappingEnabled);
            _smoothnessSlider?.SetEnabled(isSnappingEnabled);
        }

        // 设置预览初始透明度
        if (_showMeshToggle != null && _previewImage != null)
        {
            _currentOpacity = _showMeshToggle.value ? 1f : 0f;
            _previewImage.style.opacity = _currentOpacity;
            if (_currentOpacity > 0.5f) RefreshSceneView(); // 如果初始可见，则生成预览
        }
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

    // 更新 Recipe 编辑器 (与之前版本类似，但更健壮)
    private void UpdateRecipeEditor(StylizedRoadRecipe recipe)
    {
        // 安全销毁旧编辑器
        if (_recipeEditor != null)
        {
            DestroyImmediate(_recipeEditor); // 使用 Object.DestroyImmediate
            _recipeEditor = null;
        }
        // 安全移除旧容器
        _recipeContainer?.RemoveFromHierarchy(); // 使用 RemoveFromHierarchy
        _recipeContainer = null;

        if (recipe != null && _previewContent != null)
        {
            _recipeEditor = Editor.CreateEditor(recipe);
            if (_recipeEditor != null)
            {
                _recipeContainer = new IMGUIContainer(() =>
                {
                    // 增加 target 检查
                    if (_recipeEditor != null && _recipeEditor.target != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.LabelField("Stylized Road Recipe", EditorStyles.boldLabel); // 添加标题
                        _recipeEditor.OnInspectorGUI();
                        if (EditorGUI.EndChangeCheck())
                        {
                            // 内嵌编辑器变化，也需要刷新预览和场景
                            RefreshSceneView();
                            // 可选: 通知 PathProfile 更新 (如果 Profile 依赖 Recipe 内部数据)
                            // (target as PathProfile)?.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
                        }
                    }
                })
                { style = { marginTop = 10 } }; // 加点间距
                _previewContent.Add(_recipeContainer);
            }
            else
            {
                Debug.LogError($"[PathProfileEditor] Failed to create editor for Recipe: {recipe.name}");
            }
        }
    }

    // --- 预览初始化与更新 ---
    private void InitializePreview()
    {
        // 确保 _previewContent 已查询成功
        if (_previewContent == null) return;

        _previewTexture = new Texture2D(256, 32, TextureFormat.RGBA32, false) { name = "PathProfilePreview" };
        _previewImage = new Image
        {
            image = _previewTexture,
            scaleMode = ScaleMode.StretchToFill,
            style = {
                 width = Length.Percent(100),
                 height = 32,
                 marginTop = 5,
                 marginBottom = 5,
                 // 初始透明度在 InitializeControlStates 中设置
             }
        };
        // 将 Image 添加到 preview-content 容器
        _previewContent.Add(_previewImage);

        // 注意：初始预览生成推迟到 InitializeControlStates 中，
        // 因为需要等待 _showMeshToggle 的值确定
    }

    // 刷新预览纹理和场景视图


    // 仅刷新场景视图 (用于 Show/Hide 切换等不改变纹理的情况)
    private void RefreshSceneView()
    {
        SceneView.RepaintAll();
    }




    // --- 预览淡入淡出动画 ---
    private IEnumerator FadePreview(float targetOpacity)
    {
        float startTime = Time.realtimeSinceStartup;
        float startOpacity = _currentOpacity;
        bool needsRefresh = targetOpacity > 0.5f && startOpacity <= 0.5f; // 是否从隐藏变为可见

        while (Time.realtimeSinceStartup - startTime < FadeDuration)
        {
            // 使用 EaseInOut 效果
            float t = (Time.realtimeSinceStartup - startTime) / FadeDuration;
            t = t * t * (3f - 2f * t); // Smoothstep
            _currentOpacity = Mathf.Lerp(startOpacity, targetOpacity, t);
            if (_previewImage != null) _previewImage.style.opacity = _currentOpacity;
            yield return null;
        }

        _currentOpacity = targetOpacity;
        if (_previewImage != null) _previewImage.style.opacity = _currentOpacity;

        // 如果是从隐藏变为可见，动画结束后再刷新一次预览图
        if (needsRefresh)
        {
            RefreshSceneView();
        }
        _animationCoroutine = null; // 标记协程结束
    }

    // --- 清理 ---
    private void OnDisable()
    {
        // 停止协程
        if (_animationCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(_animationCoroutine);
            _animationCoroutine = null;
        }

        // 安全销毁编辑器和纹理
        if (_recipeEditor != null)
        {
            UnityEngine.Object.DestroyImmediate(_recipeEditor);
            _recipeEditor = null;
        }
        if (_previewTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }
    }


}
   