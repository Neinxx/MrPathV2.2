using System.IO;
using __temp.MrPathV2.Editor.Settings;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Settings;
using __temp.MrPathV2.Runtime.Strategies;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.Inspectors
{
    //  将策略管理功能（创建、同步）直接集成到此资产的编辑器中。

    [CustomEditor(typeof(MrPathAdvancedSettings))]
    public class MrPathAdvancedSettingsEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            // 默认属性区域（使用 IMGUI 绘制，避免递归创建导致的 StackOverflow）
            var defaultInspector = new IMGUIContainer(() =>
            {
                DrawDefaultInspector();
            });
            root.Add(defaultInspector);

            // 分隔
            root.Add(new VisualElement
            {
                style =
                {
                    height = 10
                }
            });

            // 标题
            root.Add(new Label("策略管理工具")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 4
                }
            });

            // 容器（等同于 IMGUI 的 helpBox）
            var box = new VisualElement
            {
                style =
                {
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    paddingTop = 4,
                    paddingBottom = 4,
                    paddingLeft = 4,
                    paddingRight = 4,
                    marginBottom = 6
                }
            };
            root.Add(box);

            // 按钮：创建默认策略
            var createBtn = new Button(CreateDefaultStrategies)
            {
                text = "创建默认策略资产"
            };
            box.Add(createBtn);

            // 按钮：打开策略文件夹
            // var openFolderBtn = new Button(() =>
            // {
            //     var dir = GetDynamicStrategiesPath();
            //     if (string.IsNullOrEmpty(dir)) return;
            //     Directory.CreateDirectory(dir);
            //     EditorUtility.RevealInFinder(dir);
            // }) { text = "打开策略文件夹" };
            // box.Add(openFolderBtn);

            // 按钮：同步策略
            var syncBtn = new Button(() =>
            {
                SyncOverridesToRegistry();
                EditorUtility.DisplayDialog("同步完成", "已将当前指定的策略资产同步到 PathStrategyRegistry。", "确定");
            })
            {
                text = "同步策略到注册表"
            };
            box.Add(syncBtn);

            return root;
        }

        private string GetDynamicStrategiesPath() => Path.Combine(MrPathProjectSettings.GetSettingsRootFolder(), "Strategies").Replace("\\", "/");

        private string GetDynamicResourcesPath() => Path.Combine(MrPathProjectSettings.GetSettingsRootFolder(), "Resources").Replace("\\", "/");

        private void CreateDefaultStrategies()
        {
            var settings = (MrPathAdvancedSettings)target;
            var so = new SerializedObject(settings);
            var bezierProp = so.FindProperty("bezierStrategy");
            var catmullProp = so.FindProperty("catmullRomStrategy");

            var dir = GetDynamicStrategiesPath();
            if (string.IsNullOrEmpty(dir)) return;

            Directory.CreateDirectory(dir);

            if (!bezierProp.objectReferenceValue)
            {
                var bez = CreateInstance<BezierStrategy>();
                var path = Path.Combine(dir, "BezierStrategy.asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(bez, path);
                bezierProp.objectReferenceValue = bez;
            }

            if (!catmullProp.objectReferenceValue)
            {
                var cat = CreateInstance<CatmullRomStrategy>();
                var path = Path.Combine(dir, "CatmullRomStrategy.asset").Replace("\\", "/");
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
            // 确保注册表资产存在
            var registryPath = Path.Combine(GetDynamicResourcesPath(), "PathStrategyRegistry.asset").Replace("\\", "/");
            var registry = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(registryPath);
            if (!registry)
            {
                registry = CreateInstance<PathStrategyRegistry>();
                Directory.CreateDirectory(Path.GetDirectoryName(registryPath) ?? string.Empty);
                AssetDatabase.CreateAsset(registry, registryPath);
                AssetDatabase.SaveAssets();
            }

            // 将当前设置中的策略写入注册表
            var settings = (MrPathAdvancedSettings)target;
            var entriesProp = new SerializedObject(registry).FindProperty("strategyEntries"); // field is private but SerializeField; use name

            // 由于 strategyEntries 是 private，需要通过 SerializedObject 修改
            var so = new SerializedObject(registry);
            var listProp = so.FindProperty("strategyEntries");
            if (listProp == null)
            {
                Debug.LogError("[MrPathAdvancedSettingsEditor] Failed to find 'strategyEntries' property on PathStrategyRegistry.");
                return;
            }

            // 清空并重新填充
            listProp.arraySize = 0;

            void AddEntry(int index, CurveType type, PathStrategy strategy)
            {
                if (strategy == null) return;
                listProp.InsertArrayElementAtIndex(index);
                var element = listProp.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("type").enumValueIndex = (int)type;
                element.FindPropertyRelative("strategy").objectReferenceValue = strategy;
            }

            var idx = 0;
            if (settings.bezierStrategy)
            {
                AddEntry(idx++, CurveType.Bezier, settings.bezierStrategy);
            }
            if (settings.catmullRomStrategy)
            {
                AddEntry(idx++, CurveType.CatmullRom, settings.catmullRomStrategy);
            }

            so.ApplyModifiedProperties();

            // 通知注册表立即刷新缓存
            registry.ValidateConfiguration();
        }
    }
}
