using UnityEngine;
using __temp.MrPathV2.Runtime.Core.Noise;
// for GpuMaskParamsData

namespace __temp.MrPathV2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Perlin Noise Mask")]
    public class PerlinNoiseMask : ProceduralMaskBase
    {
        [Header("Noise Settings / Scale & Rotation")]
        [Tooltip("二维噪声的无量纲缩放。小值=图案更大；大值=细节更密。")]
        public Vector2 noiseScale = new Vector2(1f, 1f);

        [Header("Uniform Scale")]
        [Tooltip("锁定XY统一缩放，调整X时同步Y。")]
        public bool uniformScale = true;

        [Tooltip("噪声UV的旋转角度（度）")]
        [Range(-180f, 180f)] public float rotationDeg;

        [Header("Noise Settings / fBm Detail")]
        [Tooltip("叠加的噪声层数（fBm Octaves）")]
        [Range(1, 8)] public int octaves = 1;

        [Tooltip("每层频率的倍增（Lacunarity，通常为2）")]
        [Range(1f, 4f)] public float lacunarity = 2f;

        [Tooltip("每层振幅的衰减（Gain/Persistence，0..1）")]
        [Range(0f, 1f)] public float gain = 0.5f;

        [Header("Smoothing / 非对称 Edge1/Edge2")]
        [Tooltip("启用后使用非对称阈值进行平滑（SmoothStep(edgeLow, edgeHigh)）。关闭则使用对称 smooth 参数。")]
        public bool useAsymmetricEdges = false;

        [Range(0f, 1f), Tooltip("下阈值（Edge1）。建议 < EdgeHigh。")]
        public float edgeLow = 0.25f;

        [Range(0f, 1f), Tooltip("上阈值（Edge2）。建议 > EdgeLow。")]
        public float edgeHigh = 0.75f;

        private void OnValidate()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
            edgeLow = Mathf.Clamp01(edgeLow);
            edgeHigh = Mathf.Clamp01(edgeHigh);
            if (edgeHigh < edgeLow)
            {
                var t = edgeLow; edgeLow = edgeHigh; edgeHigh = t;
            }
        }

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            var u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            var v = TransformPathPosition(pathProgress, pathLength);

            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            var uv = new Vector2(u * Mathf.Max(1e-5f, scale.x), v * Mathf.Max(1e-5f, scale.y));
            var rad = rotationDeg * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);
            var ruv = new Vector2(uv.x * cos - uv.y * sin, uv.x * sin + uv.y * cos);

            var sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            var sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            var seedOffset = new Vector2(sx - Mathf.Floor(sx), sy - Mathf.Floor(sy));
            ruv += seedOffset;

            var amplitude = 1f;
            var frequency = 1f;
            var sum = 0f;
            var norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                var s = NoiseLutProvider.Sample01(ruv.x * frequency, ruv.y * frequency);
                sum += s * amplitude;
                norm += amplitude;
                frequency *= Mathf.Max(1f, lacunarity);
                amplitude *= Mathf.Clamp01(gain);
            }
            var noise = norm > 1e-5f ? sum / norm : 0f;

            var rawValue = noise * strength;
            return ApplyNoiseSmoothing(Mathf.Clamp01(rawValue));
        }

        private float ApplyNoiseSmoothing(float maskValue)
        {
            maskValue *= overallScale;
            if (useAsymmetricEdges)
            {
                var e0 = Mathf.Clamp01(edgeLow);
                var e1 = Mathf.Clamp01(edgeHigh);
                if (e1 < e0) { var t = e0; e0 = e1; e1 = t; }
                if (maskValue <= e0) return 0f;
                if (maskValue >= e1) return 1f;
                var t2 = (maskValue - e0) / Mathf.Max(1e-6f, e1 - e0);
                var smoothed2 = t2 * t2 * (3f - 2f * t2);
                return Mathf.Clamp01(smoothed2);
            }
            return ApplySmoothing(maskValue);
        }

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength) => Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);

        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            dst.MaskType = 2; // MASK_TYPE_NOISE
            dst.Strength = Mathf.Max(0f, strength);

            dst.NoiseParams.Strength = Mathf.Max(0f, strength);
            dst.NoiseParams.Seed = seed;
            dst.NoiseParams.Tiling = tiling;
            dst.NoiseParams.Offset = offset;
            dst.NoiseParams.OverallScale = overallScale;
            dst.NoiseParams.Smooth = smooth;
            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            dst.NoiseParams.NoiseScale = scale;
            dst.NoiseParams.RotationRad = rotationDeg * Mathf.Deg2Rad;
            dst.NoiseParams.Octaves = octaves;
            dst.NoiseParams.Lacunarity = lacunarity;
            dst.NoiseParams.Gain = gain;
            dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
            dst.NoiseParams.EdgeLow = edgeLow;
            dst.NoiseParams.EdgeHigh = edgeHigh;
        }

        private void OnNoiseScaleChanged()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
        }

        private void OnUniformScaleToggled()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
        }
    }
}
