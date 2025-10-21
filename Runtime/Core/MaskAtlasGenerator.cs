using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace __temp.MrPathV2._2.Runtime.Core
{
    /// <summary>
    /// Generates a 2-D mask atlas containing the 1-D mask lookup for every blend layer *and*
    /// for multiple longitudinal samples along the path.
    /// 
    /// Layout:
    /// ‑ The atlas width represents horizontal samples across the path (-1 … +1 in canonical UV).
    /// ‑ The atlas height is <c>layerCount * pathSamples</c>. Each layer occupies a vertical slice of
    ///   <c>pathSamples</c> rows. Within each slice, <c>py</c> (0 … pathSamples-1) maps to
    ///   <c>pathProgress = py / (pathSamples-1)</c>.
    /// 
    /// Shaders and CPU jobs should sample the atlas like so:
    ///     u = acrossRoad;                    // 0 … 1
    ///     v = (layerIndex * pathSamples + pathProgress * (pathSamples-1) + 0.5) * _AtlasInvHeight;
    /// 
    /// The atlas is stored as an <see cref="TextureFormat.R8"/> with bilinear filtering and clamp wrap mode.
    /// </summary>
    public static class MaskAtlasGenerator
    {
        /// <summary>
        /// Builds or updates a mask atlas texture.
        /// </summary>
        /// <param name="reuse">Optional existing texture that can be reused to avoid allocations.</param>
        /// <param name="layers">List of layers to bake, in draw order.</param>
        /// <param name="worldWidth">World width (metres) of the path section to sample across.</param>
        /// <param name="pathLength">Total path length (metres) used for longitudinal UV calculations.</param>
        /// <param name="baseResolution">Horizontal resolution (pixels) for each row in the atlas.</param>
        public static Texture2D BuildMaskAtlas(
            Texture2D reuse,
            IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth,
            float pathLength = 100f,
            int baseResolution = 256)
        {
            using (ProfilingMarkers.MaskAtlasGenerator_Build.Auto())
            {
                // ------------------------------------------------------------------
                // Early-out: no layers – return 1×1 white texture so downstream code
                // always has something to sample without special-casing null.
                // ------------------------------------------------------------------
                if (layers == null || layers.Count == 0)
                {
                    if (reuse == null || reuse.width != 1 || reuse.height != 1 || reuse.format != TextureFormat.R8)
                    {
                        if (reuse != null) Object.DestroyImmediate(reuse);
                        reuse = new Texture2D(1, 1, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp };
                    }

                    reuse.SetPixel(0, 0, Color.white);
                    reuse.Apply(false, false);
                    return reuse;
                }

                // ------------------------------------------------------------------
                // Determine atlas dimensions
                // ------------------------------------------------------------------
                int layerCount  = layers.Count;
                int width       = Mathf.Clamp(baseResolution, 16, 1024); // across-road samples

                // Number of longitudinal samples along the path direction per layer.
                // TODO: expose as a user/recipe setting if needed.
                const int pathSamples = 64;

                int height = layerCount * pathSamples;

                // ------------------------------------------------------------------
                // Allocate or reuse texture
                // ------------------------------------------------------------------
                bool needCreate =
                    reuse == null ||
                    reuse.width  != width ||
                    reuse.height != height ||
                    reuse.format != TextureFormat.R8;

                if (needCreate)
                {
                    if (reuse != null) Object.DestroyImmediate(reuse);

                    reuse = new Texture2D(width, height, TextureFormat.R8, false, true)
                    {
                        wrapMode  = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        name = "MrPath_MaskAtlas"
                    };
                }

                // ------------------------------------------------------------------
                // Bake masks into atlas
                // ------------------------------------------------------------------
                Color32[] pixels = new Color32[width * height];

                for (int li = 0; li < layerCount; li++)
                {
                    var layer = layers[li];
                    if (layer.opacity <= 0f) continue;

                    for (int py = 0; py < pathSamples; py++)
                    {
                        float pathProgress = pathSamples == 1 ? 0f : py / (float)(pathSamples - 1); // 0 … 1

                        for (int x = 0; x < width; x++)
                        {
                            float pos = Mathf.Lerp(-1f, 1f, x / (float)(width - 1));

                            float v = PreviewPipelineUtility.EvaluateMask(
                                pos,
                                pathProgress,
                                worldWidth,
                                pathLength,
                                layer.mask) * layer.opacity;

                            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

                            int row = li * pathSamples + py;
                            pixels[row * width + x] = new Color32(b, 0, 0, 255);
                        }
                    }
                }

                // Upload to GPU
                reuse.SetPixels32(pixels);
                reuse.Apply(false, false);
                return reuse;
            }
        }
    }
}