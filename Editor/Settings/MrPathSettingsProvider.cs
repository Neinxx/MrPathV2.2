// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathSettingsProvider.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using MrPathV2; // 添加正确的命名空间

namespace MrPathV2
{
    /// <summary>
    /// 为 MrPath 工具提供一个清爽、导航式的项目设置界面。
    /// </summary>
    class MrPathSettingsProvider : SettingsProvider
    {
        private SerializedObject _settings;

        public MrPathSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            // 当面板激活时，获取或创建主设置资产，并为其创建一个 SerializedObject
            _settings = new SerializedObject(MrPathProjectSettings.GetOrCreateSettings());
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.LabelField("MrPath 工具配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("这是 MrPath 工具的核心配置中心。将不同的配置资产拖拽到下方字段，或点击右侧按钮创建/定位。", MessageType.Info);

            EditorGUILayout.Space();
            _settings.Update();

            // 绘制每个子配置的链接
            DrawSettingsLink("creationDefaults", "创建默认值", typeof(MrPathCreationDefaults));
            DrawSettingsLink("appearanceDefaults", "外观默认值", typeof(MrPathAppearanceDefaults));
            DrawSettingsLink("sceneUISettings", "场景 UI", typeof(MrPathSceneUISettings));
            DrawSettingsLink("terrainOperations", "地形操作", typeof(MrPathTerrainOperations));
            DrawSettingsLink("advancedSettings", "高级设置", typeof(MrPathAdvancedSettings));

            // 绘制列表: 道路配方
            SerializedProperty roadRecipesProp = _settings.FindProperty("roadRecipes");
            EditorGUILayout.PropertyField(roadRecipesProp, new GUIContent("道路配方列表"), true);

            _settings.ApplyModifiedProperties();

            EditorGUILayout.Space(20);

            // 添加新的功能按钮
            EditorGUILayout.LabelField("资产管理", EditorStyles.boldLabel);
            if (GUILayout.Button("自动扫描并填充所有资产"))
            {
                ScanAndFillAllAssets();
            }
            if (GUILayout.Button("自动创建缺失噪声资产"))
            {
                CreateMissingNoiseAssets();
            }
        }

        /// <summary>
        /// 绘制一个指向子配置资产的带操作按钮的字段。
        /// </summary>
        private string GetSettingsPath()
        {
            // 动态查找 MrPathV2.2 文件夹的路径
            string mrPathFolder = AssetDatabase.FindAssets("MrPathV2.2").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("MrPathV2.2"));

            if (string.IsNullOrEmpty(mrPathFolder))
            {
                Debug.LogError("未找到 MrPathV2.2 文件夹！");
                return null;
            }

            // 拼接 Settings 文件夹路径
            return Path.Combine(mrPathFolder, "Settings").Replace("\\", "/");
        }

        private void DrawSettingsLink(string propertyName, string label, System.Type assetType)
        {
            SerializedProperty prop = _settings.FindProperty(propertyName);
            EditorGUILayout.BeginHorizontal();

            // 绘制对象字段
            EditorGUILayout.PropertyField(prop, new GUIContent(label));

            var asset = prop.objectReferenceValue;

            // Ping 按钮
            using (new EditorGUI.DisabledScope(asset == null))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }

            // 创建/定位 按钮
            if (GUILayout.Button(asset == null ? "创建" : "定位", GUILayout.Width(60)))
            {
                if (asset == null)
                {
                    // 如果资产为空，则自动创建并赋值
                    string settingsPath = GetSettingsPath();
                    if (string.IsNullOrEmpty(settingsPath)) return;

                    string subAssetName = $"MrPath_{assetType.Name.Replace("MrPath", "").Replace("Settings", "")}";
                    string path = Path.Combine(settingsPath, $"{subAssetName}.asset").Replace("\\", "/");
                    var newAsset = ScriptableObject.CreateInstance(assetType);
                    AssetDatabase.CreateAsset(newAsset, path);
                    AssetDatabase.SaveAssets();
                    prop.objectReferenceValue = newAsset;
                }
                Selection.activeObject = prop.objectReferenceValue;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ScanAndFillAllAssets()
        {
            if (_settings == null)
            {
                Debug.LogError("_settings 对象未初始化！");
                return;
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
                var opsSO = new SerializedObject(terrainOpsProp.objectReferenceValue);
                var opsArrayProp = opsSO.FindProperty("operations");
                var foundOps = FindAssetsByType<PathTerrainOperation>($"t:{nameof(PathTerrainOperation)}");
                UpdateSerializedArray(opsArrayProp, foundOps.OrderBy(op => op.order).ToList());
                opsSO.ApplyModifiedProperties();
                Debug.Log($"MrPath: 扫描完成，已找到并填充 {foundOps.Count} 个地形操作。");
            }
            else
            {
                Debug.LogWarning("地形操作配置资产丢失，请先创建。");
            }

            _settings.ApplyModifiedProperties();
        }


        // 注册设置提供器到 Project Settings 窗口
        [SettingsProvider]
        public static SettingsProvider CreateMrPathSettingsProvider()
        {
            var provider = new MrPathSettingsProvider("Project/MrPath", SettingsScope.Project);
            return provider;
        }

        /// <summary>
        /// 根据类型查找资产。
        /// </summary>
        private List<T> FindAssetsByType<T>(string filter) where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets(filter);
            return guids
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .ToList();
        }

        /// <summary>
        /// 更新 SerializedProperty 数组。
        /// </summary>
        private void UpdateSerializedArray<T>(SerializedProperty arrayProp, List<T> items) where T : Object
        {
            arrayProp.ClearArray();
            for (int i = 0; i < items.Count; ++i)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }





        private void CreateMissingNoiseAssets()
        {
            var recipesProp = _settings.FindProperty("roadRecipes");
            if (recipesProp == null) return;

            string settingsPath = GetSettingsPath();
            if (string.IsNullOrEmpty(settingsPath)) return;

            string masksFolder = Path.Combine(settingsPath, "BlendMasks").Replace("\\", "/");
            EnsureFolderExists(masksFolder);

            int createdCount = 0;
            for (int i = 0; i < recipesProp.arraySize; ++i)
            {
                var recipeObj = recipesProp.GetArrayElementAtIndex(i).objectReferenceValue as StylizedRoadRecipe;
                if (recipeObj == null) continue;

                SerializedObject recipeSO = new SerializedObject(recipeObj);
                var layersProp = recipeSO.FindProperty("blendLayers");
                for (int l = 0; l < layersProp.arraySize; ++l)
                {
                    var layerProp = layersProp.GetArrayElementAtIndex(l);
                    var maskProp = layerProp.FindPropertyRelative("mask");
                    if (maskProp.objectReferenceValue == null)
                    {
                        // 创建 NoiseMask 资产
                        var newMask = ScriptableObject.CreateInstance<NoiseMask>();
                        string assetPath = Path.Combine(masksFolder, $"NoiseMask_{recipeObj.name}_{l}.asset").Replace("\\", "/");
                        AssetDatabase.CreateAsset(newMask, assetPath);
                        maskProp.objectReferenceValue = newMask;
                        createdCount++;
                    }
                }
                recipeSO.ApplyModifiedProperties();
            }
            if (createdCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"MrPath: 已创建 {createdCount} 个 NoiseMask 资产并分配到配方中。");
            }
            else
            {
                Debug.Log("MrPath: 未发现缺失的噪声资产。");
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
    }
}