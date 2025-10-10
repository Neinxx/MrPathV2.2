// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathProjectSettings.cs
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
        public const string k_SettingsPath = "Assets/__temp/MrPathV2.2/Settings/MrPath_ProjectSettings.asset";

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
            string path = $"Assets/__temp/MrPathV2.2/Settings/{fileName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
                // 注意：这里不需要立刻保存，GetOrCreateSettings()的末尾会统一保存
            }
            return asset;
        }
    }
}