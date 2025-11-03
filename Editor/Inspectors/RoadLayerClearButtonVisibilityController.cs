using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.Inspectors
{
    /// <summary>
    /// 基于透明度与 pickingMode 控制 Clear 按钮的显隐与交互。
    /// 当 Layer/Mask 为空（显示未选择）时：opacity=0 且 pickingMode=Ignore；否则恢复。
    /// 非侵入式：不修改原有 Inspector 逻辑。
    /// </summary>
    [InitializeOnLoad]
    public static class RoadLayerClearButtonVisibilityController
    {
        private static double _lastCheck;
        private const double CheckInterval = 0.4; // 秒

        static RoadLayerClearButtonVisibilityController()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastCheck < CheckInterval) return; // 提前返回，防抖
            _lastCheck = now;

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var wnd in windows)
            {
                var root = wnd.rootVisualElement;
                if (root == null) continue;
                UpdateClearButtons(root);
            }
        }

        private static void UpdateClearButtons(VisualElement root)
        {
            var clearButtons = root.Query<Button>(name: "ClearButton").ToList();
            if (clearButtons == null || clearButtons.Count == 0) return;

            foreach (var btn in clearButtons)
            {
                var row = btn.parent; // 行容器
                if (row == null) continue;

                var nameLabel = row.Q<Label>("LayerName") ?? row.Q<Label>("MaskName");
                if (nameLabel == null) continue;

                var hasSelection = !string.IsNullOrEmpty(nameLabel.text) && nameLabel.text != "未选择";

                // 核心：透明度与拾取模式控制
                btn.style.opacity = hasSelection ? 1f : 0f;
                btn.pickingMode = hasSelection ? PickingMode.Position : PickingMode.Ignore;

                // 可选：避免 Tab 焦点或键盘激活
                btn.focusable = hasSelection;
            }
        }
    }
}
