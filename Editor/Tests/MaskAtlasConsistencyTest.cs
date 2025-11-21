using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Editor.Tests
{
    public class MaskAtlasConsistencyTest
    {
        private static (float maxErr, float mae) CompareAtlasWithMask(BlendMaskBase mask, int width, int pathSamples, float worldWidth, float pathLength)
        {
            var layers = new List<PreviewPipelineUtility.PreviewLayerInfo>
            {
                new PreviewPipelineUtility.PreviewLayerInfo(null, Vector2.one, Vector2.zero, Color.white, 1f, BlendMode.Normal, mask)
            };

            Texture2D atlas = null;
            atlas = MaskAtlasGenerator.BuildMaskAtlas(atlas, layers, worldWidth, pathLength, width, 0f, pathSamples);

            var total = width * pathSamples;
            var sumAbs = 0f;
            var maxAbs = 0f;

            for (var py = 0; py < pathSamples; py++)
            {
                var progress = py / Mathf.Max(1f, pathSamples - 1f);
                for (var px = 0; px < width; px++)
                {
                    var across01 = px / Mathf.Max(1f, width - 1f);
                    var across = across01 * 2f - 1f;
                    var expected = Mathf.Clamp01(mask.Evaluate(across, progress, worldWidth, pathLength));
                    var c = atlas.GetPixel(px, py);
                    var actual = c.r;
                    var err = Mathf.Abs(actual - expected);
                    sumAbs += err;
                    if (err > maxAbs) maxAbs = err;
                }
            }

            var mae = sumAbs / Mathf.Max(1, total);
            return (maxAbs, mae);
        }

        [Test]
        public void FbmAtlas_MatchesMaskEvaluate_SmallGrid()
        {
            var m = ScriptableObject.CreateInstance<PerlinNoiseMask>();
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = 13f;
            m.octaves = 3;
            m.lacunarity = 2f;
            m.gain = 0.5f;
            m.seed = 123.45f;
            m.smooth = 0.1f;

            var (maxErr, mae) = CompareAtlasWithMask(m, 64, 16, 12f, 150f);
            Assert.LessOrEqual(maxErr, 1e-3f, $"Max error too high: {maxErr}, MAE: {mae}");
        }

        [Test]
        public void StripeAtlas_MatchesMaskEvaluate_SmallGrid()
        {
            var m = ScriptableObject.CreateInstance<StripeNoiseMask>();
            m.noiseScale = new Vector2(1.2f, 0.8f);
            m.uniformScale = false;
            m.rotationDeg = -20f;
            m.period = 8f;
            m.jitter = 0.25f;
            m.seed = 77f;
            m.smooth = 0.05f;

            var (maxErr, mae) = CompareAtlasWithMask(m, 64, 16, 10f, 120f);
            Assert.LessOrEqual(maxErr, 1e-3f, $"Max error too high: {maxErr}, MAE: {mae}");
        }
    }
}
