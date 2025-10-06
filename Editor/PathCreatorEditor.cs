using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(PathCreator))]
public class PathCreatorEditor : Editor
{
    #region 核心缓存
    private PathCreator _targetCreator;
    private Editor _profileEmbeddedEditor;
    private SerializedObject _profileSO;
    // 关键修改：存储 Profile 级别的本地折叠状态（替代原 isExpanded 字段）
    private bool _profileLocalExpanded = true;
    private readonly HashSet<string> _handledPropNames = new() { "profile" };
    #endregion

    #region 生命周期
    private void OnEnable()
    {
        _targetCreator = target as PathCreator;
        if (_targetCreator?.profile != null)
        {
            InitProfileReferences(_targetCreator.profile);
        }
    }

    private void OnDisable()
    {
        if (_profileEmbeddedEditor != null)
        {
            DestroyImmediate(_profileEmbeddedEditor);
            _profileEmbeddedEditor = null;
        }
        _profileSO = null;
    }
    #endregion

    public override void OnInspectorGUI()
    {
        if (_targetCreator == null)
        {
            DrawInvalidTargetUI();
            return;
        }

        serializedObject.Update();

        DrawCreatorCoreUI();

        if (_targetCreator.profile != null)
        {
            DrawEmbeddedProfileUI();
        }
        else
        {
            DrawProfileMissingUI();
        }

        serializedObject.ApplyModifiedProperties();
    }

    #region 核心UI绘制（关键修改：使用本地折叠状态）
    private void DrawInvalidTargetUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("路径对象无效", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("当前选择的对象不包含 PathCreator 组件，或组件已被销毁。", EditorStyles.wordWrappedLabel);
        }
    }

    private void DrawCreatorCoreUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("路径控制器", EditorStyles.largeLabel);

            SerializedProperty prop = serializedObject.GetIterator();
            prop.NextVisible(true);

            while (prop.NextVisible(false))
            {
                if (!_handledPropNames.Contains(prop.name))
                {
                    EditorGUILayout.PropertyField(prop, true);
                }
                else if (prop.name == "profile")
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(prop.displayName, GUILayout.Width(EditorGUIUtility.labelWidth));
                        EditorGUI.BeginChangeCheck();
                        Object newProfile = EditorGUILayout.ObjectField(
                            _targetCreator.profile,
                            typeof(PathProfile),
                            false,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)
                        );
                        if (EditorGUI.EndChangeCheck())
                        {
                            prop.objectReferenceValue = newProfile;
                            if (newProfile is PathProfile newPathProfile)
                            {
                                InitProfileReferences(newPathProfile);
                                // 切换 Profile 时重置本地折叠状态为展开
                                _profileLocalExpanded = true;
                            }
                            _targetCreator.NotifyPathChanged(PathChangeType.ProfileAssigned, -1);
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
        }

        EditorGUILayout.Space(8);
    }

    private void DrawProfileMissingUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(EditorGUIUtility.IconContent("Warning@2x"), GUILayout.Width(24));
                EditorGUILayout.LabelField("未指定路径配置文件（Profile）", EditorStyles.boldLabel);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("路径需要 Profile 来定义外观和生成规则，请先创建或指定一个 Profile 资产。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("快速创建默认 Profile", GUILayout.Width(200)))
                {
                    CreateDefaultProfile();
                }
            }
        }
    }

    private void DrawEmbeddedProfileUI()
    {
        // 安全校验：Profile 序列化对象无效时重新初始化
        if (_profileSO == null || _profileSO.targetObject != _targetCreator.profile)
        {
            InitProfileReferences(_targetCreator.profile);
        }
        if (_profileSO == null) return;

        _profileSO.Update();

        using (new EditorGUILayout.VerticalScope("Box"))
        {
            // 关键修改：使用本地折叠状态（_profileLocalExpanded），不再从 Profile 读取
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _profileLocalExpanded = EditorGUILayout.Foldout(
                    _profileLocalExpanded,
                    new GUIContent("路径配置文件（Profile）", EditorGUIUtility.IconContent("settings").image),
                    true,
                    EditorStyles.foldoutHeader
                );
                if (EditorGUI.EndChangeCheck())
                {
                    // 无需保存到 Profile（本地状态仅当前编辑器会话有效）
                    SceneView.RepaintAll();
                }

                // 打开资产快捷按钮
                if (GUILayout.Button(EditorGUIUtility.IconContent("settings"), GUILayout.Width(20), GUILayout.Height(20)))
                {
                    EditorGUIUtility.PingObject(_targetCreator.profile);
                }
            }

            // 展开状态下绘制嵌入编辑器
            if (_profileLocalExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.Space(4);
                EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.5f, 0.5f, 0.5f, 0.2f));
                EditorGUILayout.Space(6);

                // 绘制嵌入的 Profile 编辑器（复用原逻辑）
                EditorGUI.BeginChangeCheck();
                {
                    if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != _targetCreator.profile)
                    {
                        _profileEmbeddedEditor = CreateEditor(_targetCreator.profile);
                    }
                    _profileEmbeddedEditor?.OnInspectorGUI();
                }
                if (EditorGUI.EndChangeCheck())
                {
                    _profileSO.ApplyModifiedProperties();
                    _targetCreator.EnsurePathImplementationMatchesProfile(true);
                    _targetCreator.NotifyPathChanged(PathChangeType.ProfileAssigned, -1);
                    SceneView.RepaintAll();
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }
        }

        _profileSO.ApplyModifiedProperties();
    }
    #endregion

    #region 辅助方法（移除无效字段查找）
    private void InitProfileReferences(PathProfile profile)
    {
        if (profile == null)
        {
            _profileSO = null;
            return;
        }

        // 仅初始化序列化对象，不再查找 isExpanded 字段（该字段在 PathLayer 中）
        _profileSO = new SerializedObject(profile);
    }

    private void CreateDefaultProfile()
    {
        PathProfile defaultProfile = CreateInstance<PathProfile>();
        defaultProfile.name = "Default_PathProfile";

        // 配置默认参数（与 PathLayer 的 isExpanded 无关，无需处理）
        defaultProfile.generationPrecision = 0.5f;
        defaultProfile.snapToTerrain = true;
        defaultProfile.snapStrength = 1f;
        defaultProfile.heightSmoothness = 10;

        // 添加默认图层（自动包含 PathLayer 的 isExpanded 字段，默认 true）
        if (defaultProfile.layers.Count == 0)
        {
            defaultProfile.layers.Add(new PathTool.Data.PathLayer
            {
                name = "Base_Layer",
                width = 4f,
                horizontalOffset = 0f,
                verticalOffset = 0.05f
                // PathLayer 构造时自动初始化 isExpanded = true
            });
        }

        // 保存资产
        string saveDir = "Assets/MrPath/Profiles";
        if (!System.IO.Directory.Exists(saveDir))
        {
            System.IO.Directory.CreateDirectory(saveDir);
        }
        string savePath = $"{saveDir}/{defaultProfile.name}.asset";

        AssetDatabase.CreateAsset(defaultProfile, savePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 自动赋值并刷新
        _targetCreator.profile = defaultProfile;
        serializedObject.FindProperty("profile").objectReferenceValue = defaultProfile;
        _profileLocalExpanded = true; // 新 Profile 默认展开

        EditorGUIUtility.PingObject(defaultProfile);
        Debug.Log($"[PathCreatorEditor] 默认 Profile 已创建：{savePath}");

        _targetCreator.NotifyPathChanged(PathChangeType.ProfileAssigned, -1);
        SceneView.RepaintAll();
    }
    #endregion

    #region 性能优化
    public override bool RequiresConstantRepaint()
    {
        // 仅当 Profile 展开且嵌入编辑器需要刷新时才重绘
        return _targetCreator?.profile != null
               && _profileLocalExpanded
               && (_profileEmbeddedEditor?.RequiresConstantRepaint() ?? false);
    }
    #endregion
}