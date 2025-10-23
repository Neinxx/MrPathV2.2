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
    [Overlay(typeof(SceneView), id: "MrPath.TerrainOperationsOverlay", displayName: "Modife Terrain Operations")]
    public class TerrainOperationsOverlay : Overlay
    {
        private PathEditorContext _ctx;
        private MrPathTerrainOperations _terrainOpsConfig;
        private MrPathProjectSettings _projectSettings;
        private VisualElement _root;
        private VisualElement _content;
        private DropdownField _backendDropdown;
        private Toggle _gpuPreviewToggle;

        private const string GpuPreviewPrefKey = "MrPath_EnableGpuPreview";


        private const string ElCpuOrGpu = "CpuOrGpu";
        private const string ElOperationsContainer = "operationsContainer";
        private const string ElGpuPreviewToggle = "gpuPreviewToggle";

        private static readonly string[] BackendChoices = { "CPU", "GPU" };

        /// <summary>
        /// 跟踪当前是否有地形操作正在异步执行
        /// </summary>
        private bool _isExecutingOperation = false;

        /// <summary>
        /// 存储所有操作按钮，以便统一启用/禁用
        /// </summary>
        private readonly List<Button> _operationButtons = new List<Button>();

        public override VisualElement CreatePanelContent()
        {
            _projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            _terrainOpsConfig = _projectSettings.terrainOperations;
            _root = UIResourceLoader.LoadAndCloneByName(nameof(TerrainOperationsOverlay));
            if (_root == null)
            {
                return new Label("Overlay UI 加载失败");
            }

            // <--- 更改：从 UXML 查询元素，而不是手动创建
            _backendDropdown = _root.Q<DropdownField>(ElCpuOrGpu);
            _gpuPreviewToggle = _root.Q<Toggle>(ElGpuPreviewToggle);
            _content = _root.Q<VisualElement>(ElOperationsContainer);

            InitializeBackendDropdown(); // <--- 更改：方法现在只负责注册回调和设置初始值
            InitializeGpuPreviewToggleFromUXML(); // <--- 更改：新方法，用于绑定 UXML 中的 Toggle

            // <--- 更改：移除了手动删除 "refreshButton" 的代码
            // var staleRefreshBtn = _root.Q<Button>("refreshButton");
            // staleRefreshBtn?.RemoveFromHierarchy();

            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
            RefreshContent();
            UpdateVisibility();

            return _root;
        }

        private void InitializeBackendDropdown()
        {
            // <--- 更改：移除了查询 (Q)，因为它已在 CreatePanelContent 中完成
            if (_backendDropdown == null) return;

            _backendDropdown.choices = BackendChoices.ToList();

            var backend = _projectSettings?.advancedSettings?.paintingBackend ??
                          PaintTerrainCommand.PaintingBackend.CPU_Job_TwoPass;
            _backendDropdown.index = backend == PaintTerrainCommand.PaintingBackend.GPU_Compute ? 1 : 0;

            _backendDropdown.UnregisterValueChangedCallback(OnBackendChanged);
            _backendDropdown.RegisterValueChangedCallback(OnBackendChanged);
        }

        private void OnBackendChanged(ChangeEvent<string> evt)
        {
            var newBackend = evt.newValue == "GPU"
                ? PaintTerrainCommand.PaintingBackend.GPU_Compute
                : PaintTerrainCommand.PaintingBackend.CPU_Job_TwoPass;

            var advanced = _projectSettings != null ? _projectSettings.advancedSettings : null;
            if (advanced == null || advanced.paintingBackend == newBackend) return;

            Undo.RecordObject(advanced, "Change Painting Backend");
            advanced.paintingBackend = newBackend;
            EditorUtility.SetDirty(advanced);
        }

        // <--- 更改：重命名并简化了 GpuPreviewToggle 的初始化
        private void InitializeGpuPreviewToggleFromUXML()
        {
            if (_gpuPreviewToggle == null)
            {
                Debug.LogWarning("TerrainOperationsOverlay: 未在 UXML 中找到 'gpuPreviewToggle' 元素。");
                return;
            }

            _gpuPreviewToggle.value = EditorPrefs.GetBool(GpuPreviewPrefKey, true);

            _gpuPreviewToggle.RegisterValueChangedCallback(evt =>
            {
                PreviewMaterialManager.EnableGpuPreview = evt.newValue;
                EditorPrefs.SetBool(GpuPreviewPrefKey, evt.newValue);

                // Force SceneView to refresh so the preview updates immediately
                UnityEditor.SceneView.RepaintAll();
            });

            // 初始同步
            PreviewMaterialManager.EnableGpuPreview = _gpuPreviewToggle.value;
        }


        private void RefreshContent()
        {

            if (_content == null) return;
            _content.Clear();
            _operationButtons.Clear();
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
                string originalText = !string.IsNullOrEmpty(op.displayName) ? op.displayName : op.name;
                Button btn = null;
                // 2. 创建按钮，lambda 捕获 btn 自身，传递给 ExecuteOperation
                btn = new ToolbarButton(() => ExecuteOperation(op, btn))
                {
                    text = originalText,
                    userData = originalText
                };
                btn.SetEnabled(!_isExecutingOperation);
                btn.AddToClassList("terrain-op-button");

                if (op.icon != null)
                {
                    btn.style.backgroundImage = new StyleBackground(op.icon);
                    var color = op.buttonColor != default ? op.buttonColor : Color.white;
                    btn.style.unityBackgroundImageTintColor = new StyleColor(color);
                }

                _content.Add(btn);
                _operationButtons.Add(btn);
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

        private void ExecuteOperation(PathTerrainOperation op, Button clickedButton)
        {
            if (_isExecutingOperation) return;
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

            _ = _ctx.TerrainHandler.ExecuteAsync(cmd, isApplying =>
            {
                _isExecutingOperation = isApplying;
                SetOperationButtonsEnabled(!isApplying);

                // <--- 更改：简化了回调逻辑
                // SetOperationButtonsEnabled(true) 会自动恢复所有按钮的原始文本。
                // 我们只需要在 "isApplying" 时设置 "执行中..." 文本即可。
                if (clickedButton != null && isApplying)
                {
                    clickedButton.text = "执行中...";
                }
            });
        }

        private static void ShowConfigError()
        {
            EditorUtility.DisplayDialog(
                "配置错误",
                "路径缺少 Profile 或未找到对应的路径策略 (Strategy)。\n请检查 Path Creator 的 Profile 字段以及 Project/MrPath 设置中的高级设置。",
                "确定"
            );
        }

        /// <summary>
        /// 统一设置所有地形操作按钮的可用状态，并在启用时恢复其原始文本。
        /// </summary>
        /// <param name="enabled">是否启用按钮</param>
        private void SetOperationButtonsEnabled(bool enabled)
        {
            foreach (var btn in _operationButtons)
            {
                btn.SetEnabled(enabled);


                if (enabled && btn.userData is string originalText)
                {
                    btn.text = originalText;
                }
            }
        }



        public void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            _ctx?.Dispose();
            _ctx = null;


            SetOperationButtonsEnabled(true);
            _isExecutingOperation = false;
        }
    }
}