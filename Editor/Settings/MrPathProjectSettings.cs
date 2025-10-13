using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace MrPathV2
{
    /// <summary>
    /// MrPath 工具所有配置资产的根引用和导航中心。
    /// 它遵循单一职责原则，只负责持有对其他具体配置资产的引用。
    /// </summary>
    public class MrPathProjectSettings : ScriptableObject
    {
        // 定义了主设置文件的唯一、标准路径
        public const string k_SettingsPath = "Assets/MrPathV2.2/Settings/MrPath_ProjectSettings.asset";

        // --- 子配置资产的引用 ---
        [Tooltip("新路径创建时的默认值配置")]
        public MrPathCreationDefaults creationDefaults;

        [Tooltip("路径默认外观与预览材质配置")]
        public MrPathAppearanceDefaults appearanceDefaults;

        [Tooltip("场景视图中UI面板的布局配置")]
        public MrPathSceneUISettings sceneUISettings;

        [Tooltip("数据驱动的地形操作列表")]
        public MrPathTerrainOperations terrainOperations;

        [Tooltip("高级开发者设置，如依赖注入工厂和策略覆盖")]
        public MrPathAdvancedSettings advancedSettings;

        /// <summary>
        /// 获取或创建主设置资产的静态方法。这是全局访问设置的唯一入口。
        /// </summary>
        internal static MrPathProjectSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MrPathProjectSettings>(k_SettingsPath);
            if (settings == null)
            {
                // 如果主设置文件不存在，则创建一个新的实例
                settings = CreateInstance<MrPathProjectSettings>();

                // 关键步骤：自动创建并关联所有子配置资产
                settings.creationDefaults = GetOrCreateSubAsset<MrPathCreationDefaults>("MrPath_CreationDefaults");
                settings.appearanceDefaults = GetOrCreateSubAsset<MrPathAppearanceDefaults>("MrPath_AppearanceDefaults");
                settings.sceneUISettings = GetOrCreateSubAsset<MrPathSceneUISettings>("MrPath_SceneUI");
                settings.terrainOperations = GetOrCreateSubAsset<MrPathTerrainOperations>("MrPath_TerrainOperations");
                settings.advancedSettings = GetOrCreateSubAsset<MrPathAdvancedSettings>("MrPath_Advanced");

                // 确保目标目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(k_SettingsPath));

                // 在数据库中创建资产并保存
                AssetDatabase.CreateAsset(settings, k_SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        /// <summary>
        /// 一个通用的辅助方法，用于获取或创建子配置资产。
        /// </summary>


        private static T GetOrCreateSubAsset<T>(string fileName) where T : ScriptableObject
        {
            // 直接拼接目标相对路径（相对于 Assets 目录）
            string relativePath = Path.Combine("MrPathV2.2", "Settings", $"{fileName}.asset");
            // 转换为 AssetDatabase 要求的格式（以 Assets/ 开头，统一使用 / 斜杠）
            string fullPath = Path.Combine("Assets", relativePath).Replace("\\", "/");
        
            var asset = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            if (asset == null)
            {
                asset = CreateInstance<T>();
                // 确保目录存在（自动创建不存在的文件夹）
                string directoryPath = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                AssetDatabase.CreateAsset(asset, fullPath);
            }
            return asset;
        }
        [Tooltip("路径配置文件集合")] // 添加注释，描述属性用途
        public List<PathProfile> profiles = new List<PathProfile>(); // 初始化为一个空列表
        [Tooltip("道路配方集合")]
        public List<StylizedRoadRecipe> roadRecipes = new List<StylizedRoadRecipe>();
    }
}