using UnityEngine;

namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    ///     现代预览线风格主题（可注入）。
    ///     独立于具体实现，供 PreviewLineRenderer 通过 provider 使用。
    /// </summary>
    public static class ModernPreviewLineStyles
    {
        public static PreviewLineRenderer.LineStyle Get(PreviewLineRenderer.LineType type)
        {
            switch (type)
            {
                case PreviewLineRenderer.LineType.PathCurve:
                    return new PreviewLineRenderer.LineStyle
                    {
                        color = new Color(0.10f, 0.80f, 0.95f, 0.95f), // 现代青蓝
                        thickness = 2.5f,
                        dashed = false,
                        dashSize = 8f,
                        antiAliased = true
                    };
                case PreviewLineRenderer.LineType.ControlLine:
                    return new PreviewLineRenderer.LineStyle
                    {
                        color = new Color(0.90f, 0.90f, 0.90f, 0.35f), // 低饱和虚线
                        thickness = 1.25f,
                        dashed = true,
                        dashSize = 6f,
                        antiAliased = false
                    };
                case PreviewLineRenderer.LineType.WireframeEdge:
                    return new PreviewLineRenderer.LineStyle
                    {
                        color = new Color(0.56f, 0.58f, 0.62f, 0.50f),
                        thickness = 1.0f,
                        dashed = false,
                        dashSize = 4f,
                        antiAliased = false
                    };
                case PreviewLineRenderer.LineType.DebugLine:
                    return new PreviewLineRenderer.LineStyle
                    {
                        color = new Color(1.0f, 0.25f, 0.4f, 1.0f), // 鲜明提示色
                        thickness = 2.0f,
                        dashed = false,
                        dashSize = 4f,
                        antiAliased = true
                    };
                case PreviewLineRenderer.LineType.HandleConnection:
                    return new PreviewLineRenderer.LineStyle
                    {
                        color = new Color(1.0f, 0.84f, 0.30f, 0.80f), // 琥珀点缀
                        thickness = 1.5f,
                        dashed = true,
                        dashSize = 5f,
                        antiAliased = true
                    };
                default:
                    return PreviewLineRenderer.LineStyle.Default;
            }
        }

        // 委托提供器，满足依赖注入语义
        public static System.Func<PreviewLineRenderer.LineType, PreviewLineRenderer.LineStyle> Provider => Get;
    }
}
