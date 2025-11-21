// 文件: Editor/Settings/MrPathAdvancedSettings.cs

using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Strategies;
using UnityEngine;
// 确保可以访问 PaintTerrainCommand (如果 PaintingBackend 枚举定义在那里)
// ReSharper disable GrammarMistakeInComment

namespace MrPathV2.Editor.Settings // Or Editor.Settings
{
    /// <summary>
    ///     将工厂注入、策略覆盖等不常用但重要的设置隔离存放。
    /// </summary>
    public class MrPathAdvancedSettings : ScriptableObject
    {
        // --- 预览线（GPU）设置 ---
        [Header("预览线 (GPU Preview Lines)")]
        [Tooltip("屏幕空间边缘抗锯齿宽度（像素）。用于预览线条边缘的平滑处理。")]
        [Range(0.5f, 8f)]
        public float previewAAWidthPixels = 2.0f;

        [Tooltip("屏幕空间端点融合宽度（像素）。未连接的端点将按此宽度做透明过渡。")]
        [Range(0.5f, 16f)]
        public float previewCapAAWidthPixels = 4.0f;

        [Tooltip("默认虚线段长度（像素）。当样式未显式提供 dashSize 时使用。")]
        [Range(1f, 64f)]
        public float previewDefaultDashPixels = 8.0f;
        public bool previewFastDashedMode;

        public enum PreviewCapType
        {
            None = 0,
            Linear = 1
            // Round = 2 // 如需圆形端帽可后续扩展
        }

        [Tooltip("预览线端帽类型：None=无融合；Linear=线性融合（默认）")]
        public PreviewCapType previewCapType = PreviewCapType.Linear;

        [Tooltip("端帽融合宽度缩放系数。>1 更柔和，<1 更硬。")]
        [Range(0.25f, 4f)]
        public float previewSeamScale = 1.0f;

        [Tooltip("屏幕自适应采样的最大像素步长（越大采样点越少，性能更好；越小越平滑）。用于编辑器 CPU 预览曲线的自适应采样。")]
        [Range(2f, 24f)]
        public float previewMaxPixelStep = 6.0f;

        [Header("策略设置 (Strategy Settings)")]
        // [Tooltip("【已弃用/仅参考】默认路径策略不再由此处控制，请在 PathStrategyRegistry 中配置。")]
        // public PathStrategy defaultStrategy; // 保留旧字段并标记，避免数据丢失
        //
        // [Tooltip("【已弃用/仅参考】可用策略列表不再由此处控制，请在 PathStrategyRegistry 中配置。")]
        // public PathStrategy[] availableStrategies; // 保留旧字段并标记
        [Tooltip("Bezier 曲线所使用的策略实例")] public BezierStrategy bezierStrategy;

        [Tooltip("CatmullRom 曲线所使用的策略实例")] public CatmullRomStrategy catmullRomStrategy;

        // --- 地形绘制后端设置 ---
        [Header("地形绘制 (Terrain Painting)")]
        [Tooltip("选择地形纹理绘制的后端：\n" +
                 "CPU_Job_TwoPass: 兼容性好，性能优于旧版 Job，但仍受 CPU 限制。\n" +
                 "GPU_Compute: 当前版本未开放。")]
        public PaintTerrainCommand.PaintingBackend paintingBackend = PaintTerrainCommand.PaintingBackend.CPUCompute; // 默认使用 CPU

        [Tooltip("自动切换到 GPU 绘制的像素阈值。当前版本禁用GPU，仅用于占位或未来扩展。设置为 0 可禁用自动切换。")]
        [Min(0)]
        public int gpuAutoSwitchThreshold = 256 * 256; // 默认 256x256 像素

        [Header("性能与调试")]
        [Tooltip("启用内存跟踪器 (MemoryTracker)，会带来少量性能开销，建议仅在开发或调试时开启。")]
        public bool enableMemoryTracking; // 默认关闭

        [Header("预览与Basemap")]
        [Tooltip("实时 GPU 预览结束后是否触发 Basemap 重建（读回并调用 TerrainData.SetAlphamaps）。开启后远景不会回退到旧 Basemap，但会增加少量 CPU/GPU 同步与读回开销。")]
        public bool rebuildBasemapInRealtimePreview;

        [Tooltip("Basemap 重建时在对齐矩形外扩的安全边距（像素），用于读回与回写，避免边界失配。建议为线程组大小的倍数，例如 8、16。")]
        public int basemapSafetyMarginPixels = 8;

        // --- GPU 调试 (Compute Shader) ---
        // GPU 调试项：当前版本禁用，隐藏以简化界面
        [HideInInspector] public int gpuDebugMode;
        [HideInInspector] public float gpuMaskThreshold = 0.5f;
        [HideInInspector] public bool gpuOverrideEdgeWidth;
        [HideInInspector] public float gpuEdgeWidthWorld = 1.0f;

        private void OnValidate()
        {
            // 提前返回：无需校验时直接返回
            // 统一禁用 GPU 后端，防止误保存旧值
            if (paintingBackend == PaintTerrainCommand.PaintingBackend.GPUCompute)
            {
                paintingBackend = PaintTerrainCommand.PaintingBackend.CPUCompute;
                Debug.LogWarning("[MrPath] GPU绘制未开放，已自动切换到 CPU 后端。");
            }

            previewAAWidthPixels = Mathf.Clamp(previewAAWidthPixels, 0.5f, 8f);
            previewCapAAWidthPixels = Mathf.Clamp(previewCapAAWidthPixels, 0.5f, 16f);
            previewDefaultDashPixels = Mathf.Clamp(previewDefaultDashPixels, 1f, 64f);
            previewSeamScale = Mathf.Clamp(previewSeamScale, 0.25f, 4f);
            previewMaxPixelStep = Mathf.Clamp(previewMaxPixelStep, 2f, 24f);
        }
    }
}
