// PathCreatorTool.cs
// 创建一个场景视图工具栏按钮，用于编辑 PathCreator。
// 当选中 PathCreator 时，会自动激活该工具。
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace MrPathV2.EditorTools
{
    [EditorTool("Path Creator Tool", typeof(PathCreator))]
    public class PathCreatorTool : EditorTool
    {
        private static GUIContent _iconContent;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_iconContent == null)
                {
                    // 使用 Unity 内置的 Animator 图标
                    _iconContent = EditorGUIUtility.IconContent("Animator Icon", "Path Creator Tool");
                }
                return _iconContent;
            }
        }

        // 当前工具不需要额外的 GUI，因为 PathCreatorEditor 已经处理了场景绘制。
        public override void OnToolGUI(EditorWindow window)
        {
            // Intentionally left blank.
        }

        // --- 自动激活逻辑 ---------------------------------------------------
        private void OnEnable()
        {
            Selection.selectionChanged += TryAutoActivate;
            // 不在此立即触发，等待下一帧 selectionChanged 或手动调用
            TryAutoActivate();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= TryAutoActivate;
        }

        private static void TryAutoActivate()
        {
            if (!HasPathCreatorInSelection())
                return;

            if (ToolManager.activeToolType == typeof(PathCreatorTool))
                return;

            // 延迟到下一帧调用，避免在当前回调栈中因选择状态变化而抛异常
            EditorApplication.delayCall += SafeActivate;
        }

        private static bool HasPathCreatorInSelection()
        {
            foreach (var obj in Selection.gameObjects)
            {
                if (obj != null && obj.GetComponent<PathCreator>() != null)
                    return true;
            }
            return false;
        }

        private static void SafeActivate()
        {
            try
            {
                if (HasPathCreatorInSelection())
                {
                    ToolManager.SetActiveTool(typeof(PathCreatorTool));
                }
            }
            catch (System.InvalidOperationException)
            {
                // 忽略由于 selection 状态变化导致的异常
            }
        }
    }
}
#endif