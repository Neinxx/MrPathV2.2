using System.Collections.Generic;
using UnityEngine;


namespace MrPathV2
{
    /// <summary>
    /// 统一 CPU/GPU/Terrain 三端的混合与采样逻辑，确保所见即所得。
    /// 该工具放在 Runtime/Core，Editor 程序集也能引用。
    /// </summary>
    public static class PreviewPipelineUtility
    {
        /// <summary>
        /// 描述一层用于预览或绘制的所有信息。
        /// </summary>
        public struct PreviewLayerInfo
        {
            public Texture2D texture;
            public Vector2 tiling;      // 缩放
            public Vector2 offset;      // 偏移（可选，默认 0）
            public Color tint;          // 颜色调整（可选，默认 white）
            public float opacity;       // 0..1
            public BlendMode blendMode; // 与 PathTool.Data.BlendMode 保持一致
            public BlendMaskBase mask;  // 可为 null

            public PreviewLayerInfo(Texture2D tex, Vector2 tiling, Vector2 offset, Color tint, float opacity, BlendMode blend, BlendMaskBase m)
            {
                texture = tex;
                this.tiling = tiling;
                this.offset = offset;
                this.tint = tint;
                this.opacity = opacity;
                blendMode = blend;
                mask = m;
            }
        }

        /// <summary>
        /// 计算层贴图在道路宽度 worldWidth 下的缩放系数。
        /// 委托给已有的 LayerTilingUtility。
        /// </summary>
        public static Vector2 CalcLayerTiling(float worldWidth, TerrainLayer layer)
        {
            return LayerTilingUtility.CalcLayerTiling(worldWidth, layer);
        }

        /// <summary>
        /// 在 -1..1 范围 pos 采样遮罩值。
        /// 若 mask 为空，返回 1。
        /// </summary>
        public static float EvaluateMask(float pos, float worldWidth, BlendMaskBase mask)
        {
            return mask == null ? 1f : Mathf.Clamp01(mask.Evaluate(pos, worldWidth));
        }

        /// <summary>
        /// 通用通道混合，直接复用 TerrainJobsUtility.Blend 以保持一致性。
        /// </summary>
        public static float BlendChannel(float baseValue, float layerValue, BlendMode mode)
        {
            return TerrainJobsUtility.Blend(baseValue, layerValue, (int)mode);
        }

        /// <summary>
        /// 根据层信息生成或更新 RGBA32 的 256×1 LUT：RGBA 分别对应前四层的混合权重。
        /// </summary>
        /// <param name="reuse">可重用纹理，若尺寸或格式不符则重新创建。</param>
        /// <param name="layers">最多取前 4 层。</param>
        /// <param name="worldWidth">道路宽度，用于 EvaluateMask。</param>
        /// <returns>生成好的 Texture2D。</returns>
        public static Texture2D BuildMaskLUT(Texture2D reuse, IList<PreviewLayerInfo> layers, float worldWidth)
        {
            const int RES = 256;
            if (reuse == null || reuse.width != RES || reuse.height != 1 || reuse.format != TextureFormat.RGBA32)
            {
                if (reuse != null) Object.DestroyImmediate(reuse);
                reuse = new Texture2D(RES, 1, TextureFormat.RGBA32, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    name = "MrPath_MaskLUT"
                };
            }

            Color[] pixels = new Color[RES];
            int layerCount = Mathf.Min(4, layers.Count);

            for (int i = 0; i < RES; i++)
            {
                float pos = Mathf.Lerp(-1f, 1f, i / (float)(RES - 1));
                float r = 0, g = 0, b = 0, a = 0;

                for (int li = 0; li < layerCount; li++)
                {
                    var layer = layers[li];
                    if (layer.opacity <= 0f) continue;
                    float v = EvaluateMask(pos, worldWidth, layer.mask) * layer.opacity;
                    switch (li)
                    {
                        case 0: r = BlendChannel(r, v, layer.blendMode); break;
                        case 1: g = BlendChannel(g, v, layer.blendMode); break;
                        case 2: b = BlendChannel(b, v, layer.blendMode); break;
                        case 3: a = BlendChannel(a, v, layer.blendMode); break;
                    }
                }

                // 不在 GPU 预览阶段做归一化，保持与 PaintSplatmapJob 一致
                // 归一化工作应在最终写入地形时统一进行

                pixels[i] = new Color(r, g, b, a);
            }

            reuse.SetPixels(pixels);
            reuse.Apply(false, false);
            return reuse;
        }
    }
}