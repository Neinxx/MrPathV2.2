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
                 "GPU_Compute: 速度最快，利用 GPU 加速，但需要 Compute Shader 支持且可能对显卡有要求。")]
        public PaintTerrainCommand.PaintingBackend paintingBackend = PaintTerrainCommand.PaintingBackend.CPUCompute; // 默认使用 CPU

        [Tooltip("自动切换到 GPU 绘制的像素阈值。当绘制区域超过此像素数时，将自动选用 GPU 后端（如果支持）。设置为 0 可禁用自动切换。")]
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
        [Header("GPU 调试 (Compute Shader)")]
        [Tooltip("Compute 调试模式：0=正常；1=Mask UV；2=边缘衰减")]
        [Range(0, 2)]
        public int gpuDebugMode;

        [Tooltip("道路轮廓遮罩门槛，越高越严格，建议 0.5–0.95")]
        [Range(0f, 1f)]
        public float gpuMaskThreshold = 0.5f;

        [Tooltip("是否用下列数值覆盖边缘过渡宽度（单位：米）")]
        public bool gpuOverrideEdgeWidth;

        [Tooltip("用于覆盖的边缘过渡宽度（米）")]
        [Min(0.001f)]
        public float gpuEdgeWidthWorld = 1.0f;
    }
}
