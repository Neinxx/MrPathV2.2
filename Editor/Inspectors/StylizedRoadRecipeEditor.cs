using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Reflection;
using System;
using UnityEditorInternal;
using Object = UnityEngine.Object;

namespace MrPathV2
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : Editor
    {
        private static class UIConstants
        {
            /// <summary>卡片垂直内边距</summary>
            public const float CardVerticalPadding = 2f;
            /// <summary>字段之间的标准垂直间距</summary>
            public const float FieldVerticalSpacing = 8f;
            /// <summary>折叠箭头宽度</summary>
            public const float FoldoutWidth = 15f;
            /// <summary>启用开关宽度</summary>
            public const float EnabledToggleWidth = 20f;
            /// <summary>Ping按钮宽度</summary>
            public const float PingButtonWidth = 22f;
        }

        private const int PREVIEW_WIDTH = 512;
        private const int PREVIEW_HEIGHT = 128;
        private const double MIN_UPDATE_INTERVAL = 0.1;

        private Texture2D _previewTexture;
        private double _lastUpdateTime;
        private int _lastRecipeHash;
        private StylizedRoadRecipe _recipe;

        private int _selectedPreviewMode = 0; // 0: Channels, 1: Combined
        private int _selectedChannel = 0;     // 0: R, 1: G, 2: B, 3: A

        private SerializedProperty _blendLayersProp;

        private Type[] _maskTypes;
        private string[] _maskTypeNames;
        private int _selectedMaskTypeIndex = 0;

        private ReorderableList _layerList;


        #region 生命周期

        private void OnUndoRedo()
        {
            UpdatePreviewTexture();
            Repaint();
        }

        private void OnEnable()
        {
            _blendLayersProp = serializedObject.FindProperty("blendLayers");
            _recipe = target as StylizedRoadRecipe;
            if (_recipe == null) return;

            FindAvailableMaskTypes();

            SetupReorderableList();

            Undo.undoRedoPerformed += OnUndoRedo;
            UpdatePreviewTexture();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_previewTexture != null) DestroyImmediate(_previewTexture);
        }

        #endregion

        public override void OnInspectorGUI()
        {
            var recipe = target as StylizedRoadRecipe;
            if (recipe == null) return;

            serializedObject.Update();

            DrawHeader(recipe);

            _layerList.DoLayoutList();

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                DrawPreview(recipe);
                EditorGUILayout.Space();
                DrawMaskCreator();
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                UpdatePreviewTexture();
                PropagateRecipeChange();
            }
        }

        private void DrawMaskCreator()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _selectedMaskTypeIndex = EditorGUILayout.Popup(new GUIContent("遮罩列表"), _selectedMaskTypeIndex, _maskTypeNames);

                if (GUILayout.Button("创建遮罩", GUILayout.Width(100)))
                {
                    CreateMaskAsset();
                }
            }
        }

        private void FindAvailableMaskTypes()
        {
            _maskTypes = Assembly.GetAssembly(typeof(BlendMaskBase)).GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
            _maskTypeNames = _maskTypes.Select(t => t.Name).ToArray();
        }

        private void CreateMaskAsset()
        {
            if (_maskTypes == null || _maskTypes.Length == 0) return;

            var recipe = target as StylizedRoadRecipe;
            if (recipe == null) return;

            var selectedType = _maskTypes[_selectedMaskTypeIndex];
            var newMask = ScriptableObject.CreateInstance(selectedType);

            string recipePath = AssetDatabase.GetAssetPath(recipe);
            string folder = Path.GetDirectoryName(recipePath);
            string masksFolder = Path.Combine(folder, "Masks").Replace("\\", "/");
            EnsureFolderExists(masksFolder);

            string assetPath = Path.Combine(masksFolder, $"{recipe.name}_{selectedType.Name}.asset").Replace("\\", "/");
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(newMask, uniquePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMask);
            Debug.Log($"MrPath: 已在 {uniquePath} 创建新的 {selectedType.Name} 资产。");
        }

        private void DrawHeader(StylizedRoadRecipe recipe)
        {

        }

        private void SetupReorderableList()
        {
            _layerList = new ReorderableList(serializedObject, _blendLayersProp, true, false, true, true);
            _layerList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var layerProp = _layerList.serializedProperty.GetArrayElementAtIndex(index);
                DrawLayerCard(rect, layerProp, index);
            };
            _layerList.elementHeightCallback = (index) =>
            {
                var layerProp = _layerList.serializedProperty.GetArrayElementAtIndex(index);
                return GetPropertyHeight(layerProp);
            };
            _layerList.onAddCallback = (list) =>
            {
                var index = list.serializedProperty.arraySize;
                list.serializedProperty.arraySize++;
                list.index = index;
                var newLayerProp = list.serializedProperty.GetArrayElementAtIndex(index);
                newLayerProp.FindPropertyRelative("enabled").boolValue = true;
                newLayerProp.FindPropertyRelative("opacity").floatValue = 1f;
                newLayerProp.FindPropertyRelative("blendMode").enumValueIndex = 0;
                newLayerProp.FindPropertyRelative("terrainLayer").objectReferenceValue = null;
                newLayerProp.FindPropertyRelative("mask").objectReferenceValue = null;
                newLayerProp.FindPropertyRelative("isExpanded").boolValue = true;
            };
        }

        private float GetPropertyHeight(SerializedProperty layerProp)
        {
            var isExpanded = layerProp.FindPropertyRelative("isExpanded");
            float height = EditorGUIUtility.singleLineHeight; // Header
            if (isExpanded.boolValue)
            {
                // 4 properties, each with line height and spacing
                height += 4 * (EditorGUIUtility.singleLineHeight + UIConstants.FieldVerticalSpacing);
            }
            return height + UIConstants.CardVerticalPadding * 2; // Top and bottom padding
        }

        private void DrawLayerCard(Rect rect, SerializedProperty layerProp, int index)
        {
            var layer = _recipe.blendLayers[index];

            // Adjust rect for padding
            rect.y += UIConstants.CardVerticalPadding;
            rect.height = EditorGUIUtility.singleLineHeight;

            // --- Header ---
            var headerRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);

            // Foldout
            var foldoutRect = headerRect;
            foldoutRect.width = UIConstants.FoldoutWidth;
            layer.isExpanded = EditorGUI.Foldout(foldoutRect, layer.isExpanded, GUIContent.none, false);

            // Enabled Toggle
            var enabledRect = headerRect;
            enabledRect.x = headerRect.xMax - UIConstants.EnabledToggleWidth;
            enabledRect.width = UIConstants.EnabledToggleWidth;
            var enabledProp = layerProp.FindPropertyRelative("enabled");
            enabledProp.boolValue = EditorGUI.Toggle(enabledRect, enabledProp.boolValue);

            // Name Field
            var nameRect = headerRect;
            nameRect.x += UIConstants.FoldoutWidth;
            nameRect.width -= (UIConstants.FoldoutWidth + UIConstants.EnabledToggleWidth);
            var nameProp = layerProp.FindPropertyRelative("name");
            nameProp.stringValue = EditorGUI.TextField(nameRect, nameProp.stringValue);

            if (layer.isExpanded)
            {
                var contentRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                contentRect.y += EditorGUIUtility.singleLineHeight + UIConstants.FieldVerticalSpacing;
                contentRect.x += UIConstants.FoldoutWidth; // Indent content
                contentRect.width -= UIConstants.FoldoutWidth;

                // --- Blend Mode ---
                var blendModeProp = layerProp.FindPropertyRelative("blendMode");
                EditorGUI.PropertyField(contentRect, blendModeProp, new GUIContent("图层混合"));
                contentRect.y += EditorGUIUtility.singleLineHeight + UIConstants.FieldVerticalSpacing;

                // --- Opacity ---
                var opacityProp = layerProp.FindPropertyRelative("opacity");
                EditorGUI.PropertyField(contentRect, opacityProp, new GUIContent("混合强度"));
                contentRect.y += EditorGUIUtility.singleLineHeight + UIConstants.FieldVerticalSpacing;

                // --- Terrain Layer ---
                var terrainLayerProp = layerProp.FindPropertyRelative("terrainLayer");
                DrawObjectFieldWithPing(contentRect, terrainLayerProp, "地形层");
                contentRect.y += EditorGUIUtility.singleLineHeight + UIConstants.FieldVerticalSpacing;

                // --- Mask ---
                var maskProp = layerProp.FindPropertyRelative("mask");
                DrawObjectFieldWithPing(contentRect, maskProp, "遮罩");
            }
        }

        private void DrawObjectFieldWithPing(Rect position, SerializedProperty property, string label)
        {
            var labelRect = position;
            labelRect.width = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(labelRect, label);

            var fieldRect = position;
            fieldRect.x += EditorGUIUtility.labelWidth;
            fieldRect.width -= (EditorGUIUtility.labelWidth + UIConstants.PingButtonWidth);
            EditorGUI.PropertyField(fieldRect, property, GUIContent.none);

            var buttonRect = position;
            buttonRect.x = fieldRect.xMax;
            buttonRect.width = UIConstants.PingButtonWidth;
            using (new EditorGUI.DisabledScope(property.objectReferenceValue == null))
            {
                var icon = EditorGUIUtility.IconContent("d_Target");
                if (GUI.Button(buttonRect, icon, EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(property.objectReferenceValue);
                }
            }
        }

        private void DrawPreview(StylizedRoadRecipe recipe)
        {
            // 预览区标题
            GUILayout.Label(RecipeEditorStyles.PreviewHeader);

            // 更新预览纹理
            UpdatePreviewTexture();

            // 绘制预览控制工具栏
            DrawPreviewToolbar();

            // 绘制预览图
            Rect previewRect = GUILayoutUtility.GetRect(PREVIEW_WIDTH, PREVIEW_HEIGHT, GUILayout.ExpandWidth(true));
            DrawPreviewTexture(previewRect);

            // 绘制预览帮助文本
            string helpText = _selectedPreviewMode == 0 ? RecipeEditorStyles.PreviewHelpBoxChannels : RecipeEditorStyles.PreviewHelpBoxCombined;
            EditorGUILayout.HelpBox(helpText, MessageType.None);
        }
        private void DrawPreviewToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 预览模式切换 (Channels / Combined)
                _selectedPreviewMode = GUILayout.Toolbar(_selectedPreviewMode, RecipeEditorStyles.PreviewModeIcons, EditorStyles.miniButton, GUILayout.Width(80));

                GUILayout.Space(10);

                // 通道切换 (仅在 Channels 模式下显示)
                if (_selectedPreviewMode == 0)
                {
                    _selectedChannel = GUILayout.Toolbar(_selectedChannel, RecipeEditorStyles.ChannelIcons, EditorStyles.miniButton);
                }
            }
        }

        private void UpdatePreviewTexture()
        {
            if (_previewTexture == null)
            {
                _previewTexture = new Texture2D(PREVIEW_WIDTH, PREVIEW_HEIGHT, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
            }

            var recipe = target as StylizedRoadRecipe;
            int currentHash = ComputeRecipeHash(recipe) * 31 + _selectedPreviewMode;

            if (currentHash != _lastRecipeHash || EditorApplication.timeSinceStartup - _lastUpdateTime > MIN_UPDATE_INTERVAL)
            {
                if (_selectedPreviewMode == 0) // Channels
                    GenerateChannelsPreview(recipe, _previewTexture);
                else // Combined
                    GenerateCombinedPreview(recipe, _previewTexture);

                _lastRecipeHash = currentHash;
                _lastUpdateTime = EditorApplication.timeSinceStartup;
            }
        }
        private void GenerateChannelsPreview(StylizedRoadRecipe recipe, Texture2D target)
        {
            if (recipe == null || target == null) return;
            var colors = new Color[target.width * target.height];

            int layerCount = Mathf.Min(4, recipe.blendLayers != null ? recipe.blendLayers.Count : 0);
            if (layerCount == 0)
            {
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0.3f, 0.3f, 0.3f, 1f);
                target.SetPixels(colors); target.Apply(false, false); return;
            }

            for (int x = 0; x < target.width; x++)
            {
                float t = x / (float)(target.width - 1);  // 0..1
                float pos = Mathf.Lerp(-1f, 1f, t);        // -1..1，0 为中心

                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int i = 0; i < layerCount; i++)
                {
                    var layer = recipe.blendLayers[i];
                    if (layer == null || !layer.enabled) continue;
                    var brush = layer.mask;          // 新笔刷资产
                    var mask = layer.blendMask;      // 旧字段回退
                    float v = 1f;
                    if (brush != null)
                    {
                        v = Mathf.Clamp01(brush.Evaluate(pos));
                    }
                    else if (mask != null)
                    {
                        switch (mask.maskType)
                        {
                            case BlendMaskType.PositionalGradient:
                                v = mask.gradient != null ? Mathf.Clamp01(mask.gradient.Evaluate(pos)) : 1f;
                                break;
                            case BlendMaskType.ProceduralNoise:
                                float scale = Mathf.Max(0.0001f, mask.noiseScale);
                                v = Mathf.Clamp01(Mathf.PerlinNoise(x / scale, 0.5f) * mask.noiseStrength);
                                break;
                            case BlendMaskType.CustomTexture:
                                if (mask.customTexture != null)
                                {
                                    var tex2D = mask.customTexture as Texture2D;
                                    if (tex2D != null && tex2D.isReadable)
                                    {
                                        var texX = Mathf.Clamp(Mathf.RoundToInt(t * (tex2D.width - 1)), 0, tex2D.width - 1);
                                        v = tex2D.GetPixel(texX, tex2D.height / 2).a;
                                    }
                                    else
                                    {
                                        v = 1f; // 不可读时回退为满影响，避免异常
                                    }
                                }
                                else v = 0f;
                                break;
                        }
                    }
                    v *= Mathf.Clamp01(layer.opacity);
                    switch (i)
                    {
                        case 0: r = Blend(r, v, layer.blendMode); break;
                        case 1: g = Blend(g, v, layer.blendMode); break;
                        case 2: b = Blend(b, v, layer.blendMode); break;
                        case 3: a = Blend(a, v, layer.blendMode); break;
                    }
                }

                float sum = r + g + b + a;
                if (sum > 1e-6f) { float inv = 1f / sum; r *= inv; g *= inv; b *= inv; a *= inv; }
                var c = new Color(r, g, b, a);
                for (int y = 0; y < target.height; y++) colors[y * target.width + x] = c;
            }

            target.SetPixels(colors);
            target.Apply(false, false);
        }

        private void GenerateCombinedPreview(StylizedRoadRecipe recipe, Texture2D target)
        {
            if (recipe == null || target == null) return;
            var colors = new Color[target.width * target.height];

            // 背景初始化为黑色
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.black;

            // 横向坐标映射到 -1..1；每列按 Mask 灰度遮罩 Layer 的地形贴图颜色，再跨层叠加
            for (int x = 0; x < target.width; x++)
            {
                float t = (float)x / (target.width - 1);
                float pos = Mathf.Lerp(-1f, 1f, t);

                Color columnColor = Color.black;
                foreach (var layer in recipe.blendLayers)
                {
                    if (layer == null || !layer.enabled) continue;
                    var brush = layer.mask;          // 新笔刷资产
                    var mask = layer.blendMask;      // 旧字段回退
                    float v = 1f;
                    if (brush != null)
                    {
                        v = Mathf.Clamp01(brush.Evaluate(pos));
                    }
                    else if (mask != null)
                    {
                        switch (mask.maskType)
                        {
                            case BlendMaskType.PositionalGradient:
                                v = mask.gradient != null ? Mathf.Clamp01(mask.gradient.Evaluate(pos)) : 1f;
                                break;
                            case BlendMaskType.ProceduralNoise:
                                float scale = Mathf.Max(0.0001f, mask.noiseScale);
                                v = Mathf.Clamp01(Mathf.PerlinNoise(x / scale, 0.5f) * mask.noiseStrength);
                                break;
                            case BlendMaskType.CustomTexture:
                                if (mask.customTexture != null)
                                {
                                    var tex2D = mask.customTexture as Texture2D;
                                    if (tex2D != null && tex2D.isReadable)
                                    {
                                        var u = t;
                                        var texX = Mathf.Clamp(Mathf.RoundToInt(u * (tex2D.width - 1)), 0, tex2D.width - 1);
                                        v = tex2D.GetPixel(texX, tex2D.height / 2).a; // 取 Alpha 为遮罩
                                    }
                                    else
                                    {
                                        v = 1f; // 不可读时回退为满影响，避免异常
                                    }
                                }
                                else v = 0f;
                                break;
                        }
                    }

                    // 采样该 Layer 的地形贴图颜色（简化为沿横向采样一行）
                    var layerColor = SampleLayerColor(layer.terrainLayer, t);

                    // 应用不透明度与 Mask 强度；v 作为当前层对列颜色的影响权重
                    float influence = Mathf.Clamp01(layer.opacity) * Mathf.Clamp01(v);

                    // 先按 BlendMode 计算本层与已有列颜色的组合结果，再按 influence 线性插值应用
                    var blended = BlendColor(columnColor, layerColor, layer.blendMode);
                    columnColor = Color.Lerp(columnColor, blended, influence);
                }

                for (int y = 0; y < target.height; y++) colors[y * target.width + x] = columnColor;
            }

            target.SetPixels(colors);
            target.Apply(false, false);
        }

        private void DrawPreviewTexture(Rect rect)
        {
            if (_previewTexture == null) return;

            // 使用自定义着色器来显示单通道
            if (_selectedPreviewMode == 0 && _selectedChannel > 0)
            {
                var shader = Shader.Find("Hidden/MrPath/ChannelView");
                if (shader != null)
                {
                    var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    mat.SetTexture("_MainTex", _previewTexture);
                    mat.SetFloat("_Channel", (float)_selectedChannel); // 1=R, 2=G, 3=B, 4=A
                    EditorGUI.DrawPreviewTexture(rect, _previewTexture, mat, ScaleMode.StretchToFill);
                    DestroyImmediate(mat);
                }
            }
            else // RGB 或 Combined 模式直接绘制
            {
                GUI.DrawTexture(rect, _previewTexture, ScaleMode.StretchToFill, false);
            }

            // 绘制中心线
            float centerX = rect.x + rect.width * 0.5f;
            EditorGUI.DrawRect(new Rect(centerX - 0.5f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.5f));
        }

        // 将当前配方的变更传播给场景中的 PathCreator，以驱动预览刷新
        private void PropagateRecipeChange()
        {
            var recipe = target as StylizedRoadRecipe;
            if (recipe == null) return;

            var creators = Object.FindObjectsOfType<PathCreator>();
            foreach (var creator in creators)
            {
                var profile = creator?.profile;
                if (profile != null && profile.roadRecipe == recipe)
                {
                    creator.NotifyProfileModified();
                }
            }

            // 刷新所有场景视图，确保立即看到更新
            SceneView.RepaintAll();
        }




        private Color SampleLayerColor(TerrainLayer layer, float u)
        {
            if (layer == null || layer.diffuseTexture == null) return Color.white;
            var tex = layer.diffuseTexture as Texture2D;
            if (tex != null && tex.isReadable)
            {
                return tex.GetPixelBilinear(u, 0.5f);
            }
            return Color.white;
        }




        private float Blend(float baseValue, float layerValue, BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Multiply: return baseValue * layerValue;
                case BlendMode.Add: return Mathf.Clamp01(baseValue + layerValue);
                case BlendMode.Additive: return Mathf.Clamp01(baseValue + layerValue);
                case BlendMode.Lerp: return Mathf.Lerp(baseValue, layerValue, Mathf.Clamp01(layerValue));
                case BlendMode.Overlay:
                    return baseValue < 0.5f
                        ? 2f * baseValue * layerValue
                        : 1f - 2f * (1f - baseValue) * (1f - layerValue);
                case BlendMode.Screen: return 1f - (1f - baseValue) * (1f - layerValue);
                default: return layerValue; // Normal：上方覆盖当前列
            }
        }

        private Color BlendColor(Color baseColor, Color layerColor, BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Multiply:
                    return new Color(baseColor.r * layerColor.r, baseColor.g * layerColor.g, baseColor.b * layerColor.b, 1f);
                case BlendMode.Add:
                case BlendMode.Additive:
                    return new Color(
                        Mathf.Clamp01(baseColor.r + layerColor.r),
                        Mathf.Clamp01(baseColor.g + layerColor.g),
                        Mathf.Clamp01(baseColor.b + layerColor.b),
                        1f);
                case BlendMode.Lerp:
                    return Color.Lerp(baseColor, layerColor, 0.5f);
                case BlendMode.Overlay:
                    float Overlay(float a, float b) => a < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
                    return new Color(
                        Overlay(baseColor.r, layerColor.r),
                        Overlay(baseColor.g, layerColor.g),
                        Overlay(baseColor.b, layerColor.b),
                        1f);
                case BlendMode.Screen:
                    return new Color(
                        1f - (1f - baseColor.r) * (1f - layerColor.r),
                        1f - (1f - baseColor.g) * (1f - layerColor.g),
                        1f - (1f - baseColor.b) * (1f - layerColor.b),
                        1f);
                default:
                    return layerColor; // Normal：直接使用上方图层颜色
            }
        }





        private void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string parent = Path.GetDirectoryName(folderPath);
            string newFolderName = Path.GetFileName(folderPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }
            AssetDatabase.CreateFolder(parent, newFolderName);
        }





        private int ComputeRecipeHash(StylizedRoadRecipe recipe)
        {
            unchecked
            {
                int h = 17;
                if (recipe == null || recipe.blendLayers == null) return h;

                h = h * 23 + recipe.blendLayers.Count;
                foreach (var layer in recipe.blendLayers)
                {
                    if (layer == null) continue;
                    h = h * 23 + (int)layer.blendMode;
                    h = h * 23 + layer.opacity.GetHashCode();
                    h = h * 23 + layer.enabled.GetHashCode();
                    if (layer.terrainLayer != null) h = h * 23 + layer.terrainLayer.GetInstanceID();

                    var brush = layer.mask;
                    if (brush == null) continue;

                    h = h * 23 + brush.GetInstanceID();

                    // [核心增强] 识别所有噪声类型
                    switch (brush)
                    {
                        case GradientMask gm:
                            if (gm.gradient != null) h = h * 23 + gm.gradient.GetHashCode();
                            break;
                        case NoiseMask nm:
                            h = h * 23 + nm.scale.GetHashCode(); h = h * 23 + nm.strength.GetHashCode();
                            break;
                        // case FractalNoiseMask fnm:
                        //     h = h * 23 + fnm.scale.GetHashCode(); h = h * 23 + fnm.strength.GetHashCode();
                        //     h = h * 23 + fnm.octaves.GetHashCode(); h = h * 23 + fnm.lacunarity.GetHashCode(); h = h * 23 + fnm.persistence.GetHashCode();
                        //     break;
                        // case RidgeNoiseMask rnm:
                        //     h = h * 23 + rnm.scale.GetHashCode(); h = h * 23 + rnm.strength.GetHashCode();
                        //     h = h * 23 + rnm.octaves.GetHashCode(); h = h * 23 + rnm.ridgeSharpness.GetHashCode();
                        //     break;
                        // case VoronoiNoiseMask vnm:
                        //     h = h * 23 + vnm.scale.GetHashCode(); h = h * 23 + vnm.strength.GetHashCode();
                        //     h = h * 23 + vnm.type.GetHashCode(); h = h * 23 + vnm.randomness.GetHashCode();
                        //     break;
                        case BrushStrokeNoiseMask bsnm:
                            h = h * 23 + bsnm.scale.GetHashCode(); h = h * 23 + bsnm.strength.GetHashCode();
                            h = h * 23 + bsnm.strengthVariation.GetHashCode(); h = h * 23 + bsnm.strokeWidth.GetHashCode();
                            h = h * 23 + bsnm.widthVariation.GetHashCode(); h = h * 23 + bsnm.jitter.GetHashCode();
                            break;

                    }
                }
                return h;
            }
        }
    }
}