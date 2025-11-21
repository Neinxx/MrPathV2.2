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
    /// <summary>
    ///     Provides an overlay for terrain operations in the Scene view.
    ///     This overlay allows users to execute predefined terrain operations on selected PathCreator objects.
    /// </summary>
    [Overlay(typeof(SceneView), "MrPathV2.TerrainOperations", "MrPathV2Operations")]
    public class TerrainOperationsOverlay : Overlay
    {
        private const string ElCpuOrGpu = "CpuOrGpu";
        private const string ElOperationsContainer = "operationsContainer";
        private const string ElPreviewMeshTarget = "previewMeshTarget";

        private static readonly string[] BackendChoices =
        {
            // 精简：当前版本仅支持 CPU 与 Auto 两种后端
            "CPU", "Auto"
        };

        /// <summary>
        ///     Stores all operation buttons for batch state management
        /// </summary>
        private readonly List<Button> _operationButtons = new List<Button>();

        private DropdownField _backendDropdown;
        private VisualElement _content;
        private PathEditorContext _ctx;
        private Toggle _previewMeshTarget;

        /// <summary>
        ///     Indicates whether an operation is currently executing
        /// </summary>
        private bool _isExecutingOperation;
        private MrPathProjectSettings _projectSettings;
        private VisualElement _root;
        private MrPathTerrainOperations _terrainOpsConfig;

        /// <summary>
        ///     Creates the panel content for the overlay
        /// </summary>
        /// <returns>The root visual element of the overlay</returns>
        public override VisualElement CreatePanelContent()
        {
            InitializeSettings();
            LoadUxmlContent();
            InitializeUiElements();
            SetupEventHandlers();
            RefreshContent();
            UpdateVisibility();

            return _root;
        }

        /// <summary>
        ///     Initializes project settings and terrain operations configuration
        /// </summary>
        private void InitializeSettings()
        {
            _projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            _terrainOpsConfig = _projectSettings.terrainOperations;
            //
        }

        /// <summary>
        ///     Loads the UXML content for the overlay
        /// </summary>
        private void LoadUxmlContent()
        {
            _root = UIResourceLoader.LoadAndClone<TerrainOperationsOverlay>();
            if (_root != null) return;
            // Fallback if UXML is not found
            _root = new VisualElement();
            _root.Add(new Label("TerrainOperationsOverlay.uxml is not found"));
        }

        /// <summary>
        ///     Initializes UI elements from the UXML
        /// </summary>
        private void InitializeUiElements()
        {
            _backendDropdown = _root.Q<DropdownField>(ElCpuOrGpu);
            _content = _root.Q<VisualElement>(ElOperationsContainer);
            _previewMeshTarget = _root.Q<Toggle>(ElPreviewMeshTarget);

            if (_backendDropdown != null)
            {
                InitializeBackendDropdown();
            }
            if (_previewMeshTarget != null)
            {
                InitializePreviewMeshTarget();
            }
        }

        /// <summary>
        ///     Sets up event handlers for the overlay
        /// </summary>
        private void SetupEventHandlers()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        /// <summary>
        ///     Initializes the backend dropdown with choices and current value
        /// </summary>
        private void InitializeBackendDropdown()
        {
            _backendDropdown.choices = BackendChoices.ToList();

            var backend = _projectSettings?.advancedSettings?.paintingBackend ??
                          PaintTerrainCommand.PaintingBackend.CPUCompute;

            _backendDropdown.index = backend switch
            {
                PaintTerrainCommand.PaintingBackend.Auto => 1,
                _ => 0
            };

            _backendDropdown.UnregisterValueChangedCallback(OnBackendChanged);
            _backendDropdown.RegisterValueChangedCallback(OnBackendChanged);
        }
        private void InitializePreviewMeshTarget()
        {
            _previewMeshTarget.value = GetCurrentPreviewMeshState();
            _previewMeshTarget.UnregisterCallback<ChangeEvent<bool>>(OnPreviewMeshToggled);
            _previewMeshTarget.RegisterCallback<ChangeEvent<bool>>(OnPreviewMeshToggled);

        }

        private static void OnPreviewMeshToggled(ChangeEvent<bool> evt)
        {

            var previewMesh = evt.newValue;
            var creators = MultiPathPreviewRenderer.GetCreators();
            IEnumerable<PathCreator> pathCreators = creators as PathCreator[] ?? creators.ToArray();

            // 检查是否有路径创建者
            if (!pathCreators.Any()) return;

            // 检查状态是否已经一致
            if (pathCreators.All(pc => pc.profile.showPreviewMesh == previewMesh)) return;

            // 为每个对象记录撤销操作并更新属性
            foreach (var pathCreator in pathCreators)
            {
                Undo.RecordObject(pathCreator.profile, "Change Preview Mesh");
                pathCreator.profile.showPreviewMesh = previewMesh;
                EditorUtility.SetDirty(pathCreator.profile);
            }
        }
        private static bool GetCurrentPreviewMeshState()
        {
            var creators = MultiPathPreviewRenderer.GetCreators();
            IEnumerable<PathCreator> pathCreators = creators as PathCreator[] ?? creators.ToArray();

            // 如果没有路径创建者，默认返回false
            if (!pathCreators.Any()) return false;

            // 只要有一个开启，就返回true
            return pathCreators.Any(pc => pc.profile.showPreviewMesh);
        }
        /// <summary>
        ///     Handles backend dropdown value changes
        /// </summary>
        /// <param name="evt">Change event with new value</param>
        private void OnBackendChanged(ChangeEvent<string> evt)
        {
            var newBackend = evt.newValue switch
            {
                "CPU" => PaintTerrainCommand.PaintingBackend.CPUCompute,
                _ => PaintTerrainCommand.PaintingBackend.Auto
            };

            var advanced = _projectSettings?.advancedSettings;
            if (advanced == null) return;

            if (advanced.paintingBackend == newBackend) return;

            Undo.RecordObject(advanced, "Change Painting Backend");
            advanced.paintingBackend = newBackend;
            EditorUtility.SetDirty(advanced);
        }

        /// <summary>
        ///     Refreshes the UI content based on current configuration
        /// </summary>
        private void RefreshContent()
        {
            // Early return if content container is not available
            if (_content == null) return;

            ClearContent();
            DisplayContent();
        }

        /// <summary>
        ///     Clears the content container and operation buttons list
        /// </summary>
        private void ClearContent()
        {
            _content.Clear();
            _operationButtons.Clear();
        }

        /// <summary>
        ///     Displays content based on terrain operations configuration
        /// </summary>
        private void DisplayContent()
        {
            // Show error message if configuration is missing
            if (_terrainOpsConfig == null)
            {
                _content.Add(new Label("Terrain operations configuration not found"));
                return;
            }

            var ops = _terrainOpsConfig.operations;

            // Show configuration redirect button if no operations are defined
            if (ops == null || ops.Length == 0)
            {
                _content.Add(CreateConfigRedirectButton());
                return;
            }

            CreateAndAddOperationButtons(ops);
        }

        /// <summary>
        ///     Creates and adds operation buttons for each terrain operation
        /// </summary>
        /// <param name="operations">Array of terrain operations</param>
        private void CreateAndAddOperationButtons(PathTerrainOperation[] operations)
        {
            var buttons = operations
                .Where(op => op != null)
                .OrderBy(op => op.order)
                .Select(CreateOperationButton)
                .ToList();

            _operationButtons.AddRange(buttons);

            foreach (var button in buttons)
            {
                _content.Add(button);
            }
        }

        /// <summary>
        ///     Creates a button that redirects to the project settings
        /// </summary>
        /// <returns>Configuration redirect button</returns>
        private static Button CreateConfigRedirectButton()
        {
            var btn = new Button(() => SettingsService.OpenProjectSettings("Project/MrPath"))
            {
                text = "Configure Terrain Operations"
            };
            btn.AddToClassList("unity-toolbar-button");
            return btn;
        }

        /// <summary>
        ///     Creates a toolbar button for a terrain operation
        /// </summary>
        /// <param name="operation">Terrain operation to create button for</param>
        /// <returns>Toolbar button for the operation</returns>
        private Button CreateOperationButton(PathTerrainOperation operation)
        {
            var buttonText = !string.IsNullOrEmpty(operation.displayName) ? operation.displayName : operation.name;

            var btn = new ToolbarButton
            {
                text = buttonText,
                userData = buttonText
            };

            btn.RegisterCallback<ClickEvent>(_ => ExecuteOperation(operation, btn));
            btn.SetEnabled(!_isExecutingOperation);
            btn.AddToClassList("terrain-op-button");

            ApplyOperationStyles(btn, operation);

            return btn;
        }

        /// <summary>
        ///     Applies operation-specific styles to a button
        /// </summary>
        /// <param name="button">Button to apply styles to</param>
        /// <param name="operation">Terrain operation with style information</param>
        private static void ApplyOperationStyles(Button button, PathTerrainOperation operation)
        {
            if (operation.icon == null) return;

            button.style.backgroundImage = operation.icon;

            var color = operation.buttonColor != default ? operation.buttonColor : Color.white;
            button.style.unityBackgroundImageTintColor = color;
        }

        /// <summary>
        ///     Handles selection changes in the editor
        /// </summary>
        private void OnSelectionChanged()
        {
            UpdateVisibility();
        }

        /// <summary>
        ///     Updates the overlay visibility based on the current selection
        /// </summary>
        private void UpdateVisibility()
        {
            var selectedGameObject = Selection.activeGameObject;
            if (selectedGameObject == null)
            {
                HideOverlay();
                return;
            }

            var pathCreator = selectedGameObject.GetComponent<PathCreator>();
            if (pathCreator == null)
            {
                HideOverlay();
                return;
            }

            ShowOverlay(pathCreator);
        }

        /// <summary>
        ///     Shows the overlay for a specific PathCreator
        /// </summary>
        /// <param name="pathCreator">PathCreator to show overlay for</param>
        private void ShowOverlay(PathCreator pathCreator)
        {
            displayed = true;

            if (_ctx?.Target == pathCreator) return;

            _ctx?.Dispose();
            _ctx = new PathEditorContext(pathCreator);
            RefreshContent();
        }

        /// <summary>
        ///     Hides the overlay and disposes of the context
        /// </summary>
        private void HideOverlay()
        {
            displayed = false;
            _ctx?.Dispose();
            _ctx = null;
        }

        /// <summary>
        ///     Executes a terrain operation
        /// </summary>
        /// <param name="operation">Operation to execute</param>
        /// <param name="clickedButton">Button that triggered the operation</param>
        private async void ExecuteOperation(PathTerrainOperation operation, Button clickedButton)
        {
            try
            {
                if (!CanExecuteOperation(operation))
                {
                    return;
                }

                try
                {
                    SetUIStateExecuting(clickedButton);

                    var command = operation.CreateCommand(_ctx.Target, _ctx.HeightProvider);
                    if (command == null) return;

                    if (TryGetPreviewBoundsXZ(out var previewBounds))
                    {
                        command.SetPreviewBoundsXZ(previewBounds);
                    }

                    await _ctx.TerrainHandler.ExecuteAsync(command, null);
                }
                catch (Exception e)
                {
                    ErrorHandler.LogException(e);
                }
                finally
                {
                    RestoreUIState(clickedButton);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        /// <summary>
        ///     Checks if an operation can be executed
        /// </summary>
        /// <param name="operation">Operation to check</param>
        /// <returns>True if the operation can be executed</returns>
        private bool CanExecuteOperation(PathTerrainOperation operation)
        {
            // Basic validation checks
            if (_isExecutingOperation || _ctx == null || !_ctx.Target || !operation.CanExecute(_ctx.Target))
            {
                return false;
            }

            // Validate path strategy
            var profile = _ctx.Target.profile;
            if (profile && PathStrategyRegistry.Instance.GetStrategy(profile.curveType) != null)
                return true;

            ShowConfigError();
            return false;
        }

        /// <summary>
        ///     Sets the UI to executing state
        /// </summary>
        /// <param name="clickedButton">Button that triggered the execution</param>
        private void SetUIStateExecuting(Button clickedButton)
        {
            _isExecutingOperation = true;
            clickedButton.text = "Progress..";
            SetOperationButtonsEnabled(false);
        }

        /// <summary>
        ///     Restores the UI state after execution
        /// </summary>
        /// <param name="clickedButton">Button that triggered the execution</param>
        private void RestoreUIState(Button clickedButton)
        {
            _isExecutingOperation = false;
            SetOperationButtonsEnabled(true);

            // Restore original button text
            if (clickedButton is { userData: string originalText })
            {
                clickedButton.text = originalText;
            }
        }

        /// <summary>
        ///     Tries to get the XZ bounds of the preview mesh
        /// </summary>
        /// <param name="bounds">Output bounds vector (min.x, min.z, max.x, max.z)</param>
        /// <returns>True if bounds were successfully retrieved</returns>
        private bool TryGetPreviewBoundsXZ(out Vector4 bounds)
        {
            bounds = Vector4.zero;

            if (_ctx.PreviewGenerator == null)
                return false;

            var previewMesh = _ctx.PreviewGenerator.PreviewMesh;
            if (previewMesh == null)
                return false;

            var meshBounds = previewMesh.bounds;

            // Validate bounds
            if (!(meshBounds.size.x > 0) || !(meshBounds.size.z > 0))
                return false;

            // Store XZ plane min and max values
            bounds = new Vector4(meshBounds.min.x, meshBounds.min.z, meshBounds.max.x, meshBounds.max.z);
            return true;
        }

        /// <summary>
        ///     Shows a configuration error dialog
        /// </summary>
        private static void ShowConfigError()
        {
            EditorUtility.DisplayDialog(
                "Configuration Error",
                "The selected path profile is missing a valid strategy. Ensure the PathCreator has a profile assigned and that strategies are properly configured in Project/MrPath settings.",
                "OK"
            );
        }

        /// <summary>
        ///     Sets the enabled state of all operation buttons
        /// </summary>
        /// <param name="enabled">Whether buttons should be enabled</param>
        private void SetOperationButtonsEnabled(bool enabled)
        {
            foreach (var button in _operationButtons.Where(btn => btn != null))
            {
                button.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
                button.focusable = enabled;

                if (enabled && button.userData is string originalText)
                {
                    button.text = originalText;
                }
            }
        }

        /// <summary>
        ///     Called when the overlay is disabled
        /// </summary>
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
