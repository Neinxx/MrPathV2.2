using System.IO;
using System.Linq;
using System.Collections.Generic;
using __temp.MrPathV2._2.Editor.Operations;
using __temp.MrPathV2._2.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;


namespace __temp.MrPathV2._2.Editor.Settings
{
    /// <summary>
    /// 为 MrPath 工具提供一个基于 UIToolkit 的现代设置界面。
    /// </summary>
    internal class MrPathSettingsProvider : SettingsProvider
    {
        private SerializedObject _settings;

        private MrPathSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
        }

        // OnGUI 已被移除

        /// <summary>
        /// 当面板激活时，加载 UXML，绑定数据，并设置回调。
        /// </summary>
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            // 1. 获取或创建设置资产
            _settings = new SerializedObject(MrPathProjectSettings.GetOrCreateSettings());

            // 2. 加载 UXML 视觉树
            // 最佳实践：自动查找与此脚本同目录的 UXML 文件
            var scriptPath = GetScriptPath(); // 使用新的辅助方法
            if (string.IsNullOrEmpty(scriptPath))
            {
                Debug.LogError("MrPathSettings: 无法定位 MrPathSettingsProvider.cs 脚本文件。UXML/USS 将无法加载。");
                return;
            }

            var scriptFolder = Path.GetDirectoryName(scriptPath);

            if (scriptFolder != null)
            {
                var uxmlPath = Path.Combine(scriptFolder, "MrPathSettingsProvider.uxml").Replace("\\", "/");
                var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                if (visualTree == null)
                {
                    Debug.LogError(
                        $"MrPathSettings: 找不到 UXML 文件，请确保 MrPathSettingsProvider.uxml 在同一目录下。路径: {uxmlPath}");
                    return;
                }

                visualTree.CloneTree(rootElement);
            }

            // 3. 加载 USS 样式表
            if (scriptFolder != null)
            {
                var ussPath = Path.Combine(scriptFolder, "MrPathSettingsProvider.uss").Replace("\\", "/");
                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
                if (styleSheet != null)
                {
                    rootElement.styleSheets.Add(styleSheet);
                }
            }

            // 4. 绑定 SerializedObject
            // UXML 中的 binding-path 会自动链接到 _settings 中的属性
            rootElement.Bind(_settings);

            // 5. 绑定 UXML 中无法处理的自定义逻辑 (子设置链接 和 "扫描" 按钮)
            SetupSettingsLink(rootElement, "creationDefaults", "创建默认值", typeof(MrPathCreationDefaults));
            SetupSettingsLink(rootElement, "appearanceDefaults", "外观默认值", typeof(MrPathAppearanceDefaults));
            SetupSettingsLink(rootElement, "terrainOperations", "地形操作", typeof(MrPathTerrainOperations));
            SetupSettingsLink(rootElement, "advancedSettings", "高级设置", typeof(MrPathAdvancedSettings));

            // 6. 绑定 "扫描" 按钮
            var scanButton = rootElement.Q<Button>("scan-assets-button");
            if (scanButton != null)
            {
                scanButton.clicked += ScanAndFillAllAssets;
            }
        }

        private static string GetScriptPath()
        {
            // 1. 按脚本名称查找
            var guids = AssetDatabase.FindAssets($"t:MonoScript {nameof(MrPathSettingsProvider)}");
            if (guids.Length == 0)
            {
                Debug.LogWarning($"Could not find script file for {nameof(MrPathSettingsProvider)}");
                return null;
            }

            // 2. 遍历所有同名脚本，确保找到正确的类
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script && script.GetClass() == typeof(MrPathSettingsProvider))
                {
                    return path; // 找到了！
                }
            }

            // 3. 备用方案：如果类匹配失败（可能在编译中），但只有一个结果，则使用它
            if (guids.Length == 1)
            {
                Debug.LogWarning(
                    $"Found script named {nameof(MrPathSettingsProvider)} but its class type didn't match (maybe recompiling?). Using it as fallback.");
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }

            Debug.LogError(
                $"Found multiple scripts named {nameof(MrPathSettingsProvider)}. Cannot determine correct one.");
            return null;
        }

        /// <summary>
        /// 辅助方法，为 UXML 中的设置行（PropertyField + Buttons）绑定动态逻辑。
        /// </summary>
        private void SetupSettingsLink(VisualElement root, string propertyName, string label, System.Type assetType)
        {
            var prop = _settings.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"MrPathSettings: 找不到属性 '{propertyName}'");
                return;
            }

            // 1. 查找 UXML 元素
            // UXML 中的 PropertyField 应该有 name="{propertyName}"
            var propertyField = root.Q<PropertyField>(propertyName);
            if (propertyField != null)
            {
                propertyField.label = label;
            }

            // 按钮的 name 应该在 UXML 中定义为 "{propertyName}-ping" 和 "{propertyName}-create"
            var pingButton = root.Q<Button>($"{propertyName}-ping");
            var createButton = root.Q<Button>($"{propertyName}-create");

            if (propertyField == null || pingButton == null || createButton == null)
            {
                Debug.LogWarning($"MrPathSettings: 找不到 '{propertyName}' 对应的 UXML 元素。请检查 UXML 文件中的 name 属性。");
                return;
            }

            // 2. 封装更新按钮状态的逻辑
            System.Action<Object> updateButtons = (asset) =>
            {
                pingButton.SetEnabled(asset);
                createButton.text = !asset ? "创建" : "Ping";
            };

            // 3. 初始状态
            updateButtons(prop.objectReferenceValue);

            // 4. 注册值变化回调
            // 当用户在 ObjectField 中拖拽新资产时，更新按钮
            propertyField.RegisterValueChangeCallback(evt =>
            {
                // 注意：回调中用 evt.changedProperty.objectReferenceValue 获取新值
                updateButtons(evt.changedProperty.objectReferenceValue);
            });

            // 5. 注册按钮点击事件
            pingButton.clicked += () =>
            {
                if (prop.objectReferenceValue)
                {
                    EditorGUIUtility.PingObject(prop.objectReferenceValue);
                }
            };

            createButton.clicked += () =>
            {
                var asset = prop.objectReferenceValue;
                if (!asset)
                {
                    // 创建逻辑
                    var mSettingsPath = GetSettingsPath();
                    if (string.IsNullOrEmpty(mSettingsPath)) return;

                    var subAssetName = $"MrPath_{assetType.Name.Replace("MrPath", "").Replace("Settings", "")}";
                    var path = Path.Combine(mSettingsPath, $"{subAssetName}.asset").Replace("\\", "/");
                    var newAsset = ScriptableObject.CreateInstance(assetType);
                    AssetDatabase.CreateAsset(newAsset, path);
                    AssetDatabase.SaveAssets();

                    prop.objectReferenceValue = newAsset;
                    _settings.ApplyModifiedProperties(); // 关键：应用更改

                    // 手动触发 UI 更新（虽然 ApplyModifiedProperties 理论上会触发，但显式调用更安全）
                    updateButtons(newAsset);

                    Selection.activeObject = newAsset;
                    EditorGUIUtility.PingObject(newAsset);
                }
                else
                {
                    // 定位逻辑
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            };
        }

        // 注册设置提供器到 Project Settings 窗口
        [SettingsProvider]
        public static SettingsProvider CreateMrPathSettingsProvider()
        {
            var provider = new MrPathSettingsProvider("Project/MrPath", SettingsScope.Project);

            // 自动关联关键词，以便在 Project Settings 搜索框中搜到
            provider.keywords = new HashSet<string>(new[] { "MrPath", "Road", "Path", "Stylized" });

            return provider;
        }

        // ----------------------------------------------------------------------
        // 以下是纯逻辑方法，无需修改
        // ----------------------------------------------------------------------

        private static string GetSettingsPath()
        {
            return MrPathProjectSettings.GetSettingsRootFolder();
        }

        private void ScanAndFillAllAssets()
        {
            if (_settings == null)
            {
                Debug.LogError("_settings 对象未初始化！");
                _settings = new SerializedObject(MrPathProjectSettings.GetOrCreateSettings());
            }

            _settings.Update();

            // 扫描并填充 Road Recipes
            var roadRecipesProp = _settings.FindProperty("roadRecipes");
            var foundRecipes = FindAssetsByType<StylizedRoadRecipe>("t:StylizedRoadRecipe");
            UpdateSerializedArray(roadRecipesProp, foundRecipes);
            Debug.Log($"MrPath: 扫描完成，已找到并填充 {foundRecipes.Count} 个道路配方。");

            // 扫描并填充 Path Profiles
            var profilesProp = _settings.FindProperty("profiles");
            var foundProfiles = FindAssetsByType<PathProfile>("t:PathProfile");
            UpdateSerializedArray(profilesProp, foundProfiles);
            Debug.Log($"MrPath: 扫描完成，已找到并填充 {foundProfiles.Count} 个路径配置文件。");

            // 扫描并填充 Terrain Operations
            var terrainOpsProp = _settings.FindProperty("terrainOperations");
            if (terrainOpsProp.objectReferenceValue != null)
            {
                var opsSo = new SerializedObject(terrainOpsProp.objectReferenceValue);
                var opsArrayProp = opsSo.FindProperty("operations");
                var foundOps = FindAssetsByType<PathTerrainOperation>($"t:{nameof(PathTerrainOperation)}");
                UpdateSerializedArray(opsArrayProp, foundOps.OrderBy(op => op.order).ToList());
                opsSo.ApplyModifiedProperties();
                Debug.Log($"MrPath: 扫描完成，已找到并填充 {foundOps.Count} 个地形操作。");
            }
            else
            {
                Debug.LogWarning("地形操作配置资产丢失，请先创建。");
            }

            _settings.ApplyModifiedProperties();

            // 强制 UI 重新绑定以显示新数据
            // OnActivate 不会重新运行，但我们可以手动触发
            // ... (注：绑定后，ApplyModifiedProperties 应该会自动更新 UI)
        }

        private static List<T> FindAssetsByType<T>(string filter) where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets(filter);
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset)
                .ToList();
        }

        private static void UpdateSerializedArray<T>(SerializedProperty arrayProp, List<T> items) where T : Object
        {
            arrayProp.ClearArray();
            for (var i = 0; i < items.Count; ++i)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }
    }
}