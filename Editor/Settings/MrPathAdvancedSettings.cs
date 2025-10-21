// 文件: Editor/Settings/MrPathAdvancedSettings.cs

using __temp.MrPathV2._2.Editor.Terrain;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Strategies;
using UnityEngine;
// 确保可以访问 PaintTerrainCommand (如果 PaintingBackend 枚举定义在那里)
// ReSharper disable GrammarMistakeInComment

namespace __temp.MrPathV2._2.Editor.Settings // Or Editor.Settings
{
    /// <summary>
    /// 将工厂注入、策略覆盖等不常用但重要的设置隔离存放。
    /// </summary>
    public class MrPathAdvancedSettings : ScriptableObject
    {
        [Header("策略设置 (Strategy Settings)")]
        // [Tooltip("【已弃用/仅参考】默认路径策略不再由此处控制，请在 PathStrategyRegistry 中配置。")]
        // public PathStrategy defaultStrategy; // 保留旧字段并标记，避免数据丢失
        //
        // [Tooltip("【已弃用/仅参考】可用策略列表不再由此处控制，请在 PathStrategyRegistry 中配置。")]
        // public PathStrategy[] availableStrategies; // 保留旧字段并标记

        [Tooltip("Bezier 曲线所使用的策略实例")]
        public BezierStrategy bezierStrategy;

        [Tooltip("CatmullRom 曲线所使用的策略实例")]
        public CatmullRomStrategy catmullRomStrategy;

        // --- 地形绘制后端设置 ---
        [Header("地形绘制 (Terrain Painting)")]
        [Tooltip("选择地形纹理绘制的后端：\n" +
                 "CPU_Job_TwoPass: 兼容性好，性能优于旧版 Job，但仍受 CPU 限制。\n" +
                 "GPU_Compute: 速度最快，利用 GPU 加速，但需要 Compute Shader 支持且可能对显卡有要求。")]
        public PaintTerrainCommand.PaintingBackend paintingBackend = PaintTerrainCommand.PaintingBackend.CPU_Job_TwoPass; // 默认使用 CPU

        [Header("性能与调试")]
        [Tooltip("启用内存跟踪器 (MemoryTracker)，会带来少量性能开销，建议仅在开发或调试时开启。")]
        public bool enableMemoryTracking; // 默认关闭
    }
}