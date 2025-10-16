using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditorInternal;
using UnityEngine;

namespace MrPathV2
{
    [CustomEditor(typeof(PathProfile))]
    public class PathProfileEditor : Editor
    {
        // Static inner class for styles and content caching (Professional Practice)
        private static class Styles
        {
            public static readonly GUIStyle sectionHeaderStyle;
            public static readonly GUIContent coreSettingsHeader = new("核心设置 (Core Settings)", EditorGUIUtility.IconContent("settings").image);
            public static readonly GUIContent crossSectionHeader = new("道路剖面 (Cross Section)", EditorGUIUtility.IconContent("settings").image);
            public static readonly GUIContent terrainSnappingHeader = new("地形吸附 (Terrain Snapping)", EditorGUIUtility.IconContent("settings").image);
            public static readonly GUIContent meshGenerationHeader = new("网格生成 (Mesh Generation)", EditorGUIUtility.IconContent("settings").image);
            public static readonly GUIContent previewHeader = new("渲染预览 (Preview)", EditorGUIUtility.IconContent("settings").image);
            public static readonly GUIContent layersHeader = new("自定义渲染图层 (Custom Layers)", EditorGUIUtility.IconContent("settings").image);

            public static readonly GUIContent curveType = new("曲线类型", "路径插值使用的曲线算法。");
            public static readonly GUIContent generationPrecision = new("生成精度", "数值越小，曲线越平滑，但计算成本越高。");
            public static readonly GUIContent roadWidth = new("道路宽度", "道路主体的总宽度。");
            public static readonly GUIContent crossSectionCurve = new("剖面曲线", "定义道路横截面的形状。X轴[-1, 1]代表从左到右，Y轴代表相对高度。");
            public static readonly GUIContent falloffWidth = new("边缘宽度", "道路边缘与地形融合的过渡带宽度。");
            public static readonly GUIContent falloffShape = new("边缘形状", "定义边缘过渡的形状。X轴[0, 1]从道路边缘到末端，Y轴[0, 1]代表混合权重。");
            public static readonly GUIContent snapToTerrain = new("启用地形吸附", "使路径点自动贴合下方地形。");
            public static readonly GUIContent heightOffset = new("高度偏移", "路径在地形上方的高度。");
            public static readonly GUIContent smoothness = new("平滑强度", "对吸附后的路径高度进行平滑处理的迭代次数。");
            public static readonly GUIContent forceHorizontal = new("强制水平", "使道路横截面始终保持水平，不受路径坡度影响。");
            public static readonly GUIContent crossSectionSegments = new("横截面分段", "预览网格在宽度上的分段数，越高越精细。");
            public static readonly GUIContent showPreviewMesh = new("显示预览网格", "在场景视图中实时显示生成的道路网格。");
            public static readonly GUIContent roadRecipe = new("风格化道路配方", "定义道路纹理、材质和风格的资产。");

            static Styles()
            {
                sectionHeaderStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 4, 4),
                    margin = new RectOffset(0, 0, 8, 0),
                    fontStyle = FontStyle.Bold
                };
            }
        }

        // Serialized Properties
        private SerializedProperty _curveType, _generationPrecision, _roadWidth;
        private SerializedProperty _crossSection, _falloffWidth, _falloffShape;
        private SerializedProperty _snapToTerrain, _heightOffset, _smoothness;
        private SerializedProperty _forceHorizontal, _crossSectionSegments;
        private SerializedProperty _showPreviewMesh, _roadRecipe;
        private SerializedProperty _layers;

        // Editor specific fields
        private ReorderableList _layerList;
        private AnimBool _snapToTerrainFade;

        private void OnEnable()
        {
            // Cache property references
            _curveType = serializedObject.FindProperty(nameof(PathProfile.curveType));
            _generationPrecision = serializedObject.FindProperty(nameof(PathProfile.generationPrecision));
            _roadWidth = serializedObject.FindProperty(nameof(PathProfile.roadWidth));

            _crossSection = serializedObject.FindProperty(nameof(PathProfile.crossSection));
            _falloffWidth = serializedObject.FindProperty(nameof(PathProfile.falloffWidth));
            _falloffShape = serializedObject.FindProperty(nameof(PathProfile.falloffShape));

            _snapToTerrain = serializedObject.FindProperty(nameof(PathProfile.snapToTerrain));
            _heightOffset = serializedObject.FindProperty(nameof(PathProfile.heightOffset));
            _smoothness = serializedObject.FindProperty(nameof(PathProfile.smoothness));

            _forceHorizontal = serializedObject.FindProperty(nameof(PathProfile.forceHorizontal));
            _crossSectionSegments = serializedObject.FindProperty(nameof(PathProfile.crossSectionSegments));

            _showPreviewMesh = serializedObject.FindProperty(nameof(PathProfile.showPreviewMesh));
            _roadRecipe = serializedObject.FindProperty(nameof(PathProfile.roadRecipe));

            _layers = serializedObject.FindProperty("layers");
            InitializeLayerList();

            // Initialize AnimBool for smooth fade group
            _snapToTerrainFade = new AnimBool(_snapToTerrain.boolValue);
            _snapToTerrainFade.valueChanged.AddListener(Repaint);
        }

        private void OnDisable()
        {
            _snapToTerrainFade.valueChanged.RemoveListener(Repaint);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Set animation target
            _snapToTerrainFade.target = _snapToTerrain.boolValue;

            DrawCoreSettings();
            DrawCrossSectionSettings();
            DrawTerrainSnappingSettings();
            DrawMeshGenerationSettings();
            DrawPreviewSettings();
            DrawLayersSettings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSectionHeader(GUIContent label)
        {
            EditorGUILayout.LabelField(label, Styles.sectionHeaderStyle);
        }

        private void DrawCoreSettings()
        {
            DrawSectionHeader(Styles.coreSettingsHeader);
            EditorGUILayout.PropertyField(_curveType, Styles.curveType);
            EditorGUILayout.PropertyField(_generationPrecision, Styles.generationPrecision);
            EditorGUILayout.PropertyField(_roadWidth, Styles.roadWidth);
        }

        private void DrawCrossSectionSettings()
        {
            DrawSectionHeader(Styles.crossSectionHeader);
            EditorGUILayout.PropertyField(_crossSection, Styles.crossSectionCurve);
            EditorGUILayout.PropertyField(_falloffWidth, Styles.falloffWidth);
            EditorGUILayout.PropertyField(_falloffShape, Styles.falloffShape);
        }

        private void DrawTerrainSnappingSettings()
        {
            DrawSectionHeader(Styles.terrainSnappingHeader);
            EditorGUILayout.PropertyField(_snapToTerrain, Styles.snapToTerrain);


            if (EditorGUILayout.BeginFadeGroup(_snapToTerrainFade.faded))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_heightOffset, Styles.heightOffset);
                EditorGUILayout.PropertyField(_smoothness, Styles.smoothness);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFadeGroup();
        }

        private void DrawMeshGenerationSettings()
        {
            DrawSectionHeader(Styles.meshGenerationHeader);
            EditorGUILayout.PropertyField(_forceHorizontal, Styles.forceHorizontal);
            EditorGUILayout.PropertyField(_crossSectionSegments, Styles.crossSectionSegments);
        }

        private void DrawPreviewSettings()
        {
            DrawSectionHeader(Styles.previewHeader);
            EditorGUILayout.PropertyField(_showPreviewMesh, Styles.showPreviewMesh);
            EditorGUILayout.PropertyField(_roadRecipe, Styles.roadRecipe);

            // Provide helpful guidance to the user
            if (_roadRecipe.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("请分配一个 StylizedRoadRecipe 以定义道路的视觉风格。", MessageType.Info);
            }
        }

        private void DrawLayersSettings()
        {
            if (_layers == null) return;

            DrawSectionHeader(Styles.layersHeader);
            _layerList.DoLayoutList();
        }

        private void InitializeLayerList()
        {
            if (_layers == null) return;

            _layerList = new ReorderableList(serializedObject, _layers, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "路径渲染图层 (Path Render Layers)"),
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = _layers.GetArrayElementAtIndex(index);
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(rect, element, GUIContent.none, true);
                },
                elementHeightCallback = index => EditorGUI.GetPropertyHeight(_layers.GetArrayElementAtIndex(index), true) + 4
            };
        }
    }
}