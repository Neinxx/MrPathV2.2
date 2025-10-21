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
                // Update button label
                refreshBtn.text = "清除空白 SplatAlpha";
                refreshBtn.clicked += () =>
                {
                    ClearEmptySplatAlphaUnderPath();
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

        private void ClearEmptySplatAlphaUnderPath()
        {
            // Determine path bounds in XZ plane if available
            Rect pathRect = default;
            bool hasPathBounds = false;
            if (_ctx?.PreviewGenerator?.PreviewMesh != null)
            {
                var b = _ctx.PreviewGenerator.PreviewMesh.bounds;
                pathRect = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
                hasPathBounds = b.size.x > 0f && b.size.z > 0f;
            }

            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
            {
                Debug.LogWarning("[TerrainOperations] 未找到场景中的 Terrain 对象");
                return;
            }

            int clearedCount = 0;
            foreach (var terrain in terrains)
            {
                var td = terrain.terrainData;
                if (td == null) continue;

                // 路径包围盒过滤
                if (hasPathBounds)
                {
                    var pos = terrain.GetPosition();
                    var tRect = new Rect(pos.x, pos.z, td.size.x, td.size.z);
                    if (!tRect.Overlaps(pathRect))
                    {
                        continue; // Not affected by current path
                    }
                }

                int res = td.alphamapResolution;
                int layers = td.alphamapLayers;
                if (layers == 0) continue;

                var alpha = td.GetAlphamaps(0, 0, res, res);
                bool isEmpty = true;
                for (int y = 0; y < res && isEmpty; y++)
                {
                    for (int x = 0; x < res && isEmpty; x++)
                    {
                        float sum = 0f;
                        for (int l = 0; l < layers; l++)
                        {
                            sum += alpha[y, x, l];
                        }
                        if (sum > 0.0001f)
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                }

                if (isEmpty)
                {
                    var zeros = new float[res, res, layers]; // default zero-initialized
                    td.SetAlphamaps(0, 0, zeros);
                    clearedCount++;
                    EditorUtility.SetDirty(td);
                }
            }

            // Feedback
            var msg = clearedCount > 0 ? $"已清理 {clearedCount} 个 Terrain 的空白 SplatAlpha" : "未找到需要清理的 Terrain";
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(msg));
            Debug.Log($"[TerrainOperations] {msg}");
        }
    }
}