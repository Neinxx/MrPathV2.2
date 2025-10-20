using System.Collections.Generic;
using System.IO;
using System.Linq;
using __temp.MrPathV2._2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Settings
{
    /// <summary>
    /// MrPath 工具所有配置资产的根引用和导航中心。
    /// 它遵循单一职责原则，只负责持有对其他具体配置资产的引用。
    /// </summary>
    public class MrPathProjectSettings : ScriptableObject
    {
        // 定义了主设置文件的唯一、标准路径（保留作向后兼容的最终兜底）
        private const string KSettingsPath = "Assets/MrPathV2.2/Settings/MrPath_ProjectSettings.asset";
        // 文件名常量；文件夹通过 GetSettingsRootFolder() 动态决定
        private const string KSettingsFileName = "MrPath_ProjectSettings.asset";

        // --- 子配置资产的引用 ---
        [Tooltip("新路径创建时的默认值配置")] public MrPathCreationDefaults creationDefaults;

        [Tooltip("路径默认外观与预览材质配置")] public MrPathAppearanceDefaults appearanceDefaults;


        [Tooltip("数据驱动的地形操作列表")] public MrPathTerrainOperations terrainOperations;

        /// <summary>
        /// 获取或创建主设置资产的静态方法。这是全局访问设置的唯一入口。
        /// </summary>
        internal static MrPathProjectSettings GetOrCreateSettings()
        {
            // 尝试通过标签或常量路径加载
            var settings = LoadExistingSettings();
            if (settings == null)
            {
                // 创建新实例并放置在工具目录下的 Settings
                settings = CreateInstance<MrPathProjectSettings>();
                var folder = GetSettingsRootFolder();
                Directory.CreateDirectory(folder);
                var assetPath = Path.Combine(folder, "MrPath_ProjectSettings.asset").Replace("\\", "/");

                // 自动创建并关联子配置
                settings.creationDefaults = GetOrCreateSubAsset<MrPathCreationDefaults>("MrPath_CreationDefaults");
                settings.appearanceDefaults =
                    GetOrCreateSubAsset<MrPathAppearanceDefaults>("MrPath_AppearanceDefaults");
                settings.terrainOperations = GetOrCreateSubAsset<MrPathTerrainOperations>("MrPath_TerrainOperations");
                GetOrCreateSubAsset<MrPathAdvancedSettings>("MrPath_Advanced");

                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();
            }

            // 添加标签
            AssetDatabase.SetLabels(settings, new[] { "MrPathCoreAsset" });
            return settings;
        }

        private static MrPathProjectSettings LoadExistingSettings()
        {
            // 1) 通过标签查找
            var guids = AssetDatabase.FindAssets("l:MrPathCoreAsset t:MrPathProjectSettings");
            if (guids is { Length: > 1 })
            {
                Debug.LogWarning($"[MrPath] 检测到多个 MrPathProjectSettings 资产({guids.Length})，将优先使用首个：" +
                                 AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (guids is { Length: > 0 })
            {
                var firstPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(firstPath);
            }

            // 2) 尝试根据默认动态路径加载（兼容旧项目或未打标签的情况）
            var fallbackPath = Path.Combine(GetSettingsRootFolder(), KSettingsFileName).Replace("\\", "/");
            var settings = AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(fallbackPath);
            if (settings != null) return settings;

            // 3) 最后回退到旧常量路径（历史兼容）
            return AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(KSettingsPath);
        }

        /// <summary>
        /// 返回工具根目录(含 Assets/)，通过脚本本身位置推断，保证工具移动后仍能正确工作。
        /// </summary>
        private static string GetToolRootFolder()
        {
            // 先尝试根据已存在的设置资产定位
            var guids = AssetDatabase.FindAssets("l:MrPathCoreAsset t:MrPathProjectSettings");
            if (guids != null && guids.Length > 0)
            {
                var assetPath =
                    AssetDatabase.GUIDToAssetPath(guids[0]); // Assets/ToolRoot/Settings/MrPath_ProjectSettings.asset
                var settingsDir = Path.GetDirectoryName(assetPath); // Assets/ToolRoot/Settings
                return Path.GetDirectoryName(settingsDir)?.Replace("\\", "/"); // Assets/ToolRoot
            }

            // 未创建过资产时，根据脚本文件所在目录推断 (Editor/Settings)
            var scriptGuid = AssetDatabase.FindAssets("MrPathProjectSettings t:Script").FirstOrDefault();
            if (string.IsNullOrEmpty(scriptGuid)) return Path.GetDirectoryName(Path.GetDirectoryName(KSettingsPath));
            {
                var scriptPath =
                    AssetDatabase.GUIDToAssetPath(scriptGuid); // Assets/.../Editor/Settings/MrPathProjectSettings.cs
                var settingsDir = Path.GetDirectoryName(scriptPath); // Assets/.../Editor/Settings
                var editorDir = Path.GetDirectoryName(settingsDir); // Assets/.../Editor
                var rootDir = Path.GetDirectoryName(editorDir); // Assets/... (tool root)
                if (rootDir != null) return rootDir.Replace("\\", "/");
            }

            // 最后回退到常量路径的上一级
            return Path.GetDirectoryName(Path.GetDirectoryName(KSettingsPath));
        }

        public static string GetSettingsRootFolder()
        {
            return Path.Combine(GetToolRootFolder(), "Settings").Replace("\\", "/");
        }

        private static T GetOrCreateSubAsset<T>(string fileName) where T : ScriptableObject
        {
            var folder = GetSettingsRootFolder();
            var fullPath = Path.Combine(folder, $"{fileName}.asset").Replace("\\", "/");
            var asset = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            if (asset != null) return asset;
            asset = CreateInstance<T>();
            Directory.CreateDirectory(folder);
            AssetDatabase.CreateAsset(asset, fullPath);

            return asset;
        }

        [Tooltip("路径配置文件集合")] // 添加注释，描述属性用途
        public List<PathProfile> profiles = new List<PathProfile>(); // 初始化为一个空列表

        [Tooltip("道路配方集合")] public List<StylizedRoadRecipe> roadRecipes = new List<StylizedRoadRecipe>();
    }
}