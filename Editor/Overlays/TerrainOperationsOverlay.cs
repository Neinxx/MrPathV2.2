using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2._2.Editor.Inspectors;
using __temp.MrPathV2._2.Editor.Operations;
using __temp.MrPathV2._2.Editor.Settings;
using __temp.MrPathV2._2.Editor.Terrain;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Preview;
using __temp.MrPathV2._2.Runtime.Settings;

using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;


namespace __temp.MrPathV2._2.Editor.Overlays
{
    [Overlay(typeof(SceneView), id: "MrPath.TerrainOperationsOverlay", displayName: "地形操作")]
    public class TerrainOperationsOverlay : Overlay
    {
        private PathEditorContext _ctx;
        private MrPathTerrainOperations _terrainOpsConfig;
        private VisualElement _root;
        private VisualElement _content;
        private DropdownField _backendDropdown;
        private Toggle _gpuPreviewToggle;
        private const string GpuPreviewPrefKey = "MrPath_EnableGpuPreview";

        public override VisualElement CreatePanelContent()
        {
            _terrainOpsConfig = MrPathProjectSettings.GetOrCreateSettings().terrainOperations;
          //  _root = UIResourceLoader.LoadAndClone<TerrainOperationsOverlay>(); 
            _root = UIResourceLoader.LoadAndCloneByName(nameof(TerrainOperationsOverlay));
            InitializeBackendDropdown();

            InitializeGpuPreviewToggle();

            _content = _root.Q<VisualElement>("operationsContainer");

            var refreshBtn = _root.Q<Button>("refreshButton");
            refreshBtn?.RegisterCallback<ClickEvent>(_ => ClearEmptySplatAlphaUnderPath());

            Selection.selectionChanged += OnSelectionChanged;
            RefreshContent();
            UpdateVisibility();

            return _root;
        }

        private void InitializeBackendDropdown()
        {
            _backendDropdown = _root.Q<DropdownField>("CpuOrGpu");
            if (_backendDropdown == null) return;

            _backendDropdown.choices = new List<string> { "CPU", "GPU" };

            var settings = MrPathProjectSettings.GetOrCreateSettings();
            var backend = settings.advancedSettings?.paintingBackend ??
                          PaintTerrainCommand.PaintingBackend.CPU_Job_TwoPass;
            _backendDropdown.index = backend == PaintTerrainCommand.PaintingBackend.GPU_Compute ? 1 : 0;

            _backendDropdown.RegisterValueChangedCallback(evt =>
            {
                var newBackend = evt.newValue == "GPU"
                    ? PaintTerrainCommand.PaintingBackend.GPU_Compute
                    : PaintTerrainCommand.PaintingBackend.CPU_Job_TwoPass;

                var advanced = MrPathProjectSettings.GetOrCreateSettings().advancedSettings;
                if (advanced == null || advanced.paintingBackend == newBackend) return;

                Undo.RecordObject(advanced, "Change Painting Backend");
                advanced.paintingBackend = newBackend;
                EditorUtility.SetDirty(advanced);
            });
        }

        private void RefreshContent()
        {
            if (_content == null) return;
            _content.Clear();

            if (_terrainOpsConfig == null)
            {
                _content.Add(new Label("未找到 Terrain Operations 配置"));
                return;
            }

            var ops = _terrainOpsConfig.operations;
            if (ops == null || ops.Length == 0)
            {
                var btn = new Button(() => SettingsService.OpenProjectSettings("Project/MrPath"))
                {
                    text = "配置地形操作"
                };
                btn.AddToClassList("unity-toolbar-button");
                _content.Add(btn);
                return;
            }

            var validOps = ops.Where(op => op != null).OrderBy(op => op.order);
            foreach (var op in validOps)
            {
                var btn = new ToolbarButton(() => ExecuteOperation(op))
                {
                    text = !string.IsNullOrEmpty(op.displayName) ? op.displayName : op.name
                };
                btn.AddToClassList("terrain-op-button");

                if (op.icon != null)
                {
                    btn.style.backgroundImage = new StyleBackground(op.icon);
                    var color = op.buttonColor != default ? op.buttonColor : Color.white;
                    btn.style.unityBackgroundImageTintColor = new StyleColor(color);
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
            var go = Selection.activeGameObject;
            if (go == null)
            {
                HideOverlay();
                return;
            }

            var creator = go.GetComponent<PathCreator>();
            if (creator == null)
            {
                HideOverlay();
                return;
            }

            displayed = true;

            if (_ctx?.Target == creator) return;

            _ctx?.Dispose();
            _ctx = new PathEditorContext(creator);
            RefreshContent();
        }

        private void HideOverlay()
        {
            displayed = false;
            _ctx?.Dispose();
            _ctx = null;
        }

        private void ExecuteOperation(PathTerrainOperation op)
        {
            if (_ctx == null) return;
            if (_ctx.Target == null) return;
            if (!op.CanExecute(_ctx.Target)) return;

            var profile = _ctx.Target.profile;
            if (profile == null || PathStrategyRegistry.Instance.GetStrategy(profile.curveType) == null)
            {
                ShowConfigError();
                return;
            }

            var cmd = op.CreateCommand(_ctx.Target, _ctx.HeightProvider);
            if (cmd == null) return;

            var previewMesh = _ctx.PreviewGenerator?.PreviewMesh;
            if (previewMesh != null)
            {
                var b = previewMesh.bounds;
                if (b.size.x > 0 && b.size.z > 0)
                {
                    cmd.SetPreviewBoundsXZ(new Vector4(b.min.x, b.min.z, b.max.x, b.max.z));
                }
            }

            _ = _ctx.TerrainHandler.ExecuteAsync(cmd, _ => { });
        }

        private static void ShowConfigError()
        {
            EditorUtility.DisplayDialog(
                "配置错误",
                "路径缺少 Profile 或未找到对应的路径策略 (Strategy)。\n请检查 Path Creator 的 Profile 字段以及 Project/MrPath 设置中的高级设置。",
                "确定"
            );
        }

        private void ClearEmptySplatAlphaUnderPath()
        {
            var pathRect = GetPathBounds();
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
            {
                ShowNotification("未找到场景中的 Terrain 对象");
                return;
            }

            var clearedCount = 0;
            foreach (var terrain in terrains)
            {
                var td = terrain.terrainData;
                if (td == null || td.alphamapLayers == 0) continue;

                if (pathRect.HasValue)
                {
                    var pos = terrain.GetPosition();
                    var tRect = new Rect(pos.x, pos.z, td.size.x, td.size.z);
                    if (!tRect.Overlaps(pathRect.Value)) continue;
                }

                var res = td.alphamapResolution;
                var layers = td.alphamapLayers;
                var alpha = td.GetAlphamaps(0, 0, res, res);

                if (!IsAlphaMapEmpty()) continue;

                td.SetAlphamaps(0, 0, new float[res, res, layers]);
                EditorUtility.SetDirty(td);
                clearedCount++;

                
                bool IsAlphaMapEmpty()
                {
                    for (var y = 0; y < res; y++)
                    for (var x = 0; x < res; x++)
                    {
                        var sum = 0f;
                        for (var l = 0; l < layers; l++)
                        {
                            sum += alpha[y, x, l];
                        }

                        if (sum > 1e-4f) return false;
                    }

                    return true;
                }
            }

            var msg = clearedCount > 0
                ? $"已清理 {clearedCount} 个 Terrain 的空白 SplatAlpha"
                : "未找到需要清理的 Terrain";
            ShowNotification(msg);
            Debug.Log($"[TerrainOperations] {msg}");
        }

        private Rect? GetPathBounds()
        {
            var mesh = _ctx?.PreviewGenerator?.PreviewMesh;
            if (mesh == null) return null;

            var b = mesh.bounds;
            if (b.size.x <= 0 || b.size.z <= 0) return null;

            return new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        }

        private void ShowNotification(string message)
        {
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message));
        }

        private void InitializeGpuPreviewToggle()
        {
            // 插入一个 Toggle 控件到 Toolbar 区域（与 CPU/GPU 下拉同级）
            _gpuPreviewToggle = new Toggle("实时GPU预览")
            {
                value = EditorPrefs.GetBool(GpuPreviewPrefKey, true)
            };
            _gpuPreviewToggle.style.marginLeft = 6;
            _gpuPreviewToggle.RegisterValueChangedCallback(evt =>
            {
                PreviewMaterialManager.EnableGpuPreview = evt.newValue;
                EditorPrefs.SetBool(GpuPreviewPrefKey, evt.newValue);

                // Force SceneView to refresh so the preview updates immediately
                UnityEditor.SceneView.RepaintAll();
            });

            // 初始同步
            PreviewMaterialManager.EnableGpuPreview = _gpuPreviewToggle.value;

            var toolbar = _root.Q<VisualElement>("toolbarContainer") ?? _root; // fallback
            toolbar.Add(_gpuPreviewToggle);
        }

        public void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            _ctx?.Dispose();
            _ctx = null;
        }
    }
}