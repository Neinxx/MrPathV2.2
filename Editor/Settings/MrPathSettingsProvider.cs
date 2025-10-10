// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathSettingsProvider.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Reflection;

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

            _settings.ApplyModifiedProperties();

            EditorGUILayout.Space(20);

            // 可以在此添加文档链接、报告问题等辅助功能
            if (GUILayout.Button("重新扫描并填充所有操作"))
            {
                ScanAndFillOperations();
            }
        }

        /// <summary>
        /// 绘制一个指向子配置资产的带操作按钮的字段。
        /// </summary>
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
                    string subAssetName = $"MrPath_{assetType.Name.Replace("MrPath", "").Replace("Settings", "")}";
                    string path = $"Assets/__temp/MrPathV2.2/Settings/{subAssetName}.asset";
                    var newAsset = ScriptableObject.CreateInstance(assetType);
                    AssetDatabase.CreateAsset(newAsset, path);
                    AssetDatabase.SaveAssets();
                    prop.objectReferenceValue = newAsset;
                }
                Selection.activeObject = prop.objectReferenceValue;
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 扫描项目中的 PathTerrainOperation 资产并填充到配置中。
        /// </summary>
        private void ScanAndFillOperations()
        {
            var terrainOpsProp = _settings.FindProperty("terrainOperations");
            if (terrainOpsProp.objectReferenceValue == null)
            {
                Debug.LogWarning("地形操作配置资产丢失，请先创建。");
                return;
            }

            var opsSO = new SerializedObject(terrainOpsProp.objectReferenceValue);
            var opsArrayProp = opsSO.FindProperty("operations");

            var guids = AssetDatabase.FindAssets($"t:{nameof(PathTerrainOperation)}");
            var foundOps = guids
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<PathTerrainOperation>(path))
                .Where(op => op != null)
                .OrderBy(op => op.order)
                .ToList();

            opsArrayProp.ClearArray();
            for (int i = 0; i < foundOps.Count; ++i)
            {
                opsArrayProp.InsertArrayElementAtIndex(i);
                opsArrayProp.GetArrayElementAtIndex(i).objectReferenceValue = foundOps[i];
            }

            opsSO.ApplyModifiedProperties();
            Debug.Log($"MrPath: 扫描完成，已找到并填充 {foundOps.Count} 个地形操作。");
        }


        // 注册设置提供器到 Project Settings 窗口
        [SettingsProvider]
        public static SettingsProvider CreateMrPathSettingsProvider()
        {
            var provider = new MrPathSettingsProvider("Project/MrPath", SettingsScope.Project);
            return provider;
        }
    }
}