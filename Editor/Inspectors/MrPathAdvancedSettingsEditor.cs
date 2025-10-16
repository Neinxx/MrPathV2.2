// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Inspectors/MrPathAdvancedSettingsEditor.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace MrPathV2
{
    /// <summary>
    /// 为 MrPathAdvancedSettings 提供自定义的 Inspector 界面。
    //  将策略管理功能（创建、同步）直接集成到此资产的编辑器中。
    /// </summary>
    [CustomEditor(typeof(MrPathAdvancedSettings))]
    public class MrPathAdvancedSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 首先绘制默认的 Inspector 字段 (工厂、策略覆盖等)
            base.OnInspectorGUI();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("策略管理工具", EditorStyles.boldLabel);

            // 将旧 Provider 中的功能按钮移植到这里
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("创建默认策略资产"))
                {
                    CreateDefaultStrategies();
                }

                if (GUILayout.Button("打开策略文件夹"))
                {
                    string dir = "Assets/__temp/MrPathV2.2/Settings/Strategies";
                    Directory.CreateDirectory(dir);
                    EditorUtility.RevealInFinder(dir);
                }

                if (GUILayout.Button("同步策略到注册表"))
                {
                    SyncOverridesToRegistry();
                    EditorUtility.DisplayDialog("同步完成", "已将当前指定的策略资产同步到 PathStrategyRegistry。", "确定");
                }
            }
        }

        private string GetDynamicStrategiesPath()
        {
            string mrPathFolder = AssetDatabase.FindAssets("MrPathV2.2").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("MrPathV2.2"));

            if (string.IsNullOrEmpty(mrPathFolder))
            {
                Debug.LogError("未找到 MrPathV2.2 文件夹！");
                return null;
            }

            return Path.Combine(mrPathFolder, "Settings", "Strategies").Replace("\\", "/");
        }

        private string GetDynamicResourcesPath()
        {
            string mrPathFolder = AssetDatabase.FindAssets("MrPathV2.2").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("MrPathV2.2"));

            if (string.IsNullOrEmpty(mrPathFolder))
            {
                Debug.LogError("未找到 MrPathV2.2 文件夹！");
                return null;
            }

            return Path.Combine(mrPathFolder, "Settings", "Resources").Replace("\\", "/");
        }

        private void CreateDefaultStrategies()
        {
            var settings = (MrPathAdvancedSettings)target;
            var so = new SerializedObject(settings);
            var bezierProp = so.FindProperty("bezierStrategy");
            var catmullProp = so.FindProperty("catmullRomStrategy");

            string dir = GetDynamicStrategiesPath();
            if (string.IsNullOrEmpty(dir)) return;

            Directory.CreateDirectory(dir);

            if (bezierProp.objectReferenceValue == null)
            {
                var bez = CreateInstance<BezierStrategy>();
                string path = Path.Combine(dir, "BezierStrategy.asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(bez, path);
                bezierProp.objectReferenceValue = bez;
            }

            if (catmullProp.objectReferenceValue == null)
            {
                var cat = CreateInstance<CatmullRomStrategy>();
                string path = Path.Combine(dir, "CatmullRomStrategy.asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(cat, path);
                catmullProp.objectReferenceValue = cat;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // 创建后立即同步
            SyncOverridesToRegistry();
            EditorUtility.DisplayDialog("策略创建", "已创建默认策略资产并绑定到设置，同时自动同步到注册表。", "确定");
        }

        private void SyncOverridesToRegistry()
        {
            var settings = (MrPathAdvancedSettings)target;

            // 确保注册表资产存在
            string registryPath = Path.Combine(GetDynamicResourcesPath(), "PathStrategyRegistry.asset").Replace("\\", "/");
            var registry = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(registryPath);
            if (registry == null)
            {
                registry = CreateInstance<PathStrategyRegistry>();
                Directory.CreateDirectory(Path.GetDirectoryName(registryPath));
                AssetDatabase.CreateAsset(registry, registryPath);
                AssetDatabase.SaveAssets();
            }

            var rso = new SerializedObject(registry);
            var entriesProp = rso.FindProperty("_strategyEntries");

            // 封装的更新逻辑
            void SetEntry(CurveType type, PathStrategy strat)
            {
                if (strat == null) return; // 如果未指定覆盖，则不进行操作

                int foundIndex = -1;
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var e = entriesProp.GetArrayElementAtIndex(i);
                    if ((CurveType)e.FindPropertyRelative("type").enumValueIndex == type)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                SerializedProperty entryProp;
                if (foundIndex >= 0)
                {
                    entryProp = entriesProp.GetArrayElementAtIndex(foundIndex);
                }
                else
                {
                    entriesProp.InsertArrayElementAtIndex(entriesProp.arraySize);
                    entryProp = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
                }

                entryProp.FindPropertyRelative("type").enumValueIndex = (int)type;
                entryProp.FindPropertyRelative("strategy").objectReferenceValue = strat;
            }

            rso.ApplyModifiedProperties();
        }
    }
}