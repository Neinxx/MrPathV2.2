using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 负责在 SceneView 中绘制地形操作面板，并触发命令执行。
    /// 不直接持有服务，依赖注入 PathEditorContext。
    /// </summary>
    public class TerrainOperationsPanel
    {
        private readonly PathEditorContext _ctx;

        public TerrainOperationsPanel(PathEditorContext ctx)
        {
            _ctx = ctx;
        }

        public void Draw()
        {
            Handles.BeginGUI();

            SceneView currentSceneView = SceneView.currentDrawingSceneView;
            if (currentSceneView == null)
            {
                Handles.EndGUI();
                return;
            }

            var uiSettings = MrPathProjectSettings.GetOrCreateSettings().sceneUISettings;
            if (uiSettings == null)
            {
                Handles.EndGUI();
                return;
            }

            Rect windowRect = new Rect(
                currentSceneView.position.width - uiSettings.sceneUiWindowWidth - uiSettings.sceneUiRightMargin,
                currentSceneView.position.height - uiSettings.sceneUiWindowHeight - uiSettings.sceneUiBottomMargin,
                uiSettings.sceneUiWindowWidth,
                uiSettings.sceneUiWindowHeight
            );

            GUILayout.Window(GetHashCode(), windowRect, id =>
            {
                GUILayout.Label("MrPathV2.31", EditorStyles.boldLabel);

                var terrainOps = MrPathProjectSettings.GetOrCreateSettings().terrainOperations;
                var ops = terrainOps?.operations;

                if (ops != null && ops.Length > 0)
                {
                    System.Array.Sort(ops, (a, b) => a.order.CompareTo(b.order));
                    bool isAnyOperationRunning = _ctx.IsApplyingHeight || _ctx.IsApplyingPaint;

                    foreach (var op in ops)
                    {
                        if (op == null) continue;

                        // 判断当前按钮对应的操作是否正在运行
                        bool isThisOpRunning = (op.CreateCommand(_ctx.Target, null) is FlattenTerrainCommand && _ctx.IsApplyingHeight) ||
                                              (op.CreateCommand(_ctx.Target, null) is PaintTerrainCommand && _ctx.IsApplyingPaint);

                        using (new EditorGUI.DisabledScope(isAnyOperationRunning))
                        {
                            GUI.backgroundColor = isThisOpRunning ? Color.yellow : op.buttonColor;
                            string buttonText = isThisOpRunning ? "正在执行..." : op.displayName;

                            if (GUILayout.Button(new GUIContent(buttonText, op.icon), GUILayout.Height(26)))
                            {
                                ExecuteOperation(op);
                            }
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("配置地形操作", GUILayout.Height(22)))
                    {
                        SettingsService.OpenProjectSettings("Project/MrPath");
                    }
                }

                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.8f, 0.95f, 0.6f);
                if (GUILayout.Button("刷新地形缓存", GUILayout.Height(22)))
                {
                    _ctx.HeightProvider?.MarkAsDirty();
                    _ctx.MarkDirty();
                    currentSceneView.ShowNotification(new GUIContent("地形缓存已刷新"));
                }

                GUI.DragWindow(new Rect(0, 0, 10000, 20));

            }, "Mr.Path");

            Handles.EndGUI();
            GUI.backgroundColor = Color.white;
        }

        public void ExecuteOperation(PathTerrainOperation op)
        {
            if (_ctx.Target == null || !op.CanExecute(_ctx.Target)) return;

            if (_ctx.Target.profile == null || PathStrategyRegistry.Instance.GetStrategy(_ctx.Target.profile.curveType) == null)
            {
                EditorUtility.DisplayDialog("配置错误", "路径缺少 Profile 或未找到对应的路径策略 (Strategy)。\n请检查 Path Creator 的 Profile 字段以及 Project/MrPath 设置中的高级设置。", "确定");
                return;
            }

            var cmd = op.CreateCommand(_ctx.Target, _ctx.HeightProvider);
            if (cmd == null) return;

            if (cmd is FlattenTerrainCommand)
                _ = _ctx.TerrainHandler.ExecuteAsync(cmd, b => _ctx.IsApplyingHeight = b);
            else if (cmd is PaintTerrainCommand)
                _ = _ctx.TerrainHandler.ExecuteAsync(cmd, b => _ctx.IsApplyingPaint = b);
            else
                _ = _ctx.TerrainHandler.ExecuteAsync(cmd, b => { _ctx.IsApplyingHeight = b; _ctx.IsApplyingPaint = b; });
        }
    }
}