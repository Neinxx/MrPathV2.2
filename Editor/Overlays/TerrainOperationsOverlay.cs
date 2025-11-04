using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.Inspectors;
using __temp.MrPathV2.Editor.Operations;
using __temp.MrPathV2.Editor.Settings;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Settings;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.Overlays
{
    [Overlay(typeof(SceneView), "MrPath.TerrainOperationsOverlay", "Modify Terrain Operations")]
    public class TerrainOperationsOverlay : Overlay
    {

        // GPU预览是否启用
        // private const string GpuPreviewPrefKey = "MrPath_EnableGpuPreview";


        private const string ElCpuOrGpu = "CpuOrGpu";
        private const string ElOperationsContainer = "operationsContainer";
        // GPU预览是否启用
        // private const string ElGpuPreviewToggle = "gpuPreviewToggle";

        private static readonly string[] BackendChoices =
        {
            "CPU", "GPU","Auto"
        };

        /// <summary>
        ///    存储所有操作按钮的列表，用于批量处理状态
        /// </summary>
        private readonly List<Button> _operationButtons = new List<Button>();
        private DropdownField _backendDropdown;
        private VisualElement _content;
        private PathEditorContext _ctx;
        //GPU预览是否启用
        // private Toggle _gpuPreviewToggle;

        /// <summary>
        ///    标记是否正在执行操作
        /// </summary>
        private bool _isExecutingOperation;
        private MrPathProjectSettings _projectSettings;
        private VisualElement _root;
        private MrPathTerrainOperations _terrainOpsConfig;

        public override VisualElement CreatePanelContent()
        {
            _projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            _terrainOpsConfig = _projectSettings.terrainOperations;
            //_root = UIResourceLoader.LoadAndCloneByName(nameof(TerrainOperationsOverlay));
            _root = UIResourceLoader.LoadAndClone<TerrainOperationsOverlay>();
            if (_root == null)
            {
                return new Label("Overlay UI 閸旂姾娴囨径杈Е");
            }
            // <--- 从UXML中获取元素引用
            _backendDropdown = _root.Q<DropdownField>(ElCpuOrGpu);
            // GPU预览是否启用
            // _gpuPreviewToggle = _root.Q<Toggle>(ElGpuPreviewToggle);
            _content = _root.Q<VisualElement>(ElOperationsContainer);

            InitializeBackendDropdown(); // <--- 初始化后端选择下拉框
            // GPU预览是否启用
            // InitializeGpuPreviewToggleFromUxml(); // <--- 从UXML初始化GPU预览开关

            // <--- 移除旧的刷新按钮
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
            // <--- 检查下拉框是否在CreatePanelContent中正确获取
            if (_backendDropdown == null) return;

            _backendDropdown.choices = BackendChoices.ToList();

            var backend = _projectSettings?.advancedSettings?.paintingBackend ??
                          PaintTerrainCommand.PaintingBackend.CPUJobTwoPass;
            _backendDropdown.index = backend == PaintTerrainCommand.PaintingBackend.GPUCompute ? 1 : (backend == PaintTerrainCommand.PaintingBackend.Auto ? 2 : 0);

            _backendDropdown.UnregisterValueChangedCallback(OnBackendChanged);
            _backendDropdown.RegisterValueChangedCallback(OnBackendChanged);
        }

        private void OnBackendChanged(ChangeEvent<string> evt)
        {
            var newBackend = evt.newValue == "GPU"
                ? PaintTerrainCommand.PaintingBackend.GPUCompute
                : (evt.newValue == "Auto"
                    ? PaintTerrainCommand.PaintingBackend.Auto
                    : PaintTerrainCommand.PaintingBackend.CPUJobTwoPass);

            var advanced = _projectSettings != null ? _projectSettings.advancedSettings : null;
            if (advanced == null || advanced.paintingBackend == newBackend) return;

            Undo.RecordObject(advanced, "Change Painting Backend");
            advanced.paintingBackend = newBackend;
            EditorUtility.SetDirty(advanced);
        }

        // GPU预览是否启用的初始化方法
        // private void InitializeGpuPreviewToggleFromUxml() { ... }

        /// <summary>
        /// 刷新UI内容
        /// 根据当前配置重新创建所有操作按钮
        /// </summary>
        private void RefreshContent()
        {
            // 1. 守卫语句
            if (_content == null) return;

            _content.Clear();
            _operationButtons.Clear();

            // 2. 检查地形操作配置是否存在
            if (_terrainOpsConfig == null)
            {
                _content.Add(new Label("未找到地形操作配置"));
                return;
            }

            var ops = _terrainOpsConfig.operations;

            // 3. 检查操作列表是否为空
            if (ops == null || ops.Length == 0)
            {
                _content.Add(CreateConfigRedirectButton());
                return;
            }

            // 4. 为每个操作创建按钮并添加到UI

            // 使用LINQ查询过滤、排序并创建按钮
            // 转换为foreach循环以提高可读性
            var newButtons = ops
                .Where(op => op != null) // 过滤掉null
                .OrderBy(op => op.order)  // 按顺序排序
                .Select(CreateOperationButton) // 创建按钮
                .ToList();

            // 5. 更新按钮列表并添加到UI

            // 添加到按钮列表
            _operationButtons.AddRange(newButtons);

            // 添加到可视化元素
            foreach (var btn in newButtons)
            {
                _content.Add(btn);
            }
        }

        /// <summary>
        /// 创建一个跳转到配置页面的按钮
        /// </summary>
        private static Button CreateConfigRedirectButton()
        {
            var btn = new Button(() => SettingsService.OpenProjectSettings("Project/MrPath"))
            {
                text = "配置地形操作"
            };
            btn.AddToClassList("unity-toolbar-button");
            return btn;
        }

        /// <summary>
        /// 为地形操作创建一个工具栏按钮
        /// </summary>
        private Button CreateOperationButton(PathTerrainOperation op)
        {
            var buttonText = !string.IsNullOrEmpty(op.displayName) ? op.displayName : op.name;

            // 1. 创建按钮
            var btn = new ToolbarButton
            {
                text = buttonText,
                userData = buttonText
            };

            // 2. 注册点击事件
            // 使用RegisterCallback而非直接赋值clickable，以便在lambda中捕获btn
            btn.RegisterCallback<ClickEvent>(_ => ExecuteOperation(op, btn));

            // 3. 设置按钮状态和样式
            btn.SetEnabled(!_isExecutingOperation);
            btn.AddToClassList("terrain-op-button");

            // 4. 应用操作特定样式
            ApplyOperationStyles(btn, op);

            return btn;
        }

        /// <summary>
        /// 应用操作特定的样式到按钮
        /// </summary>
        private static void ApplyOperationStyles(Button btn, PathTerrainOperation op)
        {
            if (op.icon == null) return;
            // 通过设置style.backgroundImage应用图标
            btn.style.backgroundImage = op.icon;

            var color = op.buttonColor != default ? op.buttonColor : Color.white;
            btn.style.unityBackgroundImageTintColor = color;
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

        /// <summary>
        /// 执行选定的地形操作
        /// </summary>
        private async void ExecuteOperation(PathTerrainOperation op, Button clickedButton)
        {
            try
            {
                // 1. 守卫语句 - 检查是否可以执行操作
                if (!CanExecuteOperation(op))
                {
                    return;
                }

                // 2. 使用try/catch/finally确保UI状态正确恢复
                // 注意：async void应谨慎使用，此处用于事件处理
                try
                {
                    // 3. 更新UI为执行状态
                    SetUIStateExecuting(clickedButton);

                    // 4. 创建操作命令
                    var cmd = op.CreateCommand(_ctx.Target, _ctx.HeightProvider);
                    if (cmd == null) return; // 如果命令为空，在finally中恢复UI

                    // 5. 设置预览边界（如果可用）
                    if (TryGetPreviewBoundsXZ(out var previewBounds))
                    {
                        cmd.SetPreviewBoundsXZ(previewBounds);
                    }

                    // 6. 执行操作
                    await _ctx.TerrainHandler.ExecuteAsync(cmd, null);
                }
                catch (Exception e)
                {
                    // 记录操作执行过程中的异常
                    // 使用LogException而非Log以捕获完整堆栈信息
                    ErrorHandler.LogException(e);
                }
                finally
                {
                    // 7. 恢复UI状态
                    RestoreUIState(clickedButton);
                }
            }
            catch (Exception e)
            {
                ErrorHandler.LogException(e);
            }
        }

        /// <summary>
        /// 检查是否可以执行操作
        /// </summary>
        private bool CanExecuteOperation(PathTerrainOperation op)
        {
            // 基本检查
            if (_isExecutingOperation || _ctx == null || !_ctx.Target || !op.CanExecute(_ctx.Target))
            {
                return false;
            }

            // 检查路径策略
            var profile = _ctx.Target.profile;
            if (profile && PathStrategyRegistry.Instance.GetStrategy(profile.curveType) != null) return true;
            ShowConfigError();
            return false;
        }


        /// <summary>
        /// 设置UI为执行状态
        /// </summary>
        private void SetUIStateExecuting(Button clickedButton)
        {
            _isExecutingOperation = true;
            if (clickedButton != null)
            {
                clickedButton.text = "Progress..";
            }
            SetOperationButtonsEnabled(false);
        }

        /// <summary>
        /// 恢复UI状态
        /// </summary>
        private void RestoreUIState(Button clickedButton)
        {
            _isExecutingOperation = false;
            SetOperationButtonsEnabled(true);

            // [BUG FIX] 使用userData恢复原始文本
            // （防止RefreshContent重置时丢失原始文本）
            if (clickedButton is { userData: string originalText })
            {
                clickedButton.text = originalText;
            }
        }

        /// <summary>
        /// 尝试获取预览网格的XZ边界
        /// 用于限制地形操作仅在预览网格范围内执行
        /// </summary>
        private bool TryGetPreviewBoundsXZ(out Vector4 bounds)
        {
            bounds = Vector4.zero;

            // 检查预览生成器是否存在
            if (_ctx.PreviewGenerator == null) return false;

            var previewMesh = _ctx.PreviewGenerator.PreviewMesh;
            if (previewMesh == null) return false;

            var b = previewMesh.bounds;

            // 检查边界是否有效
            if (b.size.x > 0 && b.size.z > 0)
            {
                // 存储XZ平面的最小和最大值
                bounds = new Vector4(b.min.x, b.min.z, b.max.x, b.max.z);
                return true;
            }

            return false;
        }

        private static void ShowConfigError()
        {
            EditorUtility.DisplayDialog(
                "配置错误",
                "所选路径配置文件缺少有效的策略。请确保路径创建器已分配配置文件，并在Project/MrPath设置中正确配置了策略。",
                "确定"
            );
        }

        /// <summary>
        /// 设置所有操作按钮的启用状态
        /// </summary>
        /// <param name="enabled">按钮是否启用</param>
        private void SetOperationButtonsEnabled(bool enabled)
        {
            foreach (var btn in _operationButtons)
            {
                // 同时设置pickingMode和focusable以完全禁用交互
                // 跳过空引用
                if (btn == null) continue;

                btn.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
                btn.focusable = enabled;

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


