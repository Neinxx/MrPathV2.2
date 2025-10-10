// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Inspectors/PathProcessorEditor.cs
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// [已废弃] PathProcessor 的自定义编辑器。
    /// 这个编辑器的唯一作用是通知用户此组件已被弃用，并引导他们使用新的工作流程。
    /// </summary>
    [CustomEditor(typeof(PathProcessor))]
    public class PathProcessorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // --- 这是最重要的部分：一个清晰的废弃通知 ---
            EditorGUILayout.HelpBox(
                "此组件 (Path Processor) 已被废弃。\n\n" +
                "所有地形操作功能现已直接集成到 Path Creator 的场景视图UI中。\n\n" +
                "请选中带有 Path Creator 组件的物体，然后在场景视图（Scene View）的右下角找到工具面板来应用地形修改。",
                MessageType.Warning);

            EditorGUILayout.Space(10);

            // 提供一个一键移除此废弃组件的按钮，极大提升用户体验
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("安全移除此组件", GUILayout.Height(30)))
            {
                // 使用 Undo.DestroyObjectImmediate 来确保此操作可以被撤销
                Undo.DestroyObjectImmediate(target);
            }
            GUI.backgroundColor = Color.white;
        }

        // 移除所有旧的逻辑，如 OnEnable, OnDisable, ExecuteCommandAsync 等
        // 因为这个编辑器不再执行任何实际功能
    }
}