using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Editor.Inspectors;
using MrPathV2.Editor.Operations;
using MrPathV2.Editor.Preview;
using MrPathV2.Editor.Settings;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Settings;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.Overlays
{
    [Overlay(typeof(SceneView), "MrPath.TerrainOperationsOverlay", "Modify Terrain Operations")]
    public class TerrainOperationsOverlay : Overlay
    {

        private const string GpuPreviewPrefKey = "MrPath_EnableGpuPreview";


        private const string ElCpuOrGpu = "CpuOrGpu";
        private const string ElOperationsContainer = "operationsContainer";
        private const string ElGpuPreviewToggle = "gpuPreviewToggle";

        private static readonly string[] BackendChoices =
        {
            "CPU", "GPU"
        };

        /// <summary>
        ///     存储所有操作按钮，以便统一启用/禁用
        /// </summary>
        private readonly List<Button> _operationButtons = new List<Button>();
        private DropdownField _backendDropdown;
        private VisualElement _content;
        private PathEditorContext _ctx;
        private Toggle _gpuPreviewToggle;

        /// <summary>
        ///     跟踪当前是否有地形操作正在异步执行
        /// </summary>
        private bool _isExecutingOperation;
        private MrPathProjectSettings _projectSettings;
        private VisualElement _root;
        private MrPathTerrainOperations _terrainOpsConfig;

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
            InitializeGpuPreviewToggleFromUxml(); // <--- 更改：新方法，用于绑定 UXML 中的 Toggle

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
                          PaintTerrainCommand.PaintingBackend.CPUJobTwoPass;
            _backendDropdown.index = backend == PaintTerrainCommand.PaintingBackend.GPUCompute ? 1 : 0;

            _backendDropdown.UnregisterValueChangedCallback(OnBackendChanged);
            _backendDropdown.RegisterValueChangedCallback(OnBackendChanged);
        }

        private void OnBackendChanged(ChangeEvent<string> evt)
        {
            var newBackend = evt.newValue == "GPU"
                ? PaintTerrainCommand.PaintingBackend.GPUCompute
                : PaintTerrainCommand.PaintingBackend.CPUJobTwoPass;

            var advanced = _projectSettings != null ? _projectSettings.advancedSettings : null;
            if (advanced == null || advanced.paintingBackend == newBackend) return;

            Undo.RecordObject(advanced, "Change Painting Backend");
            advanced.paintingBackend = newBackend;
            EditorUtility.SetDirty(advanced);
        }

        // <--- 更改：重命名并简化了 GpuPreviewToggle 的初始化
        private void InitializeGpuPreviewToggleFromUxml()
        {
            if (_gpuPreviewToggle == null)
            {
                ErrorHandler.LogWarning("TerrainOperationsOverlay: 未在 UXML 中找到 'gpuPreviewToggle' 元素。");
                return;
            }

            _gpuPreviewToggle.value = EditorPrefs.GetBool(GpuPreviewPrefKey, true);

            _gpuPreviewToggle.RegisterValueChangedCallback(evt =>
            {
                PreviewMaterialManager.EnableGpuPreview = evt.newValue;
                EditorPrefs.SetBool(GpuPreviewPrefKey, evt.newValue);

                // Force SceneView to refresh so the preview updates immediately
                SceneView.RepaintAll();
            });

            // 初始同步
            PreviewMaterialManager.EnableGpuPreview = _gpuPreviewToggle.value;
        }


      /// <summary>
/// 刷新地形操作按钮的UI内容。
/// 这是一个高层协调器，不处理具体的按钮创建逻辑。
/// </summary>
private void RefreshContent()
{
    // 1. 守卫条款 (Guard Clause)
    if (_content == null) return;

    _content.Clear();
    _operationButtons.Clear();

    // 2. 提前返回：处理配置丢失
    if (_terrainOpsConfig == null)
    {
        _content.Add(new Label("未找到 Terrain Operations 配置"));
        return;
    }

    var ops = _terrainOpsConfig.operations;

    // 3. 提前返回：处理配置为空
    if (ops == null || ops.Length == 0)
    {
        _content.Add(CreateConfigRedirectButton());
        return;
    }

    // 4. 核心逻辑：生成并添加操作按钮

    // 使用 LINQ 将“数据”转换为“UI元素”
    // 这比 foreach 循环更具声明性（更“优雅”）
    var newButtons = ops
        .Where(op => op) // 过滤掉 null 的 op
        .OrderBy(op => op.order)  // 按 order 排序
        .Select(CreateOperationButton) // 使用工厂方法创建按钮
        .ToList();

    // 5. 将创建好的按钮批量添加到 UI 和跟踪列表

    // 跟踪按钮
    _operationButtons.AddRange(newButtons);

    // 将按钮添加到 VisualElement 层次结构中
    foreach (var btn in newButtons)
    {
        _content.Add(btn);
    }
}

/// <summary>
/// (工厂方法) 创建一个跳转到项目设置的按钮。
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
/// (工厂方法) 根据 TerrainOperation 配置创建一个新的 ToolbarButton。
/// </summary>
private Button CreateOperationButton(PathTerrainOperation op)
{
    var buttonText = !string.IsNullOrEmpty(op.displayName) ? op.displayName : op.name;

    // 1. 创建按钮实例
    var btn = new ToolbarButton
    {
        text = buttonText,
        userData = buttonText
    };

    // 2. 注册回调
    //   (使用 RegisterCallback 比在构造函数中用 lambda 捕获 btn 自身更清晰)
    btn.RegisterCallback<ClickEvent>(_ => ExecuteOperation(op, btn));

    // 3. 设置初始状态
    btn.SetEnabled(!_isExecutingOperation);
    btn.AddToClassList("terrain-op-button");

    // 4. 应用动态样式
    ApplyOperationStyles(btn, op);

    return btn;
}

/// <summary>
/// (辅助方法) 将 op 上的动态样式（图标、颜色）应用到按钮上。
/// </summary>
private static void ApplyOperationStyles(Button btn, PathTerrainOperation op)
{
    if (op.icon == null) return;
    // 性能：直接操作 style 属性比创建 StyleBackground 更高效
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
/// 异步执行一个地形操作，并管理UI状态。
/// </summary>
private async void ExecuteOperation(PathTerrainOperation op, Button clickedButton)
{
    try
    {
        // 1. 守卫条款 (Guard Clauses) - 快速前置检查
        if (!CanExecuteOperation(op))
        {
            return;
        }

        // 2. 异步操作的 try/catch/finally 封装
        //    这是 async void 事件处理程序的关键模式
        try
        {
            // 3. 设置UI为"执行中"状态
            SetUIStateExecuting(clickedButton);

            // 4. 创建命令
            var cmd = op.CreateCommand(_ctx.Target, _ctx.HeightProvider);
            if (cmd == null) return; // 如果命令无效，finally 块会正确恢复UI

            // 5. (已提取) 附加预览边界
            if (TryGetPreviewBoundsXZ(out var previewBounds))
            {
                cmd.SetPreviewBoundsXZ(previewBounds);
            }

            // 6. 执行核心异步逻辑
            await _ctx.TerrainHandler.ExecuteAsync(cmd, null);
        }
        catch (Exception e)
        {
            // 捕获异步执行中的所有异常
            // 使用 LogException 而不是 Log 来保留完整的堆栈跟踪
            ErrorHandler.LogException(e);
        }
        finally
        {
            // 7. (已提取) 无论成功还是失败，都恢复UI状态
            RestoreUIState(clickedButton);
        }
    }
    catch (Exception e)
    {
        ErrorHandler.LogException(e);
    }
}

/// <summary>
/// (辅助方法) 检查所有前置条件是否满足。
/// </summary>
private bool CanExecuteOperation(PathTerrainOperation op)
{
    // 快速检查
    if (_isExecutingOperation || _ctx == null || !_ctx.Target || !op.CanExecute(_ctx.Target))
    {
        return false;
    }

    // 稍慢的配置检查
    var profile = _ctx.Target.profile;
    if (profile && PathStrategyRegistry.Instance.GetStrategy(profile.curveType) != null) return true;
    ShowConfigError();
    return false;

}

/// <summary>
/// (辅助方法) 封装所有“开始执行”的UI状态变更。
/// </summary>
private void SetUIStateExecuting(Button clickedButton)
{
    _isExecutingOperation = true;
    if (clickedButton != null)
    {
        clickedButton.text = "执行中...";
    }
    SetOperationButtonsEnabled(false);
}

/// <summary>
/// (辅助方法) 封装所有“恢复UI”的状态变更。
/// </summary>
private void RestoreUIState(Button clickedButton)
{
    _isExecutingOperation = false;
    SetOperationButtonsEnabled(true);

    // [BUG FIX] 从 userData 恢复原始文本
    // (假设在 RefreshContent 中已将原始文本存入 userData)
    if (clickedButton is { userData: string originalText })
    {
        clickedButton.text = originalText;
    }
}

/// <summary>
/// (辅助方法) 尝试获取预览网格的2D边界。
/// </summary>
private bool TryGetPreviewBoundsXZ(out Vector4 bounds)
{
    bounds = Vector4.zero;

    // 检查 Unity 对象时应使用 '== null'
    if (_ctx.PreviewGenerator == null) return false;

    var previewMesh = _ctx.PreviewGenerator.PreviewMesh;
    if (previewMesh == null) return false;

    var b = previewMesh.bounds;

    // 使用明确的 > 0 检查，可读性比属性模式稍好
    if (b.size.x > 0 && b.size.z > 0)
    {
        bounds = new Vector4(b.min.x, b.min.z, b.max.x, b.max.z);
        return true;
    }

    return false;
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
        ///     统一设置所有地形操作按钮的可用状态，并在启用时恢复其原始文本。
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
