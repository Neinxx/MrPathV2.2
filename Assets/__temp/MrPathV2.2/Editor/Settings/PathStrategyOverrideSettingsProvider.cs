using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace MrPathV2
{
    /// <summary>
    /// 在项目设置中直接配置曲线策略（Bezier/Catmull-Rom），并可同步到注册表。
    /// 避免用户必须进入 PathStrategyRegistry 手动维护条目。
    /// </summary>
    public class PathStrategyOverrideSettingsProvider : SettingsProvider
    {
        private SerializedObject _settingsObj;
        private bool _hasPendingSave;

        public PathStrategyOverrideSettingsProvider(string path, SettingsScope scope) : base(path, scope)
        {
            keywords = new HashSet<string>(new[] { "MrPath", "Curve", "Bezier", "Catmull", "Strategy", "策略" });
        }

        [SettingsProvider]
        public static SettingsProvider Register() => new PathStrategyOverrideSettingsProvider("Project/MrPath Curve Strategies", SettingsScope.Project);

        public override void OnGUI(string searchContext)
        {
            if (_settingsObj == null || _settingsObj.targetObject == null)
            {
                _settingsObj = new SerializedObject(PathToolSettings.Instance);
            }

            _settingsObj.Update();

            EditorGUILayout.LabelField("曲线策略覆盖", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("在此直接指定 Bezier/Catmull-Rom 策略。运行时代码仍从注册表读取；点击‘同步到注册表’可确保应用。", MessageType.None);

            var bezierProp = _settingsObj.FindProperty("bezierStrategy");
            var catmullProp = _settingsObj.FindProperty("catmullRomStrategy");

            EditorGUILayout.PropertyField(bezierProp, new GUIContent("Bezier 策略"));
            EditorGUILayout.PropertyField(catmullProp, new GUIContent("Catmull-Rom 策略"));

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("创建默认策略资产", GUILayout.Width(160)))
                {
                    CreateDefaultStrategies(bezierProp, catmullProp);
                }
                if (GUILayout.Button("打开策略文件夹", GUILayout.Width(140)))
                {
                    string dir = "Assets/__temp/MrPathV2.2/Settings/Strategies";
                    System.IO.Directory.CreateDirectory(dir);
                    EditorUtility.RevealInFinder(dir);
                }
                if (GUILayout.Button("同步到注册表", GUILayout.Width(140)))
                {
                    SyncOverridesToRegistry();
                }
            }

            bool applied = _settingsObj.ApplyModifiedProperties();
            if (applied)
            {
                EditorUtility.SetDirty(PathToolSettings.Instance);
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(PathToolSettings.Instance);
#else
                AssetDatabase.SaveAssets();
#endif
                _hasPendingSave = false;
                // 自动同步一次，保持运行时代码可用
                SyncOverridesToRegistry();
            }

            if (_hasPendingSave)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox("设置已变更，尚未写入磁盘。", MessageType.Info);
            }
        }

        private void CreateDefaultStrategies(SerializedProperty bezierProp, SerializedProperty catmullProp)
        {
            string dir = "Assets/__temp/MrPathV2.2/Settings/Strategies";
            System.IO.Directory.CreateDirectory(dir);

            if (bezierProp.objectReferenceValue == null)
            {
                var bez = ScriptableObject.CreateInstance<BezierStrategy>();
                string path = System.IO.Path.Combine(dir, "BezierStrategy.asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(bez, path);
                bezierProp.objectReferenceValue = bez;
            }

            if (catmullProp.objectReferenceValue == null)
            {
                var cat = ScriptableObject.CreateInstance<CatmullRomStrategy>();
                string path = System.IO.Path.Combine(dir, "CatmullRomStrategy.asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(cat, path);
                catmullProp.objectReferenceValue = cat;
            }

            _settingsObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(PathToolSettings.Instance);
#if UNITY_2020_3_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(PathToolSettings.Instance);
#else
            AssetDatabase.SaveAssets();
#endif

            // 创建后立即同步
            SyncOverridesToRegistry();
            EditorUtility.DisplayDialog("策略创建", "已创建默认策略资产并绑定到设置，同时同步到注册表。", "确定");
        }

        private void SyncOverridesToRegistry()
        {
            string targetPath = "Assets/__temp/MrPathV2.2/Settings/Resources/PathStrategyRegistry.asset";
            CreateOrRepairRegistryAsset(targetPath);

            var registry = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(targetPath);
            if (registry == null) return;

            var rso = new SerializedObject(registry);
            var entriesProp = rso.FindProperty("_strategyEntries");

            void SetEntry(CurveType type, PathStrategy strat)
            {
                if (strat == null) return; // 未指定覆盖则跳过

                int foundIndex = -1;
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var e = entriesProp.GetArrayElementAtIndex(i);
                    var typeProp = e.FindPropertyRelative("type");
                    if ((CurveType)typeProp.enumValueIndex == type) { foundIndex = i; break; }
                }

                if (foundIndex >= 0)
                {
                    var e = entriesProp.GetArrayElementAtIndex(foundIndex);
                    e.FindPropertyRelative("strategy").objectReferenceValue = strat;
                }
                else
                {
                    entriesProp.InsertArrayElementAtIndex(entriesProp.arraySize);
                    var e = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
                    e.FindPropertyRelative("type").enumValueIndex = (int)type;
                    e.FindPropertyRelative("strategy").objectReferenceValue = strat;
                }
            }

            var settings = PathToolSettings.Instance;
            SetEntry(CurveType.Bezier, settings.bezierStrategy);
            SetEntry(CurveType.CatmullRom, settings.catmullRomStrategy);

            if (rso.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(registry);
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(registry);
#else
                AssetDatabase.SaveAssets();
#endif
            }
        }

        private void CreateOrRepairRegistryAsset(string targetPath)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(PathStrategyRegistry)}");
            PathStrategyRegistry asset = null;
            string currentPath = null;
            if (guids != null && guids.Length > 0)
            {
                currentPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                asset = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(currentPath);
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath));
            if (asset != null && currentPath != targetPath)
            {
                AssetDatabase.MoveAsset(currentPath, targetPath);
                asset = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(targetPath);
            }

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PathStrategyRegistry>();
                AssetDatabase.CreateAsset(asset, targetPath);
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(asset);
#else
                AssetDatabase.SaveAssets();
#endif
            }
        }
    }
}