using System;
using __temp.MrPathV2._2.Editor.Input;
using __temp.MrPathV2._2.Editor.Preview;
using __temp.MrPathV2._2.Editor.Settings;
using __temp.MrPathV2._2.Editor.Terrain;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Preview;
using __temp.MrPathV2._2.Runtime.Providers;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Inspectors
{
    /// <summary>
    /// 路径编辑器上下文，封装编辑器依赖项并提供统一的访问接口
    /// </summary>
    public class PathEditorContext : IDisposable
    {
        private EditorRefreshManager _refreshManager;
        private MrPathProjectSettings _mrPathProjectSettings;

        // 编辑器状态
        private int HoveredPointIdx { get; set; } = -1;
        private int HoveredSegmentIdx { get; set; } = -1;
        private bool IsDraggingHandle { get; set; }

        // 公共属性
        public PathCreator Target { get; }

        public IHeightProvider HeightProvider { get; private set; }

        public PathPreviewManager PreviewManager { get; private set; }

        private PreviewMaterialManager MaterialManager { get; set; }

        public TerrainOperationHandler TerrainHandler { get; private set; }

        // --- 新增属性/方法以满足编译器错误 ---

        /// <summary>
        /// 预览网格生成器，供外部（如 TerrainOperationsPanel）读取预览网格信息
        /// </summary>
        public IPreviewGenerator PreviewGenerator => PreviewManager?.Generator;

        /// <summary>
        /// 输入事件处理器
        /// </summary>
        public PathInputHandler InputHandler { get; private set; }

        public PathEditorContext(PathCreator target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
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
                _mrPathProjectSettings = MrPathProjectSettings.GetOrCreateSettings();

                // 初始化高度提供器
                HeightProvider = new TerrainHeightProvider();

                // 初始化材质管理器
                MaterialManager = new PreviewMaterialManager();

                // 初始化预览管理器
                var generator = new DefaultPreviewGenerator();
                var appearance = _mrPathProjectSettings.appearanceDefaults;
                var template = appearance?.previewMaterialTemplate;
                var alpha = 1f;

                // 运行时强制使用多层预览 Shader（若为空或非多层，自动回退/升级）
                var multiShader = Shader.Find("MrPath/PathPreviewSplatMulti");
                if (multiShader != null)
                {
                    if (template == null || template.shader == null || !template.shader.name.Contains("PathPreviewSplatMulti"))
                    {
                        if (template != null && template.shader != null)
                        {
                            // 就地升级已有模板的 shader 引用
                            template.shader = multiShader;
                            EditorUtility.SetDirty(template);
                        }
                        else
                        {
                            // 为空时创建一个运行时材质实例用于预览
                            template = new Material(multiShader) { name = "DefaultPreviewMaterialTemplate" };
                        }
                    }
                }

                PreviewManager = new PathPreviewManager(generator, MaterialManager, template, alpha);

                // 初始化地形操作处理器
                TerrainHandler = new TerrainOperationHandler(HeightProvider);

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
        private void RequestPreviewRefresh(bool forceImmediate = false)
        {
            if (PreviewManager == null) return;

            _refreshManager.RequestRefresh("preview_refresh", () =>
            {
                try
                {
                    PreviewManager.Update(Target,HeightProvider);
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
            _refreshManager.RequestRefresh("scene_view_refresh", SceneView.RepaintAll, forceImmediate);
        }

        /// <summary>
        /// 请求刷新Inspector
        /// </summary>
        public void RequestInspectorRefresh(bool forceImmediate = false)
        {
            _refreshManager.RequestRefresh("inspector_refresh", () =>
            {
                if (Target)
                {
                    EditorUtility.SetDirty(Target);
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
            return Target && Target.IsValidState();
        }

        /// <summary>
        /// 标记预览为脏，并请求刷新。
        /// </summary>
        public void MarkDirty()
    {
            // 当路径或外观参数变更时，同时标记脊线、网格与材质为脏，确保 UV 等属性得到重新计算
            PreviewManager?.MarkSpineDirty();
            PreviewManager?.MarkMeshDirty();
            PreviewManager?.MarkMaterialsDirty(); // Ensure material updates when parameters change
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
            PreviewManager = null;
            HeightProvider = null;
            MaterialManager = null;
            TerrainHandler = null;
            _refreshManager = null;
        }
    }
}