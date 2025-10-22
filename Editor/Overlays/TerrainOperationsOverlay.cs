using System;
using __temp.MrPathV2._2.Editor.Inspectors;
using __temp.MrPathV2._2.Editor.Operations;
using __temp.MrPathV2._2.Editor.Settings;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Settings;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// add

namespace __temp.MrPathV2._2.Editor.Overlays
{
    /// <summary>
    /// Terrain operations overlay. Replaces the legacy GUILayout window implementation (TerrainOperationsPanel)
    /// while keeping the original functional logic. The overlay is only displayed when a PathCreator is selected
    /// (similar to original requirement of "PathCreate" selected). The overlay adopts Toolbar UI style and docks
    /// to the bottom-right of the SceneView by default.
    /// </summary>
    [Overlay(
        typeof(SceneView),
        id: "MrPath.TerrainOperationsOverlay",
        displayName: "地形操作"
    )
    ]
    public class TerrainOperationsOverlay : Overlay
    {
        private PathEditorContext _ctx;
        private MrPathTerrainOperations _terrainOpsConfig;
        private VisualElement _root;
        // private Toolbar _toolbar; // remove unused field
        private VisualElement _content;

        // Unity calls this once when the overlay is created.
        public override VisualElement CreatePanelContent()
        {
            _terrainOpsConfig = MrPathProjectSettings.GetOrCreateSettings().terrainOperations;

            // Load UXML template
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/__temp/MrPathV2.2/Editor/Overlays/TerrainOperationsOverlay.uxml");
            if (visualTree == null)
            {
                Debug.LogError("Failed to load TerrainOperationsOverlay.uxml. Check the file path.");
                return new Label("[Missing UXML template]");
            }

            _root = visualTree.CloneTree();
            _content = _root.Q<VisualElement>("operationsContainer");
            var refreshBtn = _root.Q<Button>("refreshButton");
            if (refreshBtn != null)
            {
                refreshBtn.clicked += () =>
                {
                    _ctx?.HeightProvider?.MarkAsDirty();
                    _ctx?.MarkDirty();
                    SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("地形缓存已刷新"));
                };
            }

            // Subscribe to selection change events
            Selection.selectionChanged += OnSelectionChanged;

            // Populate operations
            RefreshContent();

            // Initial visibility determination
            UpdateVisibility();

            return _root;
        }

        private void RefreshContent()
        {
            if (_content == null)
            {
                return;
            }
            _content.Clear();

            if (_terrainOpsConfig == null)
            {
                _content.Add(new Label("未找到 Terrain Operations 配置"));
                return;
            }

            var ops = _terrainOpsConfig.operations;
            if (ops == null || ops.Length == 0)
            {
                var btnCfg = new Button(() => { SettingsService.OpenProjectSettings("Project/MrPath"); })
                {
                    text = "配置地形操作"
                };
                btnCfg.AddToClassList("unity-toolbar-button");
                _content.Add(btnCfg);
                return;
            }

            Array.Sort(ops, (a, b) => a.order.CompareTo(b.order));

            foreach (var op in ops)
            {
                if (op == null) continue;

                var btn = new ToolbarButton(() => ExecuteOperation(op))
                {
                    text = op.displayName ?? op.name
                };

                btn.AddToClassList("terrain-op-button");

                // icon support remains
                if (op.icon != null)
                {
                    btn.style.backgroundImage = new StyleBackground(op.icon);
                  
                }

                // colour override via C# (optional). Could also add colour classes.
                if (op.buttonColor != default)
                {
                    btn.style.unityBackgroundImageTintColor = new StyleColor(op.buttonColor);
                }

                _content.Add(btn);
            }
        }

        private void OnSelectionChanged()
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            bool shouldShow = false;
            PathCreator selectedCreator = null;

            if (Selection.activeGameObject != null)
            {
                selectedCreator = Selection.activeGameObject.GetComponent<PathCreator>();
                shouldShow = selectedCreator != null;
            }

            this.displayed = shouldShow;

            if (shouldShow)
            {
                if (_ctx == null || _ctx.Target != selectedCreator)
                {
                    _ctx?.Dispose();
                    if (selectedCreator != null)
                    {
                        _ctx = new PathEditorContext(selectedCreator);
                    }
                }
                // ensure buttons reflect new context state
                RefreshContent();
            }
            else
            {
                _ctx?.Dispose();
                _ctx = null;
            }
        }

       

        private void ExecuteOperation(PathTerrainOperation op)
        {
            if (_ctx == null) return;
            if (_ctx.Target == null || !op.CanExecute(_ctx.Target)) return;

            if (_ctx.Target.profile == null || PathStrategyRegistry.Instance.GetStrategy(_ctx.Target.profile.curveType) == null)
            {
                EditorUtility.DisplayDialog("配置错误", "路径缺少 Profile 或未找到对应的路径策略 (Strategy)。\n请检查 Path Creator 的 Profile 字段以及 Project/MrPath 设置中的高级设置。", "确定");
                return;
            }

            var cmd = op.CreateCommand(_ctx.Target, _ctx.HeightProvider);
            if (cmd == null) return;

            // 将预览网格包围盒信息传递给命令
            var previewMesh = _ctx.PreviewGenerator?.PreviewMesh;
            if (previewMesh != null)
            {
                var b = previewMesh.bounds;
                if (b.size.x > 0 && b.size.z > 0)
                {
                    var boundsXZ = new Vector4(b.min.x, b.min.z, b.max.x, b.max.z);
                    cmd.SetPreviewBoundsXZ(boundsXZ);
                }
            }

            _ = _ctx.TerrainHandler.ExecuteAsync(cmd, _ => { });
        }
    }
}