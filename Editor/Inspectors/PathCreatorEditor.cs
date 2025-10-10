// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Inspectors/PathCreatorEditor.cs
using UnityEditor;
using UnityEngine;
using System.IO;

namespace MrPathV2
{
    /// <summary>
    /// [最终整合版] PathCreator 的自定义编辑器。
    /// 它是工具的核心交互界面，集成了 Inspector 面板、场景路径绘制、用户输入处理
    /// 以及数据驱动的地形操作UI面板，遵循单一职责和最佳用户体验原则。
    /// </summary>
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : Editor
    {
        #region 字段

        private PathCreator _targetCreator;

        // --- 内嵌编辑器 ---
        private Editor _profileEmbeddedEditor;
        private SerializedObject _profileSO;
        private bool _profileLocalExpanded = true;

        // --- 核心服务与处理器 ---
        private IPreviewGenerator _previewGenerator;
        private IHeightProvider _heightProvider;
        private PreviewMaterialManager _materialManager;
        private PathInputHandler _inputHandler;
        private PathPreviewManager _previewManager;
        private TerrainOperationHandler _terrainHandler;

        // --- 场景交互状态 ---
        private int _hoveredPointIdx = -1;
        private int _hoveredSegmentIdx = -1;
        private bool _isDraggingHandle;

        // --- 地形操作状态 ---
        private bool _isApplyingHeight;
        private bool _isApplyingPaint;

        #endregion

        #region 生命周期

        private void OnEnable()
        {
            _targetCreator = target as PathCreator;
            if (_targetCreator == null) return;

            if (_targetCreator.profile != null)
            {
                InitProfileReferences(_targetCreator.profile);
            }

            // 从新的模块化配置系统中获取所有依赖
            var projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            var advancedSettings = projectSettings.advancedSettings;
            var appearanceSettings = projectSettings.appearanceDefaults;

            // 初始化核心服务
            _previewGenerator = (advancedSettings?.previewGeneratorFactory != null) ? advancedSettings.previewGeneratorFactory.Create() : new PreviewMeshControllerAdapter(new PreviewMeshController());
            _heightProvider = (advancedSettings?.heightProviderFactory != null) ? advancedSettings.heightProviderFactory.Create() : new TerrainHeightProviderAdapter(new TerrainHeightProvider());
            _materialManager = (advancedSettings?.previewMaterialManagerFactory != null) ? advancedSettings.previewMaterialManagerFactory.Create() : new PreviewMaterialManager();

            // 初始化处理器
            _inputHandler = new PathInputHandler();
            _terrainHandler = new TerrainOperationHandler(_heightProvider);

            // 初始化预览管理器
            Material splatMaterialTemplate = appearanceSettings?.previewMaterialTemplate;
            _previewManager = new PathPreviewManager(_previewGenerator, _materialManager, splatMaterialTemplate);

            // 订阅事件
            Undo.undoRedoPerformed += MarkPathAsDirty;
            _targetCreator.PathModified += OnPathModified;

            MarkPathAsDirty(); // 首次启用时强制刷新
        }

        private void OnDisable()
        {
            // 严格清理所有资源和事件订阅，防止内存泄漏
            if (_profileEmbeddedEditor != null)
            {
                DestroyImmediate(_profileEmbeddedEditor);
            }

            _previewManager?.Dispose();
            _heightProvider?.Dispose();
            _materialManager?.Dispose();
            _terrainHandler?.Dispose();

            Undo.undoRedoPerformed -= MarkPathAsDirty;
            if (_targetCreator != null)
            {
                _targetCreator.PathModified -= OnPathModified;
            }

            // 重置状态
            _isApplyingHeight = false;
            _isApplyingPaint = false;
        }

        #endregion

        #region GUI 绘制

        public override void OnInspectorGUI()
        {
            if (_targetCreator == null) return;

            serializedObject.Update();

            DrawCoreProperties();

            if (_targetCreator.profile != null)
            {
                DrawEmbeddedProfileUI();
            }
            else
            {
                DrawProfileMissingUI();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (!IsPathValid(_targetCreator))
            {
                _previewManager?.SetActive(false);
                return;
            }
            _previewManager.SetActive(true);

            // 刷新预览网格
            _previewManager.RefreshIfDirty(_targetCreator, _heightProvider);

            // 绘制路径Handles并处理输入
            var context = CreateHandleContext(_targetCreator);
            PathEditorHandles.Draw(ref context);
            UpdateHoverStateFromContext(context);
            _inputHandler.HandleInputEvents(Event.current, _targetCreator, context.hoveredPathT, context.hoveredPointIndex);

            // 绘制集成的地形操作UI面板
            DrawApplyToTerrainUI();
            // DrawModernApplyToTerrainUI();

            DrawCoordinateTooltip(_targetCreator, context);

            SceneView.RepaintAll();
        }

        private void DrawModernApplyToTerrainUI()
        {
            Handles.BeginGUI();

            var uiSettings = MrPathProjectSettings.GetOrCreateSettings().sceneUISettings;
            if (uiSettings == null) { Handles.EndGUI(); return; }

            // 使用一个更大的尺寸以容纳更美观的布局
            float windowWidth = 200f;
            float windowHeight = 150f;

            Rect windowRect = new Rect(
                SceneView.currentDrawingSceneView.position.width - windowWidth - uiSettings.sceneUiRightMargin,
                SceneView.currentDrawingSceneView.position.height - windowHeight - uiSettings.sceneUiBottomMargin,
                windowWidth,
                windowHeight
            );

            // 使用一个半透明的深色背景，模仿Unity原生面板
            GUI.Box(windowRect, GUIContent.none, EditorStyles.helpBox);

            GUILayout.BeginArea(windowRect);

            // 标题
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Mr.Path 地形工具", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            GUIHelper.DrawSeparator(); // 绘制一条分隔线

            // 按钮区域
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 4, 4) });

            var terrainOps = MrPathProjectSettings.GetOrCreateSettings().terrainOperations;
            var ops = terrainOps?.operations;
            bool isAnyOperationRunning = _isApplyingHeight || _isApplyingPaint;

            if (ops != null && ops.Length > 0)
            {
                System.Array.Sort(ops, (a, b) => a.order.CompareTo(b.order));

                foreach (var op in ops)
                {
                    if (op == null) continue;

                    bool isThisOpRunning = (op.CreateCommand(_targetCreator, null) is FlattenTerrainCommand && _isApplyingHeight) ||
                                           (op.CreateCommand(_targetCreator, null) is PaintTerrainCommand && _isApplyingPaint);

                    using (new EditorGUI.DisabledScope(isAnyOperationRunning && !isThisOpRunning))
                    {
                        string buttonText = isThisOpRunning ? "正在执行..." : op.displayName;
                        // 使用内置图标创建内容
                        GUIContent content = new GUIContent(buttonText, GetIconForOperation(op.displayName));

                        if (GUILayout.Button(content, GUILayout.Height(28)))
                        {
                            ExecuteOperation(op);
                        }
                    }
                }
            }
            else
            {
                if (GUILayout.Button("配置地形操作"))
                {
                    SettingsService.OpenProjectSettings("Project/MrPath");
                }
            }

            GUILayout.FlexibleSpace();

            // 底部按钮
            using (new EditorGUI.DisabledScope(!isAnyOperationRunning))
            {
                if (GUILayout.Button(new GUIContent(" 取消操作", GUIHelper.GetIcon("d_winbtn_win_close")), GUILayout.Height(22)))
                {
                    _terrainHandler?.Cancel();
                }
            }
            if (GUILayout.Button(new GUIContent(" 刷新地形缓存", GUIHelper.GetIcon("d_Refresh")), GUILayout.Height(22)))
            {
                _heightProvider?.MarkAsDirty();
                MarkPathAsDirty();
                SceneView.currentDrawingSceneView.ShowNotification(new GUIContent("地形缓存已刷新"));
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();

            Handles.EndGUI();
        }
        private Texture2D GetIconForOperation(string opName)
        {
            if (opName.Contains("压平") || opName.ToLower().Contains("flatten"))
                return GUIHelper.GetIcon("d_TerrainInspector.TerrainToolSmoothHeight");
            if (opName.Contains("绘制") || opName.ToLower().Contains("paint"))
                return GUIHelper.GetIcon("d_TerrainInspector.TerrainToolPaint");

            return GUIHelper.GetIcon("d_CustomTool");
        }
        /// <summary>
        /// 提供UI绘制辅助功能，如获取内置图标和绘制分隔线。
        /// </summary>
        public static class GUIHelper
        {
            private static readonly Color separatorColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            public static Texture2D GetIcon(string iconName)
            {
                return EditorGUIUtility.IconContent(iconName).image as Texture2D;
            }

            public static void DrawSeparator(int height = 1)
            {
                Rect rect = EditorGUILayout.GetControlRect(false, height);
                rect.height = height;
                EditorGUI.DrawRect(rect, separatorColor);
            }
        }
        #endregion

        #region UI 绘制辅助方法

        private void DrawCoreProperties()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pathData"), true);
            EditorGUILayout.Space();
        }

        private void DrawProfileMissingUI()
        {
            EditorGUILayout.HelpBox("未指定路径配置文件 (Profile)。请先创建或指定一个 Profile 资产。", MessageType.Warning);
            if (GUILayout.Button("快速创建默认 Profile"))
            {
                CreateDefaultProfile();
            }
        }

        private void DrawEmbeddedProfileUI()
        {
            // ... (此部分代码无需修改，保持原样)
            if (_profileSO == null || _profileSO.targetObject != _targetCreator.profile)
            {
                InitProfileReferences(_targetCreator.profile);
            }
            if (_profileSO == null) return;

            _profileSO.Update();
            using (new EditorGUILayout.VerticalScope("Box"))
            {
                _profileLocalExpanded = EditorGUILayout.Foldout(_profileLocalExpanded, "路径配置文件 (Profile)", true, EditorStyles.foldoutHeader);
                if (_profileLocalExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    {
                        if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != _targetCreator.profile)
                        {
                            _profileEmbeddedEditor = CreateEditor(_targetCreator.profile);
                        }
                        _profileEmbeddedEditor?.OnInspectorGUI();
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        _profileSO.ApplyModifiedProperties();
                        if (_targetCreator != null)
                        {
                            _targetCreator.NotifyProfileModified();
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        /// <summary>
        /// [核心集成] 绘制场景右下角的地形操作UI面板。
        /// </summary>
        private void DrawApplyToTerrainUI()
        {
            Handles.BeginGUI();

            SceneView currentSceneView = SceneView.currentDrawingSceneView;
            if (currentSceneView == null)
            {
                Handles.EndGUI();
                return;
            }

            // 从新的配置系统获取UI布局设置
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

                // 从新的配置系统获取操作列表
                var terrainOps = MrPathProjectSettings.GetOrCreateSettings().terrainOperations;
                var ops = terrainOps?.operations;

                if (ops != null && ops.Length > 0)
                {
                    System.Array.Sort(ops, (a, b) => a.order.CompareTo(b.order));
                    bool isAnyOperationRunning = _isApplyingHeight || _isApplyingPaint;

                    foreach (var op in ops)
                    {
                        if (op == null) continue;

                        // 判断当前按钮对应的操作是否正在运行
                        bool isThisOpRunning = (op.CreateCommand(_targetCreator, null) is FlattenTerrainCommand && _isApplyingHeight) ||
                                              (op.CreateCommand(_targetCreator, null) is PaintTerrainCommand && _isApplyingPaint);

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

                // 取消和刷新按钮
                //  using (new EditorGUI.DisabledScope(!_isApplyingHeight && !_isApplyingPaint))
                // {
                //     GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                //     if (GUILayout.Button("取消当前操作", GUILayout.Height(22)))
                //     {
                //         // _terrainHandler?.Cancel();
                //         //模拟按ctrl+z
                //         EditorApplication.ExecuteMenuItem("Edit/Undo");
                //     }
                // }

                GUI.backgroundColor = new Color(0.8f, 0.95f, 0.6f);
                if (GUILayout.Button("刷新地形缓存", GUILayout.Height(22)))
                {
                    _heightProvider?.MarkAsDirty();
                    MarkPathAsDirty();
                    currentSceneView.ShowNotification(new GUIContent("地形缓存已刷新"));
                }

                GUI.DragWindow(new Rect(0, 0, 10000, 20));

            }, "Mr.Path");

            Handles.EndGUI();
            GUI.backgroundColor = Color.white;
        }

        #endregion

        #region 逻辑与辅助方法

        private void OnPathModified(PathChangeCommand command) => MarkPathAsDirty();
        public void MarkPathAsDirty()
        {
            _previewManager?.MarkDirty();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// [核心集成] 执行一个地形操作命令。
        /// </summary>
        private void ExecuteOperation(PathTerrainOperation op)
        {
            if (_targetCreator == null || !op.CanExecute(_targetCreator)) return;

            // 运行时检查，确保Profile和策略都已配置
            if (_targetCreator.profile == null || PathStrategyRegistry.Instance.GetStrategy(_targetCreator.profile.curveType) == null)
            {
                EditorUtility.DisplayDialog("配置错误", "路径缺少 Profile 或未找到对应的路径策略 (Strategy)。\n请检查 Path Creator 的 Profile 字段以及 Project/MrPath 设置中的高级设置。", "确定");
                return;
            }

            var cmd = op.CreateCommand(_targetCreator, _heightProvider);
            if (cmd == null) return;

            // 根据命令类型设置不同的状态旗标
            if (cmd is FlattenTerrainCommand)
                _ = _terrainHandler.ExecuteAsync(cmd, b => _isApplyingHeight = b);
            else if (cmd is PaintTerrainCommand)
                _ = _terrainHandler.ExecuteAsync(cmd, b => _isApplyingPaint = b);
            else // 对于未知或复合命令，同时设置两个旗标以锁定UI
                _ = _terrainHandler.ExecuteAsync(cmd, b => { _isApplyingHeight = b; _isApplyingPaint = b; });
        }

        // --- 其他无需修改的辅助方法 ---
        private void InitProfileReferences(PathProfile profile)
        {
            _profileSO = (profile != null) ? new SerializedObject(profile) : null;
        }
        private void CreateDefaultProfile()
        {
            var newProfile = CreateInstance<PathProfile>();
            string saveDir = "Assets/__temp/MrPathV2.2/Settings/Profiles";
            Directory.CreateDirectory(saveDir);
            string savePath = AssetDatabase.GenerateUniqueAssetPath($"{saveDir}/Default PathProfile.asset");
            AssetDatabase.CreateAsset(newProfile, savePath);
            AssetDatabase.SaveAssets();
            serializedObject.FindProperty("profile").objectReferenceValue = newProfile;
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(newProfile);
        }
        private bool IsPathValid(PathCreator c) => c != null && c.profile != null && c.pathData.KnotCount >= 2;
        private PathEditorHandles.HandleDrawContext CreateHandleContext(PathCreator creator) => new()
        {
            creator = creator,
            heightProvider = _heightProvider,
            latestSpine = _previewManager?.LatestSpine,
            isDragging = _isDraggingHandle,
            hoveredPointIndex = _hoveredPointIdx,
            hoveredSegmentIndex = _hoveredSegmentIdx,
        };
        private void UpdateHoverStateFromContext(PathEditorHandles.HandleDrawContext context)
        {
            _hoveredPointIdx = context.hoveredPointIndex;
            _hoveredSegmentIdx = context.hoveredSegmentIndex;
            _isDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
        }
        private void DrawCoordinateTooltip(PathCreator creator, PathEditorHandles.HandleDrawContext context)
        {
            if (creator != null && context.hoveredPathT > -1)
            {
                Vector3 worldPos = creator.GetPointAt(context.hoveredPathT);
                Handles.BeginGUI();
                Vector2 screen = HandleUtility.WorldToGUIPoint(worldPos);
                GUI.Label(new Rect(screen.x + 12, screen.y + 12, 160, 22), $"Pos: {worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}", EditorStyles.helpBox);
                Handles.EndGUI();
            }
        }

        #endregion
    }
}