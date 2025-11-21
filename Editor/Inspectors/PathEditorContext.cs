using System;
using MrPathV2.Editor.Preview;
using MrPathV2.Editor.Settings;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Providers;
using MrPathV2.Editor.Input;
using MrPathV2.Runtime.Preview;
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
        private Vector3 m_LastCamPos;
        private Quaternion m_LastCamRot;
        // 共享折线缓存：供Splines风格绘制在各策略中复用，减少采样与GC
        private AdaptivePolylineCache m_PolylineCache;
        private Action<PathChangeCommand> m_OnPathModifiedHandler;

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
            // 初始化折线缓存，并在曲线定义变化时清空
            m_PolylineCache = new AdaptivePolylineCache();
            if (Target)
            {
                Target.CurveDefinitionChanged += m_PolylineCache.Clear;
                m_OnPathModifiedHandler = _ => m_PolylineCache.Clear();
                Target.PathModified += m_OnPathModifiedHandler;
            }
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
            // 统一注入共享的预览线渲染器（CPU-only）
            var mgr = MultiPathPreviewRenderer.GetManagerForCreator(Target);
            var lr = mgr != null ? mgr.GetSharedLineRenderer() : null;
            // 提前返回与一致性：当前版本禁用GPU预览
            lr?.SetUseGpu(false);

            // 从项目高级设置读取屏幕采样步长(像素)，并进行安全的范围约束
            var pxStep = 6f; // 默认兜底
            var adv = m_MrPathProjectSettings ? m_MrPathProjectSettings.advancedSettings : null;
            if (adv)
            {
                pxStep = Mathf.Clamp(adv.previewMaxPixelStep, 2f, 24f);
            }

            // 检测场景相机是否在移动（位移或旋转变化）
            bool camMoving = false;
#if UNITY_EDITOR
            var sceneView = SceneView.currentDrawingSceneView;
            if (sceneView && sceneView.camera)
            {
                var camTf = sceneView.camera.transform;
                var curPos = camTf.position;
                var curRot = camTf.rotation;
                // 使用较小阈值判断移动，以便更敏感地降级绘制
                if ((curPos - m_LastCamPos).sqrMagnitude > 1e-6f || Quaternion.Angle(curRot, m_LastCamRot) > 0.01f)
                {
                    camMoving = true;
                    m_LastCamPos = curPos;
                    m_LastCamRot = curRot;
                }
                else
                {
                    camMoving = false;
                }
            }
#endif

            return new PathEditorHandles.HandleDrawContext
            {
                Creator = Target,
                HeightProvider = HeightProvider,
                LatestSpine = null, // 不再依赖本地预览管理器
                IsDragging = IsDraggingHandle,
                HoveredPointIndex = HoveredPointIdx,
                HoveredSegmentIndex = HoveredSegmentIdx,
                LineRenderer = lr, // 注入共享实例
                PreviewMaxPixelStep = pxStep,
                // 开启 Splines 风格直接绘制，避免预览采样与批次带来的拖拽卡顿
                UseSplinesStyle = true,
                // 拖拽期间仅绘制活动段与邻接段（贴近 Splines 的行为）
                DrawActiveSegmentOnly = IsDraggingHandle,
                DragNeighborRange = 1,
                // 相机移动时降级曲线绘制，保障场景拖动流畅度
                IsCameraMoving = camMoving,
                // 注入分段折线缓存，策略可直接复用缓存点序列
                PolylineCache = m_PolylineCache
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
                if (Target)
                {
                    Target.CurveDefinitionChanged -= m_PolylineCache.Clear;
                    if (m_OnPathModifiedHandler != null)
                    {
                        Target.PathModified -= m_OnPathModifiedHandler;
                        m_OnPathModifiedHandler = null;
                    }
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
#endif
    }
}
