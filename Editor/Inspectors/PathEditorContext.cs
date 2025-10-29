using System;

using MrPathV2.Editor.Input;
using MrPathV2.Editor.Preview;
using MrPathV2.Editor.Settings;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Providers;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Inspectors
{
    /// <summary>
    ///     路径编辑器上下文，封装编辑器依赖项并提供统一的访问接口
    /// </summary>
    public class PathEditorContext : IDisposable
    {
        private MrPathProjectSettings m_MrPathProjectSettings;
        private EditorRefreshManager m_RefreshManager;

        public PathEditorContext(PathCreator target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            m_RefreshManager = new EditorRefreshManager();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
            InitializeDependencies();
        }

        // 编辑器状态
        private int HoveredPointIdx { get; set; } = -1;
        private int HoveredSegmentIdx { get; set; } = -1;
        public bool IsDraggingHandle { get; set; }

        // 公共属性
        public PathCreator Target { get; }

        public IHeightProvider HeightProvider { get; private set; }

        public PathPreviewManager PreviewManager { get; private set; }

        private PreviewMaterialManager MaterialManager { get; set; }

        public TerrainOperationHandler TerrainHandler { get; private set; }

        // --- 新增属性/方法以满足编译器错误 ---

        /// <summary>
        ///     预览网格生成器，供外部（如 TerrainOperationsPanel）读取预览网格信息
        /// </summary>
        public IPreviewGenerator PreviewGenerator => PreviewManager?.Generator;

        /// <summary>
        ///     输入事件处理器
        /// </summary>
        public PathInputHandler InputHandler { get; private set; }

        public void Dispose()
        {
            // 取消所有待执行的刷新操作
            m_RefreshManager?.ClearAllPendingRefreshes();

            // 释放各个组件
            PreviewManager?.Dispose();
            HeightProvider?.Dispose();
            MaterialManager?.Cleanup(); // 使用新的Cleanup方法释放CommandBuffer等资源
            MaterialManager?.Dispose();
            TerrainHandler?.Dispose();
            m_RefreshManager?.Dispose();

#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif

            // 清空引用
            PreviewManager = null;
            HeightProvider = null;
            MaterialManager = null;
            TerrainHandler = null;
            m_RefreshManager = null;
        }

        /// <summary>
        ///     兼容旧代码：保留带参数的重载，但内部已不再需要额外参数。
        /// </summary>
        public void Initialize(PathCreator target)
        { /* 参数已无实际用途，保留以兼容旧接口 */
        }

        private void InitializeDependencies()
        {
            try
            {
                InitializeSettings();
                InitializeHeightProvider();

                if (!MultiPathPreviewRenderer.PreferGlobalOnly)
                {
                    InitializeLocalPreviewComponents();
                }

                InitializeSharedComponents();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize PathEditorContext dependencies: {ex.Message}");
                Dispose();
                throw;
            }
        }

        private void InitializeSettings()
        {
            m_MrPathProjectSettings = MrPathProjectSettings.GetOrCreateSettings();
        }

        private void InitializeHeightProvider()
        {
            HeightProvider = new TerrainHeightProvider();
        }

        private void InitializeLocalPreviewComponents()
        {
            MaterialManager = new PreviewMaterialManager();

            var generator = new DefaultPreviewGenerator();
            var template = GetPreviewMaterialTemplate();
            const float alpha = 1f;

            PreviewManager = new PathPreviewManager(generator, MaterialManager, template, alpha);
        }

        private Material GetPreviewMaterialTemplate()
        {
            var appearance = m_MrPathProjectSettings.appearanceDefaults;
            var template = appearance?.previewMaterialTemplate;

            // 运行时强制使用多层预览 Shader（若为空或非多层，自动回退/升级）
            var multiShader = Shader.Find("MrPath/PathPreviewSplatMulti");
            if (multiShader == null) return template;

            if (template == null || template.shader == null || !template.shader.name.Contains("MrPath/PathPreviewSplatMulti"))
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
                    template = new Material(multiShader)
                    {
                        name = "DefaultPreviewMaterialTemplate"
                    };
                }
            }

            return template;
        }

        private void InitializeSharedComponents()
        {
            // 初始化地形操作处理器和输入处理器（全局/本地都需要）
            TerrainHandler = new TerrainOperationHandler(HeightProvider);
            InputHandler = new PathInputHandler();
        }


        /// <summary>
        ///     请求刷新预览，使用防抖动机制
        /// </summary>
        public void RequestPreviewRefresh(bool forceImmediate = false)
        {
            if (PreviewManager == null) return;

            m_RefreshManager.RequestRefresh("preview_refresh", () =>
            {
                try
                {
                    // 全局预览启用时，统一走全局脏标记；仅在关闭全局或特殊本地模式下才本地更新

                    if (MultiPathPreviewRenderer.IsEnabled &&
                        (MultiPathPreviewRenderer.PreferGlobalOnly || !IsDraggingHandle))
                    {
                        MultiPathPreviewRenderer.MarkCreatorDirty(Target, spine: true, mesh: true, materials: true);
                    }
                    else
                    {
                        PreviewManager.Update(Target, HeightProvider);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Preview refresh failed: {ex.Message}");
                }
            }, forceImmediate);
        }

        /// <summary>
        ///     请求刷新场景视图
        /// </summary>
        public void RequestSceneViewRefresh(bool forceImmediate = false)
        {
            m_RefreshManager.RequestRefresh("scene_view_refresh", SceneView.RepaintAll, forceImmediate);
        }

        /// <summary>
        ///     请求刷新Inspector
        /// </summary>
        public void RequestInspectorRefresh(bool forceImmediate = false)
        {
            m_RefreshManager.RequestRefresh("inspector_refresh", () =>
            {
                if (Target)
                {
                    EditorUtility.SetDirty(Target);
                }
            }, forceImmediate);
        }

        public bool CanGeneratePreview() => Target != null && Target.profile != null && Target.pathData.KnotCount >= 2;

        /// <summary>
        ///     判断 PathCreator 当前状态是否合法，供外部快速查询。
        /// </summary>
        public bool IsPathValid() => Target && Target.IsValidState();

        /// <summary>
        ///     标记预览为脏，并请求刷新。
        /// </summary>
        public void MarkDirty(bool forceImmediate = true)
        {
            // 当路径或外观参数变更时，同时标记脊线、网格与材质为脏，确保 UV 等属性得到重新计算
            PreviewManager?.MarkSpineDirty();
            PreviewManager?.MarkMeshDirty();
            PreviewManager?.MarkMaterialsDirty(); // Ensure material updates when parameters change
            RequestPreviewRefresh(forceImmediate);
            RequestSceneViewRefresh(forceImmediate);
        }

        public PathEditorHandles.HandleDrawContext CreateHandleContext() => new PathEditorHandles.HandleDrawContext
        {
            Creator = Target,
            HeightProvider = HeightProvider,
            LatestSpine = PreviewManager?.LatestSpine,
            IsDragging = IsDraggingHandle,
            HoveredPointIndex = HoveredPointIdx,
            HoveredSegmentIndex = HoveredSegmentIdx,
            LineRenderer = PreviewManager?.GetSharedLineRenderer()
        };

        public void UpdateHoverState(PathEditorHandles.HandleDrawContext context)
        {
            HoveredPointIdx = context.HoveredPointIndex;
            HoveredSegmentIdx = context.HoveredSegmentIndex;
            IsDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
        }
#if UNITY_EDITOR
        private void OnBeforeAssemblyReload()
        {
            try
            {
                m_RefreshManager?.Dispose();
            }
            catch
            {
                // ignore cleanup errors
            }
        }
#endif
    }
}
