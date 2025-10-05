// 文件路径: Editor/Settings/PathToolSettingsProvider.cs
using UnityEditor;
using UnityEngine;

public class PathToolSettingsProvider : SettingsProvider
{
    private SerializedObject m_Settings;

    public PathToolSettingsProvider (string path, SettingsScope scope = SettingsScope.Project) : base (path, scope) { }

    public override void OnActivate (string searchContext, UnityEngine.UIElements.VisualElement rootElement)
    {
        // 当设置窗口被打开时，获取设置资产的序列化对象
        m_Settings = new SerializedObject (PathToolSettings.Instance);
    }

    public override void OnGUI (string searchContext)
    {
        if (m_Settings == null || m_Settings.targetObject == null)
            return;

        m_Settings.Update ();

        // 使用属性绘制，可以自动处理Undo/Redo和脏标记
        EditorGUILayout.LabelField ("默认创建设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField (m_Settings.FindProperty ("defaultObjectName"), new GUIContent ("默认对象名"));
        EditorGUILayout.PropertyField (m_Settings.FindProperty ("defaultLineLength"), new GUIContent ("默认线段长度"));
        EditorGUILayout.PropertyField (m_Settings.FindProperty ("defaultCurveType"), new GUIContent ("默认曲线类型"));

        EditorGUILayout.Space ();

        EditorGUILayout.LabelField ("默认外观", EditorStyles.boldLabel);
        // 【核心修改】将原来的材质字段改为PathProfile字段
        EditorGUILayout.PropertyField (m_Settings.FindProperty ("defaultPathProfile"), new GUIContent ("默认路径外观 (Profile)"));

        m_Settings.ApplyModifiedProperties ();
    }

    /// <summary>
    /// 将我们的设置提供者注册到Project Settings窗口中。
    /// </summary>
    [SettingsProvider]
    public static SettingsProvider CreatePathToolSettingsProvider ()
    {
        var provider = new PathToolSettingsProvider ("Project/MrPath Tool Settings");
        return provider;
    }
}
