#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using __temp.MrPathV2._2.Editor.Operations;
using __temp.MrPathV2._2.Editor.Terrain;
using __temp.MrPathV2._2.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Object = UnityEngine.Object;

namespace __temp.MrPathV2._2.Editor.Settings
{
    /// <summary>
    /// 为 MrPath 工具提供一个基于 UIToolkit 的现代设置界面。
    /// </summary>
    internal class MrPathSettingsProvider : SettingsProvider
    {
        private SerializedObject _settings;
        private Label _titleLabel;
        private string _originalTitleText = "MrPath Settings";

        private MrPathSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settings = new SerializedObject(MrPathProjectSettings.GetOrCreateSettings());
            rootElement.Clear();

            var content = UIResourceLoader.LoadAndCloneByName(nameof(MrPathSettingsProvider));
            rootElement.Add(content);

            content.Bind(_settings);

            _titleLabel = content.Q<Label>("settings-title");
            if (_titleLabel != null)
            {
                // 确保原始文本干净（可选）
                _originalTitleText = _titleLabel.text.Trim();
                if (!_originalTitleText.EndsWith("Settings"))
                {
                    _originalTitleText = "MrPath Settings";
                    _titleLabel.text = _originalTitleText;
                }
            }

            SetupSettingsLink(content, "creationDefaults", "创建默认值", typeof(MrPathCreationDefaults), _titleLabel);
            SetupSettingsLink(content, "appearanceDefaults", "外观默认值", typeof(MrPathAppearanceDefaults), _titleLabel);
            SetupSettingsLink(content, "terrainOperations", "地形操作", typeof(MrPathTerrainOperations), _titleLabel);
            SetupSettingsLink(content, "advancedSettings", "高级设置", typeof(MrPathAdvancedSettings), _titleLabel);

            var scanButton = content.Q<Button>("scan-assets-button");
            scanButton?.RegisterCallback<ClickEvent>(_ => ScanAndFillAllAssets());
        }

        private void SetupSettingsLink(VisualElement root, string propertyName, string mLabel, Type assetType,
            Label titleLabel)
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

            propertyField.RegisterValueChangeCallback(evt =>
                UpdateButtons(evt.changedProperty.objectReferenceValue));

            sayHi.clicked += () =>
            {
                if (titleLabel == null) return;

                string[] greetings = {
                   
                    "Good day, sir! ",
                    "Hello, Mr. ",
                   
                };

                string greeting = greetings[UnityEngine.Random.Range(0, greetings.Length)];
                string newFullText = $"{_originalTitleText}: {greeting}";

                _titleLabel.text = newFullText;


                _titleLabel.schedule.Execute(() =>
                {
                    if (_titleLabel == null) return;
                    
                    _titleLabel.text = _originalTitleText;
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

                // 创建新资产
                var path = GetSettingsPath();
                if (string.IsNullOrEmpty(path)) return;

                var typeName = assetType.Name
                    .Replace("MrPath", "")
                    .Replace("Settings", "")
                    .Replace("Defaults", "");
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
            return;

            void UpdateButtons(Object obj)
            {
                sayHi.SetEnabled(obj);
                createButton.text = obj ? "Ping" : "Create";
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateMrPathSettingsProvider()
        {
            return new MrPathSettingsProvider("Project/MrPath", SettingsScope.Project)
            {
                keywords = new HashSet<string> { "MrPath", "Road", "Path", "Stylized" }
            };
        }

        // ----------------------------------------------------------------------
        // 扫描与工具方法
        // ----------------------------------------------------------------------

        private static string GetSettingsPath() => MrPathProjectSettings.GetSettingsRootFolder();

        private void ScanAndFillAllAssets()
        {
            if (_settings == null)
            {
                Debug.LogError("_settings 未初始化！");
                _settings = new SerializedObject(MrPathProjectSettings.GetOrCreateSettings());
            }

            _settings.Update();

            // 扫描 Road Recipes
            var recipes = FindAssetsByType<StylizedRoadRecipe>("t:StylizedRoadRecipe");
            UpdateSerializedArray(_settings.FindProperty("roadRecipes"), recipes);
            Debug.Log($"MrPath: 已填充 {recipes.Count} 个道路配方。");

            // 扫描 Path Profiles
            var profiles = FindAssetsByType<PathProfile>("t:PathProfile");
            UpdateSerializedArray(_settings.FindProperty("profiles"), profiles);
            Debug.Log($"MrPath: 已填充 {profiles.Count} 个路径配置文件。");

            // 扫描 Terrain Operations
            var terrainOpsProp = _settings.FindProperty("terrainOperations");
            if (terrainOpsProp.objectReferenceValue is ScriptableObject terrainOpsAsset)
            {
                var opsSo = new SerializedObject(terrainOpsAsset);
                var opsArray = opsSo.FindProperty("operations");
                var ops = FindAssetsByType<PathTerrainOperation>($"t:{nameof(PathTerrainOperation)}")
                    .OrderBy(op => op.order)
                    .ToList();

                UpdateSerializedArray(opsArray, ops);
                opsSo.ApplyModifiedProperties();
                Debug.Log($"MrPath: 已填充 {ops.Count} 个地形操作。");
            }
            else
            {
                Debug.LogWarning("地形操作配置资产丢失，请先创建。");
            }

            _settings.ApplyModifiedProperties();
        }

        private static List<T> FindAssetsByType<T>(string filter) where T : ScriptableObject
        {
            return AssetDatabase.FindAssets(filter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
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
    }
}
#endif