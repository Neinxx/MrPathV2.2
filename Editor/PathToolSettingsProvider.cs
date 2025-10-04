using UnityEditor;
using UnityEngine;

public class PathToolSettingsProvider : SettingsProvider
{
    public PathToolSettingsProvider (string path, SettingsScope scope = SettingsScope.Project) : base (path, scope) { }

    public override void OnGUI (string searchContext)
    {
        PathToolSettings settings = PathToolSettings.Instance;

        // 使用 BeginChangeCheck 来检测用户是否修改了设置
        EditorGUI.BeginChangeCheck ();

        // 绘制UI元素
        EditorGUILayout.LabelField ("默认创建设置", EditorStyles.boldLabel);
        settings.defaultObjectName = EditorGUILayout.TextField ("默认对象名", settings.defaultObjectName);
        settings.defaultLineLength = EditorGUILayout.FloatField ("默认线段长度", settings.defaultLineLength);
        settings.defaultCurveType = (PathCreator.CurveType) EditorGUILayout.EnumPopup ("默认曲线类型", settings.defaultCurveType);
        EditorGUILayout.Space ();
        EditorGUILayout.LabelField ("预览设置", EditorStyles.boldLabel);
        settings.terrainPreviewTemplate = (Material) EditorGUILayout.ObjectField ("地形预览模板材质", settings.terrainPreviewTemplate, typeof (Material), false);

        // 如果检测到修改，则保存设置
        if (EditorGUI.EndChangeCheck ())
        {
            PathToolSettings.Save ();
        }
    }

    /// <summary>
    /// 这个静态方法是关键，它将我们的设置提供者注册到Project Settings窗口中。
    /// </summary>
    [SettingsProvider]
    public static SettingsProvider CreatePathToolSettingsProvider ()
    {
        var provider = new PathToolSettingsProvider ("Project/MrPath");
        return provider;
    }
}
