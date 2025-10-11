using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 封装 PathCreatorEditor 依赖的上下文：服务、处理器、预览管理器与状态。
    /// 负责初始化与释放，避免 Editor 类过胖。
    /// </summary>
    public class PathEditorContext
    {
        public PathCreator Target { get; private set; }

        // 服务
        public IPreviewGenerator PreviewGenerator { get; private set; }
        public IHeightProvider HeightProvider { get; private set; }
        public PreviewMaterialManager MaterialManager { get; private set; }

        // 处理器
        public PathInputHandler InputHandler { get; private set; }
        public TerrainOperationHandler TerrainHandler { get; private set; }

        // 预览
        public PathPreviewManager PreviewManager { get; private set; }

        // 场景交互状态
        public int HoveredPointIdx { get; set; } = -1;
        public int HoveredSegmentIdx { get; set; } = -1;
        public bool IsDraggingHandle { get; set; }

        // 地形操作状态（统一）
        // 当前正在执行的操作标识，用于 UI 高亮与禁用逻辑。
        // 由 TerrainOperationHandler 在执行期间设置/清除。
        public string CurrentOperationId { get; set; }

        public void Initialize(PathCreator creator)
        {
            Target = creator;
            if (Target == null) return;

            var projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            var advancedSettings = projectSettings.advancedSettings;
            var appearanceSettings = projectSettings.appearanceDefaults;

            PreviewGenerator = (advancedSettings?.previewGeneratorFactory != null) ? advancedSettings.previewGeneratorFactory.Create() : new PreviewMeshControllerAdapter(new PreviewMeshController());
            HeightProvider = (advancedSettings?.heightProviderFactory != null) ? advancedSettings.heightProviderFactory.Create() : new TerrainHeightProviderAdapter(new TerrainHeightProvider());
            MaterialManager = (advancedSettings?.previewMaterialManagerFactory != null) ? advancedSettings.previewMaterialManagerFactory.Create() : new PreviewMaterialManager();

            InputHandler = new PathInputHandler();
            TerrainHandler = new TerrainOperationHandler(HeightProvider);

            Material splatMaterialTemplate = appearanceSettings?.previewMaterialTemplate;
            PreviewManager = new PathPreviewManager(PreviewGenerator, MaterialManager, splatMaterialTemplate);
        }

        public void Dispose()
        {
            PreviewManager?.Dispose();
            HeightProvider?.Dispose();
            MaterialManager?.Dispose();
            TerrainHandler?.Dispose();

            CurrentOperationId = null;
        }

        public void MarkDirty()
        {
            PreviewManager?.MarkDirty();
            SceneView.RepaintAll();
        }

        public bool IsPathValid()
        {
            return Target != null && Target.profile != null && Target.pathData.KnotCount >= 2;
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
            };
        }

        public void UpdateHoverState(PathEditorHandles.HandleDrawContext context)
        {
            HoveredPointIdx = context.hoveredPointIndex;
            HoveredSegmentIdx = context.hoveredSegmentIndex;
            IsDraggingHandle = Event.current.type == EventType.MouseDrag && Event.current.button == 0 && GUIUtility.hotControl != 0;
        }
    }
}