namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    ///     运行时的预览线参数配置对象，仅包含数据，不依赖 Editor。
    ///     通过编辑器侧读取项目设置后注入到 PreviewLineRenderer。
    /// </summary>
    public readonly struct PreviewLineConfig
    {
        public float AAWidthPx { get; }
        public float CapAAWidthPx { get; }
        public float SeamScale { get; }
        public int CapType { get; } // 0=None, 1=Linear, 2=Reserved
        public float DefaultDashPixels { get; }
        public bool FastDashed { get; }

        public bool IsValid => AAWidthPx > 0f && CapAAWidthPx > 0f && SeamScale > 0f && DefaultDashPixels > 0f;

        public PreviewLineConfig(float aaWidthPx, float capAaWidthPx, float seamScale, int capType, float defaultDashPixels, bool fastDashed)
        {
            AAWidthPx = aaWidthPx;
            CapAAWidthPx = capAaWidthPx;
            SeamScale = seamScale;
            CapType = capType;
            DefaultDashPixels = defaultDashPixels;
            FastDashed = fastDashed;
        }

        public static PreviewLineConfig Default => new PreviewLineConfig(2.0f, 4.0f, 1.0f, 1, 8.0f, false);
    }
}
