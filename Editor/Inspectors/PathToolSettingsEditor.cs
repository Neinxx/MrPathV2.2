using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    [CustomEditor(typeof(PathToolSettings))]
    public class PathToolSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "此资产是 MrPath 的全局配置源。为避免重复入口带来的混淆，请在 Project Settings -> MrPath Settings 中进行编辑。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("打开 Project Settings", GUILayout.Width(180)))
            {
                SettingsService.OpenProjectSettings("Project/MrPath Settings");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("当前配置 (只读)", EditorStyles.boldLabel);

            // 将默认 Inspector 以只读方式展示，便于快速核对现有值
            EditorGUI.BeginDisabledGroup(true);
            base.OnInspectorGUI();
            EditorGUI.EndDisabledGroup();
        }
    }
}