#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MrPathV2.Editor;
using MrPathV2.Editor.Operations;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace MrPathV2.Editor.Settings
{
    class MrPathSettingsProvider : SettingsProvider
    {
        private string _originalTitleText = "MrPath Settings";
        private SerializedObject _settings;
        private Label _titleLabel;

        private MrPathSettingsProvider(string path, SettingsScope scopes) : base(path, scopes) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            // 自动创建主资产（仅主资产），避免页面空白
            var settings = MrPathProjectSettings.GetOrCreateSettings();
            _settings = new SerializedObject(settings);
            rootElement.Clear();

            var content = UIResourceLoader.LoadAndCloneByName(nameof(MrPathSettingsProvider));
            rootElement.Add(content);
            content.Bind(_settings);

            _titleLabel = content.Q<Label>("settings-title");
            if (_titleLabel != null)
            {
                _originalTitleText = _titleLabel.text.Trim();
                if (!_originalTitleText.EndsWith("Settings"))
                {
                    _originalTitleText = "MrPath Settings";
                    _titleLabel.text = _originalTitleText;
                }
            }

            // 默认配置链接
            SetupSettingsLink(content, "creationDefaults", "创建默认值", typeof(MrPathCreationDefaults), _titleLabel);
            SetupSettingsLink(content, "appearanceDefaults", "外观默认值", typeof(MrPathAppearanceDefaults), _titleLabel);
            // 新增：Stylized Road Recipe
            SetupSettingsLink(content, "stylizedRoadRecipe", "Stylized Road Recipe", typeof(StylizedRoadRecipe), _titleLabel);
            SetupSettingsLink(content, "terrainOperations", "地形操作", typeof(MrPathTerrainOperations), _titleLabel);
            SetupSettingsLink(content, "advancedSettings", "高级设置", typeof(MrPathAdvancedSettings), _titleLabel);

            var scanButton = content.Q<Button>("scan-assets-button");
            scanButton?.RegisterCallback<ClickEvent>(_ => ScanAndFillAllAssets());

            // 预览线参数面板（去除GPU调试项）
            SetupPreviewLinePanel(content);
        }

        private void SetupSettingsLink(VisualElement root, string propertyName, string mLabel, Type assetType, Label titleLabel)
        {
            var prop = _settings.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"MrPathSettings: 找不到属性 '{propertyName}'");
                return;
            }

            var propertyField = root.Q<PropertyField>(propertyName);
            var sayHi = root.Q<Button>($"{propertyName}-ping");
            var createButton = root.Q<Button>($"{propertyName}-create");
            if (propertyField == null || sayHi == null || createButton == null)
            {
                Debug.LogWarning($"MrPathSettings: 找不到 '{propertyName}' 对应的 UXML 元素。请检查 name 属性。");
                return;
            }

            propertyField.label = mLabel;
            UpdateButtons(prop.objectReferenceValue);
            propertyField.RegisterValueChangeCallback(evt => UpdateButtons(evt.changedProperty.objectReferenceValue));

            sayHi.clicked += () =>
            {
                if (titleLabel == null) return;
                string[] greetings =
                {
                    "Good day, sir! ", "Hello, Mr. "
                };
                var greeting = greetings[Random.Range(0, greetings.Length)];
                _titleLabel.text = $"{_originalTitleText}: {greeting}";
                _titleLabel.schedule.Execute(() =>
                {
                    if (_titleLabel != null) _titleLabel.text = _originalTitleText;
                }).StartingIn(3000);
            };

            createButton.clicked += () =>
            {
                var asset = prop.objectReferenceValue;
                if (asset)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    return;
                }
                var path = GetSettingsPath();
                if (string.IsNullOrEmpty(path)) return;
                var typeName = assetType.Name.Replace("MrPath", "").Replace("Settings", "").Replace("Defaults", "");
                var assetName = $"MrPath_{typeName}.asset";
                var fullPath = Path.Combine(path, assetName).Replace("\\", "/");
                var newAsset = ScriptableObject.CreateInstance(assetType);
                AssetDatabase.CreateAsset(newAsset, fullPath);
                AssetDatabase.SaveAssets();
                prop.objectReferenceValue = newAsset;
                _settings.ApplyModifiedProperties();
                UpdateButtons(newAsset);
                Selection.activeObject = newAsset;
                EditorGUIUtility.PingObject(newAsset);
            };

            void UpdateButtons(Object obj)
            {
                sayHi.SetEnabled(obj);
                createButton.text = obj ? "Ping" : "Create";
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateMrPathSettingsProvider() => new MrPathSettingsProvider("Project/MrPath", SettingsScope.Project)
        {
            keywords = new HashSet<string>
            {
                "MrPath",
                "Road",
                "Path",
                "Stylized"
            }
        };

        private static string GetSettingsPath() => MrPathProjectSettings.GetSettingsRootFolder();

        private void ScanAndFillAllAssets()
        {
            if (_settings == null)
            {
                Debug.LogError("_settings 未初始化！");
                var existing = MrPathProjectSettings.GetOrCreateSettings();
                _settings = new SerializedObject(existing);
            }

            _settings.Update();

            // 扫描 Road Recipes（仅扫描，不自动创建）
            var recipes = FindAssetsByType<StylizedRoadRecipe>("t:StylizedRoadRecipe");
            UpdateSerializedArray(_settings.FindProperty("roadRecipes"), recipes);
            Debug.Log($"MrPath: 已填充 {recipes.Count} 个道路配方。");

            // 扫描 Path Profiles
            var profiles = FindAssetsByType<PathProfile>("t:PathProfile");
            UpdateSerializedArray(_settings.FindProperty("profiles"), profiles);
            Debug.Log($"MrPath: 已填充 {profiles.Count} 个路径配置文件。");

            // 扫描 Masks
            var masks = FindAssetsByType<BlendMaskBase>("t:BlendMaskBase");
            UpdateSerializedArray(_settings.FindProperty("masks"), masks);
            Debug.Log($"MrPath: 已填充 {masks.Count} 个遮罩资产。");

            // 扫描 Terrain Operations
            var terrainOpsProp = _settings.FindProperty("terrainOperations");
            if (terrainOpsProp.objectReferenceValue is ScriptableObject terrainOpsAsset)
            {
                var opsSo = new SerializedObject(terrainOpsAsset);
                var opsArray = opsSo.FindProperty("operations");
                var ops = FindAssetsByType<PathTerrainOperation>($"t:{nameof(PathTerrainOperation)}").OrderBy(op => op.order).ToList();
                UpdateSerializedArray(opsArray, ops);
                opsSo.ApplyModifiedProperties();
                Debug.Log($"MrPath: 已填充 {ops.Count} 个地形操作。");
            }
            else
            {
                Debug.LogWarning("地形操作配置资产丢失，请先创建。");
            }

            // 完整性检查并提示（包含默认 Stylized Road Recipe）
            ShowIntegrityAndBlessings(recipes, profiles, masks);

            _settings.ApplyModifiedProperties();
        }

        private void ShowIntegrityAndBlessings(List<StylizedRoadRecipe> recipes, List<PathProfile> profiles, List<BlendMaskBase> masks)
        {
            var missing = new List<string>();
            var creation = _settings.FindProperty("creationDefaults").objectReferenceValue;
            var appearance = _settings.FindProperty("appearanceDefaults").objectReferenceValue;
            var terrain = _settings.FindProperty("terrainOperations").objectReferenceValue;
            var advanced = _settings.FindProperty("advancedSettings").objectReferenceValue;
            var defaultRecipe = _settings.FindProperty("stylizedRoadRecipe").objectReferenceValue;

            if (!creation) missing.Add("创建默认值");
            if (!appearance) missing.Add("外观默认值");
            if (!terrain) missing.Add("地形操作");
            if (!advanced) missing.Add("高级设置");
            if (!defaultRecipe) missing.Add("Stylized Road Recipe");
            if (recipes.Count == 0) missing.Add("Road Recipes");
            if (profiles.Count == 0) missing.Add("Path Profiles");
            if (masks.Count == 0) missing.Add("Masks");

            if (_titleLabel == null) return;

            if (missing.Count > 0)
            {
                var warn = $"⚠ Incomplete resources: {string.Join(", ", missing)}";
                _titleLabel.text = warn;
                _titleLabel.style.color = new StyleColor(new Color(0.85f, 0.3f, 0.2f));
                _titleLabel.schedule.Execute(() =>
                {
                    if (_titleLabel == null) return;
                    _titleLabel.text = _originalTitleText;
                    _titleLabel.style.color = new StyleColor(new Color(226f / 255f, 152f / 255f, 61f / 255f));
                }).StartingIn(4000);
            }
            else
            {
                var bless = " (●'◡'●) Resources are complete! ";
                _titleLabel.text = bless;
                _titleLabel.style.color = new StyleColor(new Color(0.25f, 0.65f, 0.35f));
                _titleLabel.schedule.Execute(() =>
                {
                    if (_titleLabel == null) return;
                    _titleLabel.text = _originalTitleText;
                    _titleLabel.style.color = new StyleColor(new Color(226f / 255f, 152f / 255f, 61f / 255f));
                }).StartingIn(3500);
            }
        }

        private static List<T> FindAssetsByType<T>(string filter) where T : ScriptableObject
        {
            return AssetDatabase.FindAssets(filter).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(asset => asset != null).ToList();
        }

        private static void UpdateSerializedArray<T>(SerializedProperty arrayProp, List<T> items) where T : Object
        {
            arrayProp.ClearArray();
            for (var i = 0; i < items.Count; i++)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        // --- 预览线参数面板（CPU-only） ---
        private void SetupPreviewLinePanel(VisualElement content)
        {
            var rootBox = content.Q<Box>("root");
            if (rootBox == null) return;

            var advProp = _settings.FindProperty("advancedSettings");
            var advObj = advProp?.objectReferenceValue as MrPathAdvancedSettings;

            var section = new Foldout
            {
                text = "预览线设置"
            };
            section.value = false; // 默认折叠

            if (advObj != null)
            {
                var advSO = new SerializedObject(advObj);
                section.Add(new PropertyField(advSO.FindProperty("previewAAWidthPixels"), "边缘AA宽度(像素)"));
                section.Add(new PropertyField(advSO.FindProperty("previewCapAAWidthPixels"), "端帽融合宽度(像素)"));
                section.Add(new PropertyField(advSO.FindProperty("previewDefaultDashPixels"), "默认虚线长度(像素)"));
                section.Add(new PropertyField(advSO.FindProperty("previewMaxPixelStep"), "屏幕采样步长(像素)"));
                section.Add(new PropertyField(advSO.FindProperty("previewCapType"), "端帽类型"));
                section.Add(new PropertyField(advSO.FindProperty("previewSeamScale"), "端帽宽度缩放"));
                section.Bind(advSO);
            }
            else
            {
                section.Add(new Label("未设置 '高级设置' 资产。请先在上方创建/关联后使用预览线设置。"));
            }

            rootBox.Add(section);
        }
    }
}
#endif
