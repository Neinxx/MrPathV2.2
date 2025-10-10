using UnityEditor;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【金身不坏版】MrPath 工具的项目设置提供器。
    /// - 解决了因编辑器“轮回”(Domain Reload)导致的UI状态丢失问题。
    /// </summary>
    public class PathToolSettingsProvider : SettingsProvider
    {
        private SerializedObject _settingsObj;
        private bool _hasPendingSave;

        // 折叠状态的持久化
        private bool _creationFoldout = true;
        private bool _appearanceFoldout = true;
        private bool _previewFoldout = true;
        private bool _operationsFoldout = true;
        private bool _registryFoldout = true;
        private const string CreationFoldoutKey = "MrPath_CreationFoldout";
        private const string AppearanceFoldoutKey = "MrPath_AppearanceFoldout";
        private const string PreviewFoldoutKey = "MrPath_PreviewFoldout";
        private const string OperationsFoldoutKey = "MrPath_OperationsFoldout";
        private const string RegistryFoldoutKey = "MrPath_RegistryFoldout";

        public PathToolSettingsProvider(string path, SettingsScope scope) : base(path, scope)
        {
            keywords = GetSearchKeywordsFromPath("MrPath;Path;路径;策略;Registry");
        }

        [SettingsProvider]
        public static SettingsProvider Register() => new PathToolSettingsProvider("Project/MrPath Settings", SettingsScope.Project);

        // OnActivate 不再负责初始化 SerializedObject，只负责读取UI状态
        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            _creationFoldout = SessionState.GetBool(CreationFoldoutKey, true);
            _appearanceFoldout = SessionState.GetBool(AppearanceFoldoutKey, true);
            _previewFoldout = SessionState.GetBool(PreviewFoldoutKey, true);
            _operationsFoldout = SessionState.GetBool(OperationsFoldoutKey, true);
            _registryFoldout = SessionState.GetBool(RegistryFoldoutKey, true);
        }

        public override void OnGUI(string searchContext)
        {
            // --- 【核心修正】固魂之术 ---
            // 在每次 OnGUI 时，都确保 _settingsObj 是有效的。
            if (_settingsObj == null || _settingsObj.targetObject == null)
            {
                var instance = PathToolSettings.Instance;
                _settingsObj = new SerializedObject(instance);
            }
            // -------------------------

            _settingsObj.Update();

            EditorGUILayout.LabelField("MrPath 工具配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("配置路径工具的默认创建参数与外观样式。", MessageType.None);

            EditorGUILayout.Space();

            // --- 后续的绘制逻辑完全不变 ---

            EditorGUI.BeginChangeCheck();
            _creationFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_creationFoldout, "默认创建设置");
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(CreationFoldoutKey, _creationFoldout);

            if (_creationFoldout) DrawCreationSettingsGroup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUI.BeginChangeCheck();
            _appearanceFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_appearanceFoldout, "默认外观设置");
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(AppearanceFoldoutKey, _appearanceFoldout);

            if (_appearanceFoldout) DrawAppearanceSettingsGroup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUI.BeginChangeCheck();
            _previewFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_previewFoldout, "预览与依赖设置");
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(PreviewFoldoutKey, _previewFoldout);
            if (_previewFoldout) DrawPreviewSettingsGroup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUI.BeginChangeCheck();
            _operationsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_operationsFoldout, "场景操作列表");
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(OperationsFoldoutKey, _operationsFoldout);
            if (_operationsFoldout) DrawOperationsSettingsGroup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUI.BeginChangeCheck();
            _registryFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_registryFoldout, "策略注册中心 (PathStrategyRegistry)");
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(RegistryFoldoutKey, _registryFoldout);
            if (_registryFoldout) DrawRegistrySettingsGroup();
            EditorGUILayout.EndFoldoutHeaderGroup();

            // 仅在有改动时才保存，避免每帧触发导入导致“载入资源”循环
            bool applied = _settingsObj.ApplyModifiedProperties();
            if (applied)
            {
                EditorUtility.SetDirty(PathToolSettings.Instance);
                // 不再在 OnGUI 中立刻 SaveAssets，避免频繁导入造成卡顿。
                // 改为标记待保存，并提供显式按钮触发保存。
                _hasPendingSave = true;
            }

            // 待保存提示与显式保存按钮
            if (_hasPendingSave)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox("设置已变更，尚未写入磁盘。点击下方按钮或执行 Ctrl+S 以保存。", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("立即保存到磁盘", GUILayout.Width(160)))
                {
                    // 仅保存受影响的资产，避免全局导入引发卡顿
#if UNITY_2020_3_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(PathToolSettings.Instance);
#else
                    AssetDatabase.SaveAssets();
#endif
                    _hasPendingSave = false;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawCreationSettingsGroup()
        {
            EditorGUILayout.PropertyField(_settingsObj.FindProperty("defaultObjectName"), new GUIContent("默认对象名称"));
            EditorGUILayout.PropertyField(_settingsObj.FindProperty("defaultLineLength"), new GUIContent("默认线段长度"));
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("集中所有资产到 Settings", GUILayout.Width(200)))
            {
                ConsolidateAllAssets();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAppearanceSettingsGroup()
        {
            var profileProp = _settingsObj.FindProperty("defaultPathProfile");
            EditorGUILayout.PropertyField(profileProp, new GUIContent("默认路径 Profile"));

            if (profileProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("必须指定一个默认 PathProfile！", MessageType.Error);
                if (GUILayout.Button("快速创建并指定"))
                {
                    CreateAndAssignDefaultProfile(profileProp);
                }
            }
        }

        private void DrawPreviewSettingsGroup()
        {
            var matProp = _settingsObj.FindProperty("previewMaterialTemplate");
            var alphaProp = _settingsObj.FindProperty("previewAlpha");
            EditorGUILayout.PropertyField(matProp, new GUIContent("Preview Material Template"));
            EditorGUILayout.PropertyField(alphaProp, new GUIContent("Preview Alpha"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("依赖工厂 (注入)", EditorStyles.miniBoldLabel);
            var genFactoryProp = _settingsObj.FindProperty("previewGeneratorFactory");
            var heightFactoryProp = _settingsObj.FindProperty("heightProviderFactory");
            var matMgrFactoryProp = _settingsObj.FindProperty("previewMaterialManagerFactory");
            EditorGUILayout.PropertyField(genFactoryProp, new GUIContent("Preview Generator Factory"));
            EditorGUILayout.PropertyField(heightFactoryProp, new GUIContent("Height Provider Factory"));
            EditorGUILayout.PropertyField(matMgrFactoryProp, new GUIContent("Material Manager Factory"));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("创建默认工厂资产"))
                {
                    CreateDefaultFactories(genFactoryProp, heightFactoryProp, matMgrFactoryProp);
                }
                if (GUILayout.Button("定位并打开工厂文件夹"))
                {
                    string dir = "Assets/__temp/MrPathV2.2/Editor/Factories";
                    System.IO.Directory.CreateDirectory(dir);
                    EditorUtility.RevealInFinder(dir);
                }
            }
        }

        private void DrawOperationsSettingsGroup()
        {
            var opsProp = _settingsObj.FindProperty("operations");
            EditorGUILayout.LabelField("场景工具操作按钮来源", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("拖拽 PathTerrainOperation 资产到下方插槽，或点击‘扫描并填充’自动收集 Settings/Operations 下的操作资产。场景右下角的工具面板将按操作的 order 字段排序显示。", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描并填充", GUILayout.Width(120)))
            {
                FillOperationsFromAssets(opsProp);
            }
            if (GUILayout.Button("清空列表", GUILayout.Width(100)))
            {
                ClearArray(opsProp);
                _settingsObj.ApplyModifiedProperties();
            }
            if (GUILayout.Button("打开操作文件夹", GUILayout.Width(140)))
            {
                string dir = "Assets/__temp/MrPathV2.2/Settings/Operations";
                System.IO.Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
            EditorGUILayout.EndHorizontal();

            if (opsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("当前列表为空：点击‘扫描并填充’或‘添加插槽’以配置。", MessageType.Warning);
            }

            for (int i = 0; i < opsProp.arraySize; i++)
            {
                var elem = opsProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(elem, new GUIContent($"操作 {i + 1}"));
                if (GUILayout.Button("上移", GUILayout.MaxWidth(42)) && i > 0)
                {
                    opsProp.MoveArrayElement(i, i - 1);
                }
                if (GUILayout.Button("下移", GUILayout.MaxWidth(42)) && i < opsProp.arraySize - 1)
                {
                    opsProp.MoveArrayElement(i, i + 1);
                }
                if (GUILayout.Button("删除", GUILayout.MaxWidth(48)))
                {
                    opsProp.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加插槽", GUILayout.Width(100)))
            {
                opsProp.InsertArrayElementAtIndex(opsProp.arraySize);
                var e = opsProp.GetArrayElementAtIndex(opsProp.arraySize - 1);
                e.objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void CreateDefaultFactories(SerializedProperty genFactoryProp, SerializedProperty heightFactoryProp, SerializedProperty matMgrFactoryProp)
        {
            string dir = "Assets/__temp/MrPathV2.2/Settings/Resources";
            System.IO.Directory.CreateDirectory(dir);

            // 预览生成器工厂
            if (genFactoryProp.objectReferenceValue == null)
            {
                var gen = ScriptableObject.CreateInstance<DefaultPreviewGeneratorFactory>();
                string path = System.IO.Path.Combine(dir, "Default Preview Generator Factory.asset");
                AssetDatabase.CreateAsset(gen, path);
                genFactoryProp.objectReferenceValue = gen;
            }

            // 高度提供者工厂
            if (heightFactoryProp.objectReferenceValue == null)
            {
                var hp = ScriptableObject.CreateInstance<DefaultHeightProviderFactory>();
                string path = System.IO.Path.Combine(dir, "Default Height Provider Factory.asset");
                AssetDatabase.CreateAsset(hp, path);
                heightFactoryProp.objectReferenceValue = hp;
            }

            // 材质管理器工厂
            if (matMgrFactoryProp.objectReferenceValue == null)
            {
                var mm = ScriptableObject.CreateInstance<DefaultPreviewMaterialManagerFactory>();
                string path = System.IO.Path.Combine(dir, "Default Preview Material Manager Factory.asset");
                AssetDatabase.CreateAsset(mm, path);
                matMgrFactoryProp.objectReferenceValue = mm;
            }

            _settingsObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(PathToolSettings.Instance);
#if UNITY_2020_3_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(PathToolSettings.Instance);
#else
            AssetDatabase.SaveAssets();
#endif
            EditorUtility.DisplayDialog("工厂创建", "已创建默认工厂资产并绑定到设置。", "确定");
        }

        private void CreateAndAssignDefaultProfile(SerializedProperty profileProp)
        {
            var profile = ScriptableObject.CreateInstance<PathProfile>();
            string path = "Assets/__temp/MrPathV2.2/Settings/Profiles/DefaultPathProfile.asset";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(profile, path);

            profileProp.objectReferenceValue = profile;
            _settingsObj.ApplyModifiedProperties();

            EditorGUIUtility.PingObject(profile);
        }

        private void DrawRegistrySettingsGroup()
        {
            var registry = PathStrategyRegistry.Instance;
            string targetPath = "Assets/__temp/MrPathV2.2/Settings/Resources/PathStrategyRegistry.asset";
            bool existsAtTarget = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(targetPath) != null;

            EditorGUILayout.LabelField("注册表资产位置", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(existsAtTarget ? $"已集中存放：{targetPath}" : "未在 Settings/Resources 发现注册表资产，可在下方创建/修复。", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建/修复到 Settings/Resources"))
            {
                CreateOrRepairRegistryAsset(targetPath);
            }
            if (GUILayout.Button("扫描并填充"))
            {
                ScanAndFillRegistryEntries(createMissing:false);
            }
            if (GUILayout.Button("扫描填充并创建缺失"))
            {
                ScanAndFillRegistryEntries(createMissing:true);
            }
            if (registry != null && GUILayout.Button("定位并打开"))
            {
                Selection.activeObject = registry;
                EditorGUIUtility.PingObject(registry);
            }
            EditorGUILayout.EndHorizontal();

            // 简化：避免重复的编辑入口，仅在注册表资产 Inspector 中进行具体编辑
            if (registry != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("策略条目的具体编辑请在 PathStrategyRegistry 资产的 Inspector 中进行。此处仅提供创建/扫描与定位。", MessageType.None);
            }
        }

        /// <summary>
        /// 扫描项目中的 PathStrategy 资产并填充注册表；可选地为缺失类型创建默认策略资产。
        /// </summary>
        private void ScanAndFillRegistryEntries(bool createMissing)
        {
            var registry = PathStrategyRegistry.Instance;
            if (registry == null)
            {
                EditorUtility.DisplayDialog("未发现注册表", "请先创建或修复 PathStrategyRegistry 资产到 Settings/Resources。", "确定");
                return;
            }

            // 建立目标目录
            string strategiesDir = "Assets/__temp/MrPathV2.2/Settings/Strategies";
            System.IO.Directory.CreateDirectory(strategiesDir);

            var rso = new SerializedObject(registry);
            var entriesProp = rso.FindProperty("_strategyEntries");

            // 先读取现有类型集合，避免重复
            var existingTypes = new System.Collections.Generic.HashSet<CurveType>();
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var e = entriesProp.GetArrayElementAtIndex(i);
                var typeProp = e.FindPropertyRelative("type");
                existingTypes.Add((CurveType)typeProp.enumValueIndex);
            }

            // 扫描所有 PathStrategy 资产
            var guids = AssetDatabase.FindAssets($"t:{nameof(PathStrategy)}");
            var loadedStrategies = new System.Collections.Generic.Dictionary<System.Type, PathStrategy>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var strat = AssetDatabase.LoadAssetAtPath<PathStrategy>(path);
                if (strat == null) continue;
                loadedStrategies[strat.GetType()] = strat;
            }

            // 用于按类型创建缺失资产
            PathStrategy EnsureAsset<T>() where T : PathStrategy
            {
                var t = typeof(T);
                if (loadedStrategies.TryGetValue(t, out var exist)) return exist;

                // 创建缺失资产
                var asset = ScriptableObject.CreateInstance<T>();
                var fileName = typeof(T).Name + ".asset";
                var targetPath = System.IO.Path.Combine(strategiesDir, fileName).Replace("\\", "/");
                AssetDatabase.CreateAsset(asset, targetPath);
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(asset);
#else
                AssetDatabase.SaveAssets();
#endif
                loadedStrategies[t] = asset;
                return asset;
            }

            // 逐个曲线类型检查与填充
            void AddOrUpdate(CurveType type, System.Type implType)
            {
                // 查找现有条目
                int foundIndex = -1;
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var e = entriesProp.GetArrayElementAtIndex(i);
                    var typeProp = e.FindPropertyRelative("type");
                    if ((CurveType)typeProp.enumValueIndex == type) { foundIndex = i; break; }
                }

                // 获取策略实例（已存在于磁盘或可选择性创建）
                PathStrategy strat = null;
                if (loadedStrategies.TryGetValue(implType, out var got))
                {
                    strat = got;
                }
                else if (createMissing)
                {
                    // 运行时泛型创建
                    var method = GetType().GetMethod(nameof(EnsureAsset), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var generic = method.MakeGenericMethod(implType);
                    strat = (PathStrategy)generic.Invoke(this, null);
                }

                if (strat == null) return; // 不创建时缺失则跳过

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

            // 已支持的枚举类型与实现类的映射
            AddOrUpdate(CurveType.Bezier, typeof(BezierStrategy));
            AddOrUpdate(CurveType.CatmullRom, typeof(CatmullRomStrategy));

            if (rso.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(registry);
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(registry);
#endif
            }

            EditorUtility.DisplayDialog("策略扫描", createMissing ? "已扫描并创建缺失策略资产，注册表已更新。" : "已扫描并填充注册表。", "确定");
        }

        

        private void CreateOrRepairRegistryAsset(string targetPath)
        {
            // 查找现有资产
            var guids = AssetDatabase.FindAssets($"t:{nameof(PathStrategyRegistry)}");
            PathStrategyRegistry asset = null;
            string currentPath = null;
            if (guids != null && guids.Length > 0)
            {
                currentPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                asset = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(currentPath);
            }

            // 若存在且路径不一致，则移动到目标位置
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath));
            if (asset != null && currentPath != targetPath)
            {
                AssetDatabase.MoveAsset(currentPath, targetPath);
                asset = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(targetPath);
            }

            // 若不存在则创建新资产（不进行硬编码策略绑定）
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PathStrategyRegistry>();
                AssetDatabase.CreateAsset(asset, targetPath);
                // 仅保存新建的注册表资产
#if UNITY_2020_3_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(asset);
#else
                AssetDatabase.SaveAssets();
#endif
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }


        private void ConsolidateAllAssets()
        {
            string root = "Assets/__temp/MrPathV2.2/Settings";
            string profilesDir = System.IO.Path.Combine(root, "Profiles").Replace("\\", "/");
            string operationsDir = System.IO.Path.Combine(root, "Operations").Replace("\\", "/");
            string strategiesDir = System.IO.Path.Combine(root, "Strategies").Replace("\\", "/");
            string resourcesDir = System.IO.Path.Combine(root, "Resources").Replace("\\", "/");

            System.IO.Directory.CreateDirectory(profilesDir);
            System.IO.Directory.CreateDirectory(operationsDir);
            System.IO.Directory.CreateDirectory(strategiesDir);
            System.IO.Directory.CreateDirectory(resourcesDir);

            // 使用批量编辑减少导入次数
            AssetDatabase.StartAssetEditing();
            try
            {
                // 移动 PathProfile
                var profileGuids = AssetDatabase.FindAssets($"t:{nameof(PathProfile)}");
                foreach (var guid in profileGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith(profilesDir))
                    {
                        var fileName = System.IO.Path.GetFileName(path);
                        var targetPath = System.IO.Path.Combine(profilesDir, fileName).Replace("\\", "/");
                        AssetDatabase.MoveAsset(path, targetPath);
                    }
                }

                // 移动 PathTerrainOperation
                var opGuids = AssetDatabase.FindAssets($"t:{nameof(PathTerrainOperation)}");
                foreach (var guid in opGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith(operationsDir))
                    {
                        var fileName = System.IO.Path.GetFileName(path);
                        var targetPath = System.IO.Path.Combine(operationsDir, fileName).Replace("\\", "/");
                        AssetDatabase.MoveAsset(path, targetPath);
                    }
                }

                // 策略资产集中（若存在）
                var strategyGuids = AssetDatabase.FindAssets($"t:{nameof(PathStrategy)}");
                foreach (var guid in strategyGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith(strategiesDir))
                    {
                        var fileName = System.IO.Path.GetFileName(path);
                        var targetPath = System.IO.Path.Combine(strategiesDir, fileName).Replace("\\", "/");
                        AssetDatabase.MoveAsset(path, targetPath);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // 注册表集中
            string registryTarget = System.IO.Path.Combine(resourcesDir, "PathStrategyRegistry.asset").Replace("\\", "/");
            CreateOrRepairRegistryAsset(registryTarget);

            // 不再自动修复或绑定引用，交由用户在面板中拖拽指定，避免硬编码加载。

            EditorUtility.DisplayDialog("集中完成", "已将相关资产集中到 Settings，并修复默认引用。", "确定");
        }

        // --- 辅助方法：操作列表扫描与数组维护 ---
        private void FillOperationsFromAssets(SerializedProperty opsProp)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(PathTerrainOperation)}");
            var found = new System.Collections.Generic.List<PathTerrainOperation>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var op = AssetDatabase.LoadAssetAtPath<PathTerrainOperation>(path);
                if (op != null) found.Add(op);
            }

            // 现有集合去重
            var set = new System.Collections.Generic.HashSet<PathTerrainOperation>();
            for (int i = 0; i < opsProp.arraySize; i++)
            {
                var cur = opsProp.GetArrayElementAtIndex(i).objectReferenceValue as PathTerrainOperation;
                if (cur != null) set.Add(cur);
            }
            foreach (var op in found)
            {
                set.Add(op);
            }

            // 按 order 排序并回写
            var list = new System.Collections.Generic.List<PathTerrainOperation>(set);
            list.Sort((a, b) =>
            {
                int ao = a != null ? a.order : 0;
                int bo = b != null ? b.order : 0;
                return ao.CompareTo(bo);
            });

            // 重置数组并填充
            ClearArray(opsProp);
            for (int i = 0; i < list.Count; i++)
            {
                opsProp.InsertArrayElementAtIndex(i);
                var e = opsProp.GetArrayElementAtIndex(i);
                e.objectReferenceValue = list[i];
            }
            _settingsObj.ApplyModifiedProperties();
        }

        private void ClearArray(SerializedProperty arrayProp)
        {
            while (arrayProp.arraySize > 0)
            {
                arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);
            }
        }
    }
}