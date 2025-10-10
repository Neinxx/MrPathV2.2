// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathAdvancedSettings.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 将工厂注入、策略覆盖等不常用但重要的设置隔离存放。
    /// </summary>
    public class MrPathAdvancedSettings : ScriptableObject
    {
        [Header("依赖工厂设置 (Dependency Injection)")]
        [Tooltip("用于创建预览生成器(IPreviewGenerator)的工厂")]
        public PreviewGeneratorFactory previewGeneratorFactory;

        [Tooltip("用于创建高度提供者(IHeightProvider)的工厂")]
        public HeightProviderFactory heightProviderFactory;

        [Tooltip("用于创建预览材质管理器(PreviewMaterialManager)的工厂")]
        public PreviewMaterialManagerFactory previewMaterialManagerFactory;

        [Header("策略映射覆盖 (Strategy Overrides)")]
        [Tooltip("覆盖 Bezier 曲线的策略资产（可为空则使用注册表）")]
        public PathStrategy bezierStrategy;

        [Tooltip("覆盖 Catmull-Rom 曲线的策略资产（可为空则使用注册表）")]
        public PathStrategy catmullRomStrategy;
    }
}