using System.Collections.Generic;
using System.IO;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Settings
{
    /// <summary>
    ///     MrPath 工具所有配置资产的根引用和导航中心。
    /// </summary>
    public class MrPathProjectSettings : ScriptableObject
    {
        // 定义了主设置文件的唯一、标准路径（保留作向后兼容的最终兜底）
        private const string KSettingsPath = "Assets/MrPathV2/Settings/MrPath_ProjectSettings.asset";

        // 文件名常量；文件夹通过 GetSettingsRootFolder() 动态决定
        private const string KSettingsFileName = "MrPath_ProjectSettings.asset";

        // --- 子配置资产的引用 ---
        [Tooltip("新路径创建时的默认值配置")] public MrPathCreationDefaults creationDefaults;

        [Tooltip("路径默认外观与预览材质配置")] public MrPathAppearanceDefaults appearanceDefaults;


        [Tooltip("数据驱动的地形操作列表")] public MrPathTerrainOperations terrainOperations;


        [Tooltip("高级开发者设置，如地形绘制后端选择、性能调试选项等")] public MrPathAdvancedSettings advancedSettings;
        // ------------------------------------

        // 新增：默认风格化道路配方
        [Tooltip("默认风格化道路配方")] public StylizedRoadRecipe stylizedRoadRecipe;

        // 发现资产集合
        [Tooltip("路径配置文件集合")] public List<PathProfile> profiles = new List<PathProfile>();
        [Tooltip("道路配方集合")] public List<StylizedRoadRecipe> roadRecipes = new List<StylizedRoadRecipe>();
        [Tooltip("遮罩资产集合")] public List<BlendMaskBase> masks = new List<BlendMaskBase>();

        /// <summary>
        ///     获取或创建主设置资产的静态方法。
        /// </summary>
        internal static MrPathProjectSettings GetOrCreateSettings()
        {
            var settings = LoadExistingSettings();
            if (!settings)
            {
                settings = CreateInstance<MrPathProjectSettings>();
                var folder = GetSettingsRootFolder();
                Directory.CreateDirectory(folder);
                var assetPath = Path.Combine(folder, KSettingsFileName).Replace("\\", "/");

                // --- 自动创建并关联所有子配置 ---
                settings.creationDefaults = GetOrCreateSubAsset<MrPathCreationDefaults>("MrPath_CreationDefaults");
                settings.appearanceDefaults =
                    GetOrCreateSubAsset<MrPathAppearanceDefaults>("MrPath_AppearanceDefaults");

                settings.terrainOperations = GetOrCreateSubAsset<MrPathTerrainOperations>("MrPath_TerrainOperations");

                // AdvancedSettings 可选：不在初始化时自动创建，由用户在 UI 中创建
                // settings.advancedSettings = GetOrCreateSubAsset<MrPathAdvancedSettings>("MrPath_Advanced");
                // ------------------------------------------

                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.SetLabels(settings, new[]
                {
                    "MrPathCoreAsset"
                }); // 创建后立即添加标签
                Debug.Log($"[MrPath] Created new Project Settings asset at: {assetPath}");
            }
            // 确保现有资产也有标签
            else if (AssetDatabase.GetLabels(settings).All(l => l != "MrPathCoreAsset"))
            {
                AssetDatabase.SetLabels(settings, new[]
                {
                    "MrPathCoreAsset"
                });
            }


            var changed = false;
            if (!settings.creationDefaults)
            {
                settings.creationDefaults = GetOrCreateSubAsset<MrPathCreationDefaults>("MrPath_CreationDefaults");
                changed = true;
            }

            if (!settings.appearanceDefaults)
            {
                settings.appearanceDefaults =
                    GetOrCreateSubAsset<MrPathAppearanceDefaults>("MrPath_AppearanceDefaults");
                changed = true;
            }

            if (!settings.terrainOperations)
            {
                settings.terrainOperations = GetOrCreateSubAsset<MrPathTerrainOperations>("MrPath_TerrainOperations");
                changed = true;
            }

            if (!settings.advancedSettings)
            {
                // AdvancedSettings 可选：不自动创建，保持为空，允许在 UI 中手动创建
                // settings.advancedSettings = GetOrCreateSubAsset<MrPathAdvancedSettings>("MrPath_Advanced");
                // changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.LogWarning(
                    "[MrPath] Project Settings had missing sub-asset references, attempted to re-create them.");
            }
            // --------------------------

            return settings;
        }

        internal static MrPathProjectSettings LoadExistingSettings()
        {
            var guids = AssetDatabase.FindAssets("l:MrPathCoreAsset t:MrPathProjectSettings");
            if (guids is { Length: > 1 })
                Debug.LogWarning(
                    $"[MrPath] Found multiple MrPathProjectSettings assets ({guids.Length}). Using first: {AssetDatabase.GUIDToAssetPath(guids[0])}");
            if (guids is { Length: > 0 })
                return AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var fallbackPath = Path.Combine(GetSettingsRootFolder(), KSettingsFileName).Replace("\\", "/");
            var settings = AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(fallbackPath);
            if (settings) return settings;

            return AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(KSettingsPath); // Final fallback
        }

        private static string GetToolRootFolder()
        {
            var guids = AssetDatabase.FindAssets("l:MrPathCoreAsset t:MrPathProjectSettings");
            if (guids is { Length: > 0 })
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                var settingsDir = Path.GetDirectoryName(assetPath);
                return Path.GetDirectoryName(settingsDir)?.Replace("\\", "/");
            }

            var scriptGuid = AssetDatabase.FindAssets("t:Script MrPathProjectSettings").FirstOrDefault();
            if (!string.IsNullOrEmpty(scriptGuid))
            {
                var scriptPath =
                    AssetDatabase.GUIDToAssetPath(scriptGuid); // Assets/.../Editor/Settings/MrPathProjectSettings.cs
                var settingsDir = Path.GetDirectoryName(scriptPath); // Assets/.../Editor/Settings
                var editorDir = Path.GetDirectoryName(settingsDir); // Assets/.../Editor
                var rootDir = Path.GetDirectoryName(editorDir); // Assets/... (tool root)
                if (rootDir != null) return rootDir.Replace("\\", "/");
            }

            // Fallback if script not found (shouldn't happen)
            Debug.LogError("[MrPath] Could not determine tool root folder dynamically. Falling back to default.");
            return "Assets/MrPathV2"; // Adjust if your default path differs
        }

        public static string GetSettingsRootFolder() => Path.Combine(GetToolRootFolder(), "Settings").Replace("\\", "/");

        private static T GetOrCreateSubAsset<T>(string fileName) where T : ScriptableObject
        {
            var folder = GetSettingsRootFolder();
            // 子资产通常放在 Settings 文件夹下一层，例如 Settings/Advanced/
            var subFolder =
                Path.Combine(folder, typeof(T).Name.Replace("MrPath", "").Replace("Settings", ""));
            Directory.CreateDirectory(subFolder); // Ensure subfolder exists
            var fullPath = Path.Combine(subFolder, $"{fileName}.asset").Replace("\\", "/");

            var asset = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            if (asset) return asset;

            asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, fullPath);
            Debug.Log($"[MrPath] Created sub-asset: {fullPath}");
            // No need to SaveAssets here, GetOrCreateSettings handles it

            return asset;
        }
        // -------------------------------------------------------------------------------------
    }
}
