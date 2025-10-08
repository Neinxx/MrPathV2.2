using UnityEditor;
using UnityEngine;

/// <summary>
/// 【金身不坏版】MrPath 工具的项目设置提供器。
/// - 解决了因编辑器“轮回”(Domain Reload)导致的UI状态丢失问题。
/// </summary>
public class PathToolSettingsProvider : SettingsProvider
{
    private SerializedObject _settingsObj;

    // 折叠状态的持久化
    private bool _creationFoldout = true;
    private bool _appearanceFoldout = true;
    private const string CreationFoldoutKey = "MrPath_CreationFoldout";
    private const string AppearanceFoldoutKey = "MrPath_AppearanceFoldout";

    public PathToolSettingsProvider(string path, SettingsScope scope) : base(path, scope)
    {
        keywords = GetSearchKeywordsFromPath("MrPath;Path;路径");
    }

    [SettingsProvider]
    public static SettingsProvider Register() => new("Project/MrPath Settings", SettingsScope.Project);

    // OnActivate 不再负责初始化 SerializedObject，只负责读取UI状态
    public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
    {
        _creationFoldout = SessionState.GetBool(CreationFoldoutKey, true);
        _appearanceFoldout = SessionState.GetBool(AppearanceFoldoutKey, true);
    }

    public override void OnGUI(string searchContext)
    {
        // --- 【核心修正】固魂之术 ---
        // 在每次 OnGUI 时，都确保 _settingsObj 是有效的。
        if (_settingsObj == null || _settingsObj.targetObject == null)
        {
            var instance = PathToolSettings.Instance;
            if (instance == null)
            {
                EditorGUILayout.HelpBox("无法加载或创建 PathToolSettings 资产！", MessageType.Error);
                return;
            }
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

        _settingsObj.ApplyModifiedProperties();
    }

    private void DrawCreationSettingsGroup()
    {
        EditorGUILayout.PropertyField(_settingsObj.FindProperty("defaultObjectName"), new GUIContent("默认对象名称"));
        EditorGUILayout.PropertyField(_settingsObj.FindProperty("defaultLineLength"), new GUIContent("默认线段长度"));
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

    private void CreateAndAssignDefaultProfile(SerializedProperty profileProp)
    {
        var profile = ScriptableObject.CreateInstance<PathProfile>();
        string path = "Assets/Settings/DefaultPathProfile.asset";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();

        profileProp.objectReferenceValue = profile;
        _settingsObj.ApplyModifiedProperties();

        EditorGUIUtility.PingObject(profile);
    }
}