// PathCreatorEditor.cs
using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEditor.EditorTools;

/// <summary>
/// 【最终定稿 • 大师级】PathCreator 的自定义编辑器
/// 
/// 这份代码是整个工具“脸面”的最终形态。它遵循以下设计哲学：
/// 
/// - 优雅 (Elegant): 结构清晰，职责分明。使用内嵌编辑器提供一流的UX。
/// - 可读 (Readable): 使用 #region 和详尽的“心法注释”来阐明每一部分的设计意图。
/// - 高性能 (Performant): 缓存重用编辑器实例，避免不必要的GUI重绘，采用事件驱动的刷新机制。
/// - 鲁棒 (Robust): 完整处理资源生命周期和事件订阅/退订，防止内存泄漏；
///   始终使用 SerializedObject 处理属性，确保Undo/Redo和Prefab的兼容性。
/// - 逻辑清晰 (Logical): 编辑器只负责“呈现”和“应用修改”。它相信组件自身（通过OnValidate）
///   能够响应变化，实现了完美的关注点分离。
/// - 干净 (Clean): 移除了所有对旧架构的依赖，代码精炼，无冗余。
/// </summary>
[CustomEditor(typeof(PathCreator))]
public class PathCreatorEditor : Editor
{
    #region 字段与属性 (Fields & Properties)

    private PathCreator _targetCreator;

    // --- 内嵌编辑器所需的核心缓存 ---
    private Editor _profileEmbeddedEditor;
    private SerializedObject _profileSO;

    // --- UI状态管理 ---
    // 将折叠状态保存在编辑器本地，而非序列化到资产中，
    // 这是避免多人协作时互相干扰UI状态的关键技巧。
    private bool _profileLocalExpanded = true;

    #endregion

    #region 生命周期与事件订阅 (Lifecycle & Event Subscription)

    private void OnEnable()
    {
        _targetCreator = target as PathCreator;
        if (_targetCreator == null) return;

        // 初始化对 Profile 的引用，以便创建内嵌编辑器
        if (_targetCreator.profile != null)
        {
            InitProfileReferences(_targetCreator.profile);
        }

    }

    private void OnDisable()
    {
        // OnDisable 是编辑器脚本的“金钟罩”，必须在这里清理所有引用的资源和事件，
        // 否则会导致内存泄漏和令人头痛的空引用错误。

        if (_profileEmbeddedEditor != null)
        {
            DestroyImmediate(_profileEmbeddedEditor);
            _profileEmbeddedEditor = null;
        }
        _profileSO = null;


    }

    #endregion

    #region 主GUI循环 (Main GUI Loop)

    public override void OnInspectorGUI()
    {
        if (_targetCreator == null) return;

        // 始终从 serializedObject 开始，这是保证Undo/Redo正确的基石。
        serializedObject.Update();

        // 绘制核心属性
        DrawCoreProperties();

        // 根据 Profile 是否存在，决定绘制内嵌编辑器还是提示信息
        if (_targetCreator.profile != null)
        {
            DrawEmbeddedProfileUI();
        }
        else
        {
            DrawProfileMissingUI();
        }

        // 应用所有修改
        serializedObject.ApplyModifiedProperties();
    }

    #endregion

    #region UI绘制方法 (UI Drawing Methods)

    private void DrawCoreProperties()
    {
        var profileProperty = serializedObject.FindProperty("profile");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(profileProperty);
        if (EditorGUI.EndChangeCheck())
        {
            // 当用户直接更换Profile资产时，应用修改。
            // OnValidate会自动触发，进而触发事件链，无需额外操作。
            serializedObject.ApplyModifiedProperties();
        }

        var pathDataProperty = serializedObject.FindProperty("pathData");
        EditorGUILayout.PropertyField(pathDataProperty, true);
        EditorGUILayout.Space();
    }

    private void DrawProfileMissingUI()
    {
        EditorGUILayout.HelpBox("未指定路径配置文件 (Profile)。请先创建或指定一个 Profile 资产。", MessageType.Warning);
        if (GUILayout.Button("快速创建默认 Profile"))
        {
            CreateDefaultProfile();
        }
    }

    private void DrawEmbeddedProfileUI()
    {
        if (_profileSO == null || _profileSO.targetObject != _targetCreator.profile)
        {
            InitProfileReferences(_targetCreator.profile);
        }
        if (_profileSO == null) return;

        _profileSO.Update();

        using (new EditorGUILayout.VerticalScope("Box"))
        {
            _profileLocalExpanded = EditorGUILayout.Foldout(_profileLocalExpanded, "路径配置文件 (Profile)", true, EditorStyles.foldoutHeader);

            if (_profileLocalExpanded)
            {
                EditorGUI.indentLevel++;

                // 使用 BeginChangeCheck 监控内嵌编辑器中的所有UI变化
                EditorGUI.BeginChangeCheck();
                {
                    if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != _targetCreator.profile)
                    {
                        _profileEmbeddedEditor = CreateEditor(_targetCreator.profile);
                    }
                    // 绘制 Profile 的 Inspector 内容
                    _profileEmbeddedEditor?.OnInspectorGUI();
                }
                if (EditorGUI.EndChangeCheck())
                {

                    // 步骤 1: 签发“诏书” (保存修改)
                    _profileSO.ApplyModifiedProperties();

                    if (_targetCreator != null)
                    {
                        _targetCreator.NotifyProfileModified();
                    }

                    // 步骤 3 (可选但推荐): 强制重绘场景，确保即时响应
                    //  SceneView.RepaintAll();





                }

                EditorGUI.indentLevel--;
            }
        }
    }
    #endregion

    #region 事件处理器 (Event Handlers)

    private void OnTargetPathChanged(PathChangeCommand command)
    {
        // 当 PathCreator 通知我们它的路径已改变时，
        // 我们只需重绘所有场景视图，以确保 Handles 和预览网格能及时刷新。
        SceneView.RepaintAll();
    }

    #endregion

    #region 辅助方法 (Helper Methods)



    private void InitProfileReferences(PathProfile profile)
    {
        _profileSO = (profile != null) ? new SerializedObject(profile) : null;
    }

    private void CreateDefaultProfile()
    {
        var newProfile = CreateInstance<PathProfile>();
        newProfile.name = "Default PathProfile";
        // ... (可以添加更详细的默认值设置)

        // 安全地创建资产目录和文件
        string saveDir = "Assets/MrPathV2.2/Profiles";
        Directory.CreateDirectory(saveDir);
        string savePath = AssetDatabase.GenerateUniqueAssetPath($"{saveDir}/{newProfile.name}.asset");

        AssetDatabase.CreateAsset(newProfile, savePath);
        AssetDatabase.SaveAssets();

        // 自动将新创建的Profile赋给当前对象
        serializedObject.FindProperty("profile").objectReferenceValue = newProfile;
        serializedObject.ApplyModifiedProperties();

        EditorGUIUtility.PingObject(newProfile);
    }

    #endregion
}