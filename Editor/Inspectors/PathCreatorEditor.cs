// PathCreatorEditor.cs
using UnityEditor;
using UnityEngine;
using System.IO;
// using UnityEditor.EditorTools; // 旧版工具已删除，移除冗余引用
namespace MrPathV2
{
    /// <summary>
    /// 【最终定稿 • 大师级】PathCreator 的自定义编辑器
    /// 
    /// 这份代码是整个工具“脸面”的最终形态。它遵循以下设计哲学：
    /// 
    /// - 优雅 (Elegant): 结构清晰，职责分明。使用内嵌编辑器提供一流的UX。
    /// - 可读 (Readable): 使用 #region 和详尽的“心法注释”来阐明每一部分的设计意图。
    /// - 高性能 (Performant): 缓存重用编辑器实例，避免不必要的GUI重绘，采用事件驱动的刷新机制。
    /// - 鲁棒 (Robust): 完整处理资源生命周期和事件订阅/退订，防止内存泄漏；
    ///   始终使用 SerializedObject 处理属性，确保Undo/Redo和Prefab的兼容性。
    /// - 逻辑清晰 (Logical): 编辑器只负责“呈现”和“应用修改”。它相信组件自身（通过OnValidate）
    ///   能够响应变化，实现了完美的关注点分离。
    /// - 干净 (Clean): 移除了所有对旧架构的依赖，代码精炼，无冗余。
    /// </summary>
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : Editor
    {
        #region 字段与属性 (Fields & Properties)

        private PathCreator _targetCreator;

        // --- 内嵌编辑器所需的核心缓存 ---
        private Editor _profileEmbeddedEditor;
        private SerializedObject _profileSO;

        // --- UI状态管理 ---
        // 将折叠状态保存在编辑器本地，而非序列化到资产中，
        // 这是避免多人协作时互相干扰UI状态的关键技巧。
        private bool _profileLocalExpanded = true;

        // --- 场景交互与预览状态 ---
        private IPreviewGenerator _previewGenerator;
        private IHeightProvider _heightProvider;
        private PreviewMaterialManager _materialManager;
        private PathInputHandler _inputHandler;
        private PathPreviewManager _previewManager;
        private TerrainOperationHandler _terrainHandler;

        private Material _splatMaterialTemplate;

        private int _hoveredPointIdx = -1;
        private int _hoveredSegmentIdx = -1;
        private bool _isDraggingHandle;

        private bool _isApplyingHeight;
        private bool _isApplyingPaint;

        #endregion

        #region 生命周期与事件订阅 (Lifecycle & Event Subscription)

        private void OnEnable()
        {
            _targetCreator = target as PathCreator;
            if (_targetCreator == null) return;

            // 初始化对 Profile 的引用，以便创建内嵌编辑器
            if (_targetCreator.profile != null)
            {
                InitProfileReferences(_targetCreator.profile);
            }

            // 初始化场景交互与预览组件（通过工厂注入）
            var settings = PathToolSettings.Instance;
            _previewGenerator = (settings.previewGeneratorFactory != null) ? settings.previewGeneratorFactory.Create() : new PreviewMeshControllerAdapter(new PreviewMeshController());
            _heightProvider = (settings.heightProviderFactory != null) ? settings.heightProviderFactory.Create() : new TerrainHeightProviderAdapter(new TerrainHeightProvider());
            _materialManager = (settings.previewMaterialManagerFactory != null) ? settings.previewMaterialManagerFactory.Create() : new PreviewMaterialManager();
            _inputHandler = new PathInputHandler();

            _splatMaterialTemplate = settings.previewMaterialTemplate != null ? settings.previewMaterialTemplate : Resources.Load<Material>("test");
            if (_splatMaterialTemplate == null)
            {
                Debug.LogWarning("未配置预览材质模板，已回退到 Resources/test，若不存在将无法显示预览。");
            }

            _previewManager = new PathPreviewManager(_previewGenerator, _materialManager, _splatMaterialTemplate);
            _terrainHandler = new TerrainOperationHandler(_heightProvider);

            Undo.undoRedoPerformed += MarkPathAsDirty;
            MarkPathAsDirty();

            _targetCreator.PathModified += OnPathModified;

        }

        private void OnDisable()
        {
            // OnDisable 是编辑器脚本的“金钟罩”，必须在这里清理所有引用的资源和事件，
            // 否则会导致内存泄漏和令人头痛的空引用错误。

            if (_profileEmbeddedEditor != null)
            {
                DestroyImmediate(_profileEmbeddedEditor);
                _profileEmbeddedEditor = null;
            }
            _profileSO = null;

            _previewManager?.Dispose();
            _heightProvider?.Dispose();
            _materialManager?.Dispose();
            _terrainHandler?.Dispose();
            Undo.undoRedoPerformed -= MarkPathAsDirty;
            if (_targetCreator != null)
            {
                _targetCreator.PathModified -= OnPathModified;
            }

            _isApplyingHeight = false;
            _isApplyingPaint = false;


        }

        #endregion

        #region 主GUI循环 (Main GUI Loop)

        public override void OnInspectorGUI()
        {
            if (_targetCreator == null) return;

            // 始终从 serializedObject 开始，这是保证Undo/Redo正确的基石。
            serializedObject.Update();

            // 绘制核心属性
            DrawCoreProperties();

            // 根据 Profile 是否存在，决定绘制内嵌编辑器还是提示信息
            if (_targetCreator.profile != null)
            {
                DrawEmbeddedProfileUI();
            }
            else
            {
                DrawProfileMissingUI();
            }

            // 应用所有修改
            serializedObject.ApplyModifiedProperties();
        }

        #endregion

        #region 场景交互 (Scene GUI & Handles)
        private void OnSceneGUI()
        {
            var creator = _targetCreator;
            if (!IsPathValid(creator))
            {
                _previewManager?.SetActive(false);
                return;
            }

            _previewManager.RefreshIfDirty(creator, _heightProvider);

            var context = CreateHandleContext(creator);
            PathEditorHandles.Draw(ref context);
            UpdateHoverStateFromContext(context);
            _inputHandler.HandleInputEvents(Event.current, creator, context.hoveredPathT, context.hoveredPointIndex);

            DrawApplyToTerrainUI();
            DrawCoordinateTooltip(creator, context);

            SceneView.RepaintAll();
        }
        #endregion

        #region UI绘制方法 (UI Drawing Methods)

        private void DrawCoreProperties()
        {
            var profileProperty = serializedObject.FindProperty("profile");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(profileProperty);
            if (EditorGUI.EndChangeCheck())
            {
                // 当用户直接更换Profile资产时，应用修改。
                // OnValidate会自动触发，进而触发事件链，无需额外操作。
                serializedObject.ApplyModifiedProperties();
            }

            var pathDataProperty = serializedObject.FindProperty("pathData");
            EditorGUILayout.PropertyField(pathDataProperty, true);
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

                    // 使用 BeginChangeCheck 监控内嵌编辑器中的所有UI变化
                    EditorGUI.BeginChangeCheck();
                    {
                        if (_profileEmbeddedEditor == null || _profileEmbeddedEditor.target != _targetCreator.profile)
                        {
                            _profileEmbeddedEditor = CreateEditor(_targetCreator.profile);
                        }
                        // 绘制 Profile 的 Inspector 内容
                        _profileEmbeddedEditor?.OnInspectorGUI();
                    }
                    if (EditorGUI.EndChangeCheck())
                    {

                        // 步骤 1: 签发“诏书” (保存修改)
                        _profileSO.ApplyModifiedProperties();

                        if (_targetCreator != null)
                        {
                            _targetCreator.NotifyProfileModified();
                        }

                        // 步骤 3 (可选但推荐): 强制重绘场景，确保即时响应
                        //  SceneView.RepaintAll();





                    }

                    EditorGUI.indentLevel--;
                }
            }
        }
        #endregion

        #region 事件处理器 (Event Handlers)
        private void OnPathModified(PathChangeCommand command) => MarkPathAsDirty();
        #endregion

        #region 辅助方法 (Helper Methods)



        private void InitProfileReferences(PathProfile profile)
        {
            _profileSO = (profile != null) ? new SerializedObject(profile) : null;
        }

        private void CreateDefaultProfile()
        {
            var newProfile = CreateInstance<PathProfile>();
            newProfile.name = "Default PathProfile";
            // ... (可以添加更详细的默认值设置)

            // 安全地创建资产目录和文件
            string saveDir = "Assets/__temp/MrPathV2.2/Settings/Profiles";
            Directory.CreateDirectory(saveDir);
            string savePath = AssetDatabase.GenerateUniqueAssetPath($"{saveDir}/{newProfile.name}.asset");

            AssetDatabase.CreateAsset(newProfile, savePath);
#if UNITY_2020_3_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(newProfile);
#else
            AssetDatabase.SaveAssets();
#endif

            // 自动将新创建的Profile赋给当前对象
            serializedObject.FindProperty("profile").objectReferenceValue = newProfile;
            serializedObject.ApplyModifiedProperties();

            EditorGUIUtility.PingObject(newProfile);
        }

        private bool IsPathValid(PathCreator c)
        {
            return c != null && c.profile != null && c.pathData.KnotCount >= 2;
        }

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

        public void MarkPathAsDirty()
        {
            _previewManager?.MarkDirty();
            SceneView.RepaintAll();
        }

        private void DrawCoordinateTooltip(PathCreator creator, PathEditorHandles.HandleDrawContext context)
        {
            if (creator == null) return;
            if (context.hoveredPathT > -1)
            {
                Vector3 worldPos = creator.GetPointAt(context.hoveredPathT);
                Handles.BeginGUI();
                Vector2 screen = HandleUtility.WorldToGUIPoint(worldPos);
                var rect = new Rect(screen.x + 12, screen.y + 12, 160, 22);
                GUI.Label(rect, $"Pos: {worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}", EditorStyles.helpBox);
                Handles.EndGUI();
            }
        }

        private void DrawApplyToTerrainUI()
        {
            Handles.BeginGUI();

            SceneView currentSceneView = SceneView.currentDrawingSceneView;
            if (currentSceneView == null)
            {
                Handles.EndGUI();
                return;
            }

            var settings = PathToolSettings.Instance;
            float windowWidth = settings.sceneUiWindowWidth;
            float windowHeight = settings.sceneUiWindowHeight;

            Rect sceneViewClientRect = currentSceneView.position;
            float xPos = sceneViewClientRect.xMax - windowWidth - settings.sceneUiRightMargin;
            float yPos = sceneViewClientRect.yMax - windowHeight - settings.sceneUiBottomMargin;
            Rect windowRect = new Rect(xPos, yPos, windowWidth, windowHeight);

            GUILayout.Window(GetHashCode(), windowRect, id =>
            {
                GUILayout.BeginVertical();
                GUILayout.Space(6);
                GUILayout.Label("路径应用工具", EditorStyles.boldLabel);
                GUILayout.Space(4);

                var ops = settings.operations;
                if (ops != null && ops.Length > 0)
                {
                    System.Array.Sort(ops, (a, b) => a.order.CompareTo(b.order));
                    foreach (var op in ops)
                    {
                        if (op == null) continue;
                        bool isBusy = _isApplyingHeight || _isApplyingPaint;
                        using (new EditorGUI.DisabledScope(isBusy))
                        {
                            GUI.backgroundColor = op.buttonColor;
                            string label = op.displayName;
                            if (GUILayout.Button(new GUIContent(label, op.icon), EditorStyles.toolbarButton, GUILayout.Height(26)))
                            {
                                ExecuteOperation(op);
                            }
                            GUILayout.Space(3);
                        }
                    }
                }
                else
                {
                    GUILayout.Label("未配置任何路径地形操作。请在 Project/MrPath Settings -> Operations 中添加操作资产。", EditorStyles.helpBox);
                    if (GUILayout.Button("打开设置 (Project/MrPath Settings)", EditorStyles.toolbarButton, GUILayout.Height(22)))
                    {
                        SettingsService.OpenProjectSettings("Project/MrPath Settings");
                    }
                }

                GUILayout.Space(3);
                using (new EditorGUI.DisabledScope(_isApplyingHeight == false && _isApplyingPaint == false))
                {
                    GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                    if (GUILayout.Button("取消当前操作", EditorStyles.toolbarButton, GUILayout.Height(22)))
                    {
                        _terrainHandler?.Cancel();
                    }
                }

                GUILayout.Space(3);
                GUI.backgroundColor = new Color(0.8f, 0.95f, 0.6f);
                if (GUILayout.Button("刷新地形缓存", EditorStyles.toolbarButton, GUILayout.Height(22)))
                {
                    _heightProvider?.MarkAsDirty();
                    MarkPathAsDirty();
                    currentSceneView.ShowNotification(new GUIContent("地形缓存已刷新"));
                }

                GUILayout.Space(4);
                GUILayout.EndVertical();

                GUI.DragWindow(new Rect(0, 0, windowWidth, 20));

            }, "路径转地形");

            Handles.EndGUI();
            GUI.backgroundColor = Color.white;
        }

        private void ExecuteOperation(PathTerrainOperation op)
        {
            var creator = _targetCreator;
            if (creator == null) return;

            // 校验 PathProfile
            if (creator.profile == null)
            {
                EditorUtility.DisplayDialog("未配置路径 Profile", "请先在 PathCreator 组件上指定 PathProfile。", "确定");
                return;
            }

            // 校验策略注册与当前策略
            var registry = MrPathV2.PathStrategyRegistry.Instance;
            var strategy = registry?.GetStrategy(creator.profile.curveType);
            if (strategy == null)
            {
                EditorUtility.DisplayDialog("未配置路径策略", "未找到 PathStrategyRegistry 或对应曲线类型的策略资产。请在 Project/MrPath Settings 中创建注册表并填充策略。", "打开设置");
                SettingsService.OpenProjectSettings("Project/MrPath Settings");
                return;
            }

            if (!op.CanExecute(creator)) return;
            var cmd = op.CreateCommand(creator, _heightProvider);
            if (cmd == null) return;

            if (cmd is FlattenTerrainCommand)
            {
                _ = _terrainHandler.ExecuteAsync(cmd, b => _isApplyingHeight = b);
            }
            else if (cmd is PaintTerrainCommand)
            {
                _ = _terrainHandler.ExecuteAsync(cmd, b => _isApplyingPaint = b);
            }
            else
            {
                _ = _terrainHandler.ExecuteAsync(cmd, b => { _isApplyingHeight = b; _isApplyingPaint = b; });
            }
        }

        private void OnFlattenTerrainClicked()
        {
            var creator = _targetCreator;
            if (!IsPathValid(creator)) return;

            if (creator.profile == null)
            {
                EditorUtility.DisplayDialog("未配置路径 Profile", "请先在 PathCreator 组件上指定 PathProfile。", "确定");
                return;
            }
            var registry = MrPathV2.PathStrategyRegistry.Instance;
            var strategy = registry?.GetStrategy(creator.profile.curveType);
            if (strategy == null)
            {
                EditorUtility.DisplayDialog("未配置路径策略", "未找到 PathStrategyRegistry 或对应曲线类型的策略资产。请在 Project/MrPath Settings 中创建注册表并填充策略。", "打开设置");
                SettingsService.OpenProjectSettings("Project/MrPath Settings");
                return;
            }

            var command = new FlattenTerrainCommand(creator, _heightProvider);
            _ = _terrainHandler.ExecuteAsync(command, b => _isApplyingHeight = b);
        }

        private void OnPaintTerrainClicked()
        {
            var creator = _targetCreator;
            if (!IsPathValid(creator)) return;

            if (creator.profile == null)
            {
                EditorUtility.DisplayDialog("未配置路径 Profile", "请先在 PathCreator 组件上指定 PathProfile。", "确定");
                return;
            }
            var registry = MrPathV2.PathStrategyRegistry.Instance;
            var strategy = registry?.GetStrategy(creator.profile.curveType);
            if (strategy == null)
            {
                EditorUtility.DisplayDialog("未配置路径策略", "未找到 PathStrategyRegistry 或对应曲线类型的策略资产。请在 Project/MrPath Settings 中创建注册表并填充策略。", "打开设置");
                SettingsService.OpenProjectSettings("Project/MrPath Settings");
                return;
            }

            var command = new PaintTerrainCommand(creator, _heightProvider);
            _ = _terrainHandler.ExecuteAsync(command, b => _isApplyingPaint = b);
        }
        #endregion
    }
}