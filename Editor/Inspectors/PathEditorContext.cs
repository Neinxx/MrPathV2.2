using System;
using MrPathV2.Editor.Preview;
using MrPathV2.Editor.Settings;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Providers;
using MrPathV2.Editor.Input;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Inspectors
{
    /// <summary>
    ///     路径编辑器上下文，封装编辑器依赖项并提供统一的访问接口
    /// </summary>
    public class PathEditorContext : IDisposable
    {
        private bool m_Disposed; // 防止重复释放
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

        public TerrainOperationHandler TerrainHandler { get; private set; }

        // --- 新增属性/方法以满足编译器错误 ---

        /// <summary>
        ///     预览网格生成器，供外部（如 TerrainOperationsPanel）读取预览网格信息
        /// </summary>
        public IPreviewGenerator PreviewGenerator => null; // 现在使用全局多路径预览系统

        /// <summary>
        ///     输入事件处理器
        /// </summary>
        public PathInputHandler InputHandler { get; private set; }

        public void Dispose()
        {
            if (m_Disposed) return; // 防止重复释放
            m_Disposed = true;

            // 取消所有待执行的刷新操作
            m_RefreshManager?.ClearAllPendingRefreshes();

            // 释放各个组件
            HeightProvider?.Dispose();
            TerrainHandler?.Dispose();
            m_RefreshManager?.Dispose();

#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif

            // 清空引用
            HeightProvider = null;
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
            m_RefreshManager.RequestRefresh("preview_refresh", () =>
            {
                try
                {
                    // 统一使用全局预览系统
                    MultiPathPreviewRenderer.MarkCreatorDirty(Target, true, true, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Preview refresh failed: {ex.Message}");
                }
            }, forceImmediate);
        }

        /// <summary>
        ///     仅请求脊线(Spine)相关的刷新：会隐含导致网格重建，但不触发材质重建。
        /// </summary>
        public void RequestSpineRefresh(bool forceImmediate = false)
        {
            m_RefreshManager.RequestRefresh("preview_spine_refresh", () =>
            {
                try
                {
                    MultiPathPreviewRenderer.MarkCreatorDirty(Target, true, true, false);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Spine refresh failed: {ex.Message}");
                }
            }, forceImmediate);
        }

        /// <summary>
        ///     仅请求网格(Mesh)刷新：用于依赖最新脊线完成网格重算的场景，不触发材质重建。
        /// </summary>
        public void RequestMeshRefresh(bool forceImmediate = false)
        {
            m_RefreshManager.RequestRefresh("preview_mesh_refresh", () =>
            {
                try
                {
                    MultiPathPreviewRenderer.MarkCreatorDirty(Target, false, true, false);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Mesh refresh failed: {ex.Message}");
                }
            }, forceImmediate);
        }

        /// <summary>
        ///     仅请求材质(Materials)刷新：不重建脊线与网格，适用于外观参数变化。
        /// </summary>
        public void RequestMaterialsRefresh(bool forceImmediate = false)
        {
            m_RefreshManager.RequestRefresh("preview_materials_refresh", () =>
            {
                try
                {
                    MultiPathPreviewRenderer.MarkCreatorDirty(Target, false, false, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Materials refresh failed: {ex.Message}");
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
        ///     判断 PathCreator 当前状态是否合法，供外部快速查询。
        /// </summary>
        public bool IsPathValid() => Target && Target.IsValidState();

        /// <summary>
        ///     标记预览为脏，并请求刷新。
        /// </summary>
        public void MarkDirty(bool forceImmediate = true)
        {
            // 现在使用全局多路径预览系统，直接标记为脏
            RequestPreviewRefresh(forceImmediate);
            RequestSceneViewRefresh(forceImmediate);
        }

        public PathEditorHandles.HandleDrawContext CreateHandleContext()
        {
            // 统一注入共享的预览线渲染器，并启用 GPU 模式
            var mgr = MultiPathPreviewRenderer.GetManagerForCreator(Target);
            var lr = mgr != null ? mgr.GetSharedLineRenderer() : null;
            lr?.SetUseGpu(true);

            return new PathEditorHandles.HandleDrawContext
            {
                Creator = Target,
                HeightProvider = HeightProvider,
                LatestSpine = null, // 不再依赖本地预览管理器
                IsDragging = IsDraggingHandle,
                HoveredPointIndex = HoveredPointIdx,
                HoveredSegmentIndex = HoveredSegmentIdx,
                LineRenderer = lr // 注入共享实例
            };
        }

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
