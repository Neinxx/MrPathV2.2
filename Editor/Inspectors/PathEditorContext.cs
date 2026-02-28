using System;
using UnityEngine;
using UnityEditor;


namespace MrPathV2
{
    /// <summary>
    /// 路径编辑器上下文，封装编辑器依赖项并提供统一的访问接口
    /// </summary>
    public class PathEditorContext : IDisposable
    {
        private PathCreator _target;
        private IHeightProvider _heightProvider;
        private PathPreviewManager _previewManager;
        private PreviewMaterialManager _materialManager;
        private TerrainOperationHandler _terrainHandler;
        private EditorRefreshManager _refreshManager;
        private MrPathProjectSettings mrPathProjectSettings;

        // 编辑器状态
        public int HoveredPointIdx { get; set; } = -1;
        public int HoveredSegmentIdx { get; set; } = -1;
        public bool IsDraggingHandle { get; set; } = false;

        // 公共属性
        public PathCreator Target => _target;
        public IHeightProvider HeightProvider => _heightProvider;
        public PathPreviewManager PreviewManager => _previewManager;
        public PreviewMaterialManager MaterialManager => _materialManager;
        public TerrainOperationHandler TerrainHandler => _terrainHandler;

        // --- 新增属性/方法以满足编译器错误 ---
        /// <summary>
        /// 当前正在执行的地形操作ID（由 TerrainOperationsPanel 进行设置/查询）
        /// </summary>
        public string CurrentOperationId { get; set; } = null;

        /// <summary>
        /// 预览网格生成器，供外部（如 TerrainOperationsPanel）读取预览网格信息
        /// </summary>
        public IPreviewGenerator PreviewGenerator => _previewManager?.Generator;

        /// <summary>
        /// 输入事件处理器
        /// </summary>
        public PathInputHandler InputHandler { get; private set; }

        public PathEditorContext(PathCreator target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _refreshManager = new EditorRefreshManager();
            InitializeDependencies();
        }

        /// <summary>
        /// 兼容旧代码：保留带参数的重载，但内部已不再需要额外参数。
        /// </summary>
        public void Initialize(PathCreator target) { /* 参数已无实际用途，保留以兼容旧接口 */ }

        private void InitializeDependencies()
        {
            try
            {
                // 先获取项目设置，供后续依赖初始化使用
                mrPathProjectSettings = MrPathProjectSettings.GetOrCreateSettings();

                // 初始化高度提供器
                _heightProvider = new TerrainHeightProvider();

                // 初始化材质管理器
                _materialManager = new PreviewMaterialManager();

                // 初始化预览管理器
                var generator = new DefaultPreviewGenerator();
                _previewManager = new PathPreviewManager(generator, _materialManager,
                mrPathProjectSettings.appearanceDefaults?.previewMaterialTemplate,
                mrPathProjectSettings.appearanceDefaults != null ? mrPathProjectSettings.appearanceDefaults.previewAlpha : 0.5f);

                // 初始化地形操作处理器
                _terrainHandler = new TerrainOperationHandler(_heightProvider);

                // 初始化输入处理器
                InputHandler = new PathInputHandler();

            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize PathEditorContext dependencies: {ex.Message}");
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 请求刷新预览，使用防抖动机制
        /// </summary>
        public void RequestPreviewRefresh(bool forceImmediate = false)
        {
            if (_previewManager == null) return;

            _refreshManager.RequestRefresh("preview_refresh", () =>
            {
                try
                {
                    _previewManager.Update(_target,_heightProvider);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Preview refresh failed: {ex.Message}");
                }
            }, forceImmediate);
        }

        /// <summary>
        /// 请求刷新场景视图
        /// </summary>
        public void RequestSceneViewRefresh(bool forceImmediate = false)
        {
            _refreshManager.RequestRefresh("scene_view_refresh", () =>
            {
                SceneView.RepaintAll();
            }, forceImmediate);
        }

        /// <summary>
        /// 请求刷新Inspector
        /// </summary>
        public void RequestInspectorRefresh(bool forceImmediate = false)
        {
            _refreshManager.RequestRefresh("inspector_refresh", () =>
            {
                if (_target != null)
                {
                    EditorUtility.SetDirty(_target);
                }
            }, forceImmediate);
        }

        public bool CanGeneratePreview()
        {
            return Target != null && Target.profile != null && Target.pathData.KnotCount >= 2;
        }

        /// <summary>
        /// 判断 PathCreator 当前状态是否合法，供外部快速查询。
        /// </summary>
        public bool IsPathValid()
        {
            return Target != null && Target.IsValidState();
        }

        /// <summary>
        /// 标记预览为脏，并请求刷新。
        /// </summary>
        public void MarkDirty()
        {
            _previewManager?.MarkDirty();
            RequestPreviewRefresh();
        }

        public PathEditorHandles.HandleDrawContext CreateHandleContext()
        {
            return new PathEditorHandles.HandleDrawContext
            {
                creator = Target,
                heightProvider = HeightProvider,
                latestSpine = PreviewManager?.LatestSpine,
                isDragging = IsDraggingHandle,
                hoveredPointIndex = HoveredPointIdx,
                hoveredSegmentIndex = HoveredSegmentIdx,
                lineRenderer = PreviewManager?.GetSharedLineRenderer() 
            };
        }

        public void UpdateHoverState(PathEditorHandles.HandleDrawContext context)
        {
            HoveredPointIdx = context.hoveredPointIndex;
            HoveredSegmentIdx = context.hoveredSegmentIndex;
            IsDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
        }

        public void Dispose()
        {
            // 取消所有待执行的刷新操作
            _refreshManager?.ClearAllPendingRefreshes();

            // 释放各个组件
            PreviewManager?.Dispose();
            HeightProvider?.Dispose();
            MaterialManager?.Dispose();
            TerrainHandler?.Dispose();
            _refreshManager?.Dispose();

            // 清空引用
            _previewManager = null;
            _heightProvider = null;
            _materialManager = null;
            _terrainHandler = null;
            _refreshManager = null;
        }
    }
}