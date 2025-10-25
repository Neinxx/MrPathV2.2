using System.Collections.Generic;
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Jobs;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     统一 CPU/GPU/Terrain 三端的混合与采样逻辑，确保所见即所得。
    ///     该工具放在 Runtime/Core，Editor 程序集也能引用。
    /// </summary>
    public static class PreviewPipelineUtility
    {

        /// <summary>
        ///     计算层贴图在道路宽度 worldWidth 下的缩放系数。
        ///     委托给已有的 LayerTilingUtility。
        /// </summary>
        public static Vector2 CalcLayerTiling(float worldWidth, TerrainLayer layer) => LayerTilingUtility.CalcLayerTiling(worldWidth, layer);

        /// <summary>
        ///     在 -1..1 范围 pos 采样遮罩值。
        ///     若 mask 为空，返回 1。
        /// </summary>
        public static float EvaluateMask(float pos, float pathProgress, float worldWidth, float pathLength, BlendMaskBase mask) =>
            // 当未指定遮罩时返回 1，表示全权重，将直接使用图层自身的不透明度
            mask == null ? 1f : Mathf.Clamp01(mask.Evaluate(pos, pathProgress, worldWidth, pathLength));

        /// <summary>
        ///     兼容旧接口：若未提供 pathProgress，则使用 0.5f 作为默认值（路径中点）
        /// </summary>
        public static float EvaluateMask(float pos, float worldWidth, float pathLength, BlendMaskBase mask) => EvaluateMask(pos, 0.5f, worldWidth, pathLength, mask);

        /// <summary>
        ///     通用通道混合，直接复用 TerrainJobsUtility.Blend 以保持一致性。
        /// </summary>
        public static float BlendChannel(float baseValue, float layerValue, BlendMode mode) => TerrainJobsUtility.Blend(baseValue, layerValue, (int)mode);

        /// <summary>
        ///     根据层信息生成或更新 RGBA32 的 256×1 LUT：RGBA 分别对应前四层的混合权重。
        /// </summary>
        /// <param name="reuse">可重用纹理，若尺寸或格式不符则重新创建。</param>
        /// <param name="layers">最多取前 4 层。</param>
        /// <param name="worldWidth">道路宽度，用于 EvaluateMask。</param>
        /// <param name="pathLength">道路长度，用于 EvaluateMask。</param>
        /// <returns>生成好的 Texture2D。</returns>
        public static Texture2D BuildMaskAtlas(Texture2D reuse, IList<PreviewLayerInfo> layers, float worldWidth, float pathLength = 100f, int baseResolution = 256) => MaskAtlasGenerator.BuildMaskAtlas(reuse, layers, worldWidth, pathLength, baseResolution);

        /// <summary>
        ///     描述一层用于预览或绘制的所有信息。
        /// </summary>
        public struct PreviewLayerInfo
        {
            public Texture2D texture;
            public Vector2 tiling; // 缩放
            public Vector2 offset; // 偏移（可选，默认 0）
            public Color tint; // 颜色调整（可选，默认 white）
            public float opacity; // 0..1
            public BlendMode blendMode; // 与 PathTool.Data.BlendMode 保持一致
            public BlendMaskBase mask; // 可为 null

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
    }
}
