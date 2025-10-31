#if UNITY_EDITOR
using __temp.MrPathV2.Editor.Settings;
using __temp.MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Inspectors
{
    /// <summary>
    /// MrPath_ProjectSettings 的自定义 Inspector
    /// 提供只读显示和快速访问 Settings 面板的功能
    /// </summary>
    [CustomEditor(typeof(MrPathProjectSettings))]
    public class MrPathProjectSettingsEditor : UnityEditor.Editor
    {
        private GUIStyle _readOnlyStyle;
        private GUIStyle _buttonStyle;
        private bool _initialized;

        private void InitializeStyles()
        {
            if (_initialized) return;

            _readOnlyStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 5, 5)
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 35
            };

            _initialized = true;
        }

        public override void OnInspectorGUI()
        {
            InitializeStyles();

            var settings = target as MrPathProjectSettings;
            if (!settings) return;

            // 标题和说明
            EditorGUILayout.Space(10);
            
            using (new EditorGUILayout.VerticalScope(_readOnlyStyle))
            {
                var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter
                };
                
                EditorGUILayout.LabelField("🛠️ MrPath 项目设置", titleStyle);
                EditorGUILayout.Space(5);
                
                var infoStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter
                };
                
                EditorGUILayout.LabelField("此资产为只读模式，请使用下方按钮打开完整的设置面板进行配置", infoStyle);
            }

            EditorGUILayout.Space(10);

            // 打开 Settings 面板的按钮 - 使用更安全的按钮实现
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                
                var buttonContent = new GUIContent("🔧 打开 MrPath 设置面板", "打开项目设置中的 MrPath 配置面板");
                
                // Use EditorGUILayout.Button instead of GUILayout.Button for better compatibility
                if (GUILayout.Button(buttonContent, _buttonStyle, GUILayout.Width(250)))
                {
                    // Delay the action to avoid GUI conflicts
                    EditorApplication.delayCall += OpenMrPathSettings;
                }
                
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(15);

            // 只读信息显示
            using (new EditorGUILayout.VerticalScope(_readOnlyStyle))
            {
                EditorGUILayout.LabelField("📋 配置概览", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                // 显示主要配置信息（只读）
                GUI.enabled = false;
                
                EditorGUILayout.ObjectField("创建默认值", settings.creationDefaults, typeof(MrPathCreationDefaults), false);
                EditorGUILayout.ObjectField("外观默认值", settings.appearanceDefaults, typeof(MrPathAppearanceDefaults), false);
                EditorGUILayout.ObjectField("地形操作", settings.terrainOperations, typeof(MrPathTerrainOperations), false);
                EditorGUILayout.ObjectField("高级设置", settings.advancedSettings, typeof(MrPathAdvancedSettings), false);
                EditorGUILayout.ObjectField("默认道路配方", settings.stylizedRoadRecipe, typeof(StylizedRoadRecipe), false);

                EditorGUILayout.Space(5);
                
                // 显示集合统计信息
                EditorGUILayout.LabelField("📊 资产统计", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"路径配置文件: {settings.profiles?.Count ?? 0} 个");
                EditorGUILayout.LabelField($"道路配方: {settings.roadRecipes?.Count ?? 0} 个");
                EditorGUILayout.LabelField($"遮罩资产: {settings.masks?.Count ?? 0} 个");

                GUI.enabled = true;
            }

            EditorGUILayout.Space(10);

            // 底部提示
            using (new EditorGUILayout.VerticalScope(_readOnlyStyle))
            {
                var tipStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Italic
                };
                
                EditorGUILayout.LabelField("💡 提示: 要修改这些设置，请点击上方按钮打开完整的设置面板，或通过菜单 Edit → Project Settings → MrPath 访问。", tipStyle);
            }
        }

        /// <summary>
        /// 打开 MrPath 设置面板
        /// </summary>
        private static void OpenMrPathSettings()
        {
            // 打开项目设置窗口并导航到 MrPath 设置
            SettingsService.OpenProjectSettings("Project/MrPath");
        }

        /// <summary>
        /// 在菜单中添加快速访问选项
        /// </summary>
        [MenuItem("MrPath/打开设置面板", priority = 0)]
        public static void OpenMrPathSettingsFromMenu()
        {
            OpenMrPathSettings();
        }
    }
}
#endif
