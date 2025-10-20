using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// Utility to build a 2D mask atlas where every row contains the 1-D mask lookup for a single blend layer.
    /// The atlas is sampled in shaders using UV.x = across-road (0..1) and UV.y = (layerIndex + 0.5) * _AtlasInvHeight.
    /// This replaces the legacy 1-pixel-height RGBA LUT and supports an arbitrary number of layers with flexible resolution.
    /// </summary>
    public static class MaskAtlasGenerator
    {
        /// <summary>
        /// Builds or updates a mask atlas texture.
        /// </summary>
        /// <param name="reuse">Existing texture that can be reused to avoid allocations (can be null).</param>
        /// <param name="layers">PreviewPipelineUtility.PreviewLayerInfo list</param>
        /// <param name="worldWidth">World width (metres) used to sample masks.</param>
        /// <param name="pathLength">Path length (metres) used to sample masks.</param>
        /// <param name="baseResolution">Base horizontal resolution (pixels) for each row, default 256.</param>
        /// <returns>Texture2D atlas containing mask weights per layer in the R channel.</returns>
        public static Texture2D BuildMaskAtlas(Texture2D reuse, IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth, float pathLength = 100f, int baseResolution = 256)
        {
            if (layers == null || layers.Count == 0)
            {
                // Provide 1x1 white texture as fallback
                if (reuse == null || reuse.width != 1 || reuse.height != 1 || reuse.format != TextureFormat.R8)
                {
                    if (reuse != null) Object.DestroyImmediate(reuse);
                    reuse = new Texture2D(1, 1, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp };
                }
                reuse.SetPixel(0, 0, Color.white);
                reuse.Apply(false, false);
                return reuse;
            }

            int layerCount = layers.Count;
            int width = Mathf.Clamp(baseResolution, 16, 1024); // clamp for safety
            int height = layerCount;

            bool needCreate = reuse == null || reuse.width != width || reuse.height != height || reuse.format != TextureFormat.R8;
            if (needCreate)
            {
                if (reuse != null) Object.DestroyImmediate(reuse);
                reuse = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "MrPath_MaskAtlas"
                };
            }

            Color32[] pixels = new Color32[width * height];

            for (int li = 0; li < layerCount; li++)
            {
                var layer = layers[li];
                if (layer.opacity <= 0f) continue;

                for (int x = 0; x < width; x++)
                {
                    float pos = Mathf.Lerp(-1f, 1f, x / (float)(width - 1));
                    float v = PreviewPipelineUtility.EvaluateMask(pos, worldWidth, pathLength, layer.mask) * layer.opacity;
                    v = Mathf.Clamp01(v);
                    byte b = (byte)Mathf.RoundToInt(v * 255f);
                    pixels[li * width + x] = new Color32(b, 0, 0, 255);
                }
            }

            reuse.SetPixels32(pixels);
            reuse.Apply(false, false);
            return reuse;
        }
    }
}