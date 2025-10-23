using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using __temp.MrPathV2._2.Runtime.Core; // for GpuMaskParamsData
using UnityEngine;
using Sirenix.OdinInspector;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Perlin Noise Mask")]
    public class PerlinNoiseMask : ProceduralMaskBase
    {
        [Header("Noise Settings / Scale & Rotation")]
        [Tooltip("二维噪声的无量纲缩放。小值=图案更大；大值=细节更密。")]
        [OnValueChanged(nameof(OnNoiseScaleChanged))]
        public Vector2 noiseScale = new Vector2(1f, 1f);

        [Header("Uniform Scale")]
        [Tooltip("锁定XY统一缩放，调整X时同步Y。")]
        [OnValueChanged(nameof(OnUniformScaleToggled))]
        public bool uniformScale = true;

        [Tooltip("噪声UV的旋转角度（度）")]
        [Range(-180f, 180f)] public float rotationDeg = 0f;

        [Header("Noise Settings / fBm Detail")]
        [Tooltip("叠加的噪声层数（fBm Octaves）")]
        [Range(1, 8)] public int octaves = 1;

        [Tooltip("每层频率的倍增（Lacunarity，通常为2）")]
        [Range(1f, 4f)] public float lacunarity = 2f;

        [Tooltip("每层振幅的衰减（Gain/Persistence，0..1）")]
        [Range(0f, 1f)] public float gain = 0.5f;

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            float u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            float v = TransformPathPosition(pathProgress, pathLength);

            Vector2 scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            Vector2 uv = new Vector2(u * Mathf.Max(1e-5f, scale.x), v * Mathf.Max(1e-5f, scale.y));
            float rad = rotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            Vector2 ruv = new Vector2(uv.x * cos - uv.y * sin, uv.x * sin + uv.y * cos);

            float sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            float sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            Vector2 seedOffset = new Vector2(sx - Mathf.Floor(sx), sy - Mathf.Floor(sy));
            ruv += seedOffset;

            float amplitude = 1f;
            float frequency = 1f;
            float sum = 0f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(ruv.x * frequency, ruv.y * frequency) * amplitude;
                norm += amplitude;
                frequency *= Mathf.Max(1f, lacunarity);
                amplitude *= Mathf.Clamp01(gain);
            }
            float noise = (norm > 1e-5f) ? (sum / norm) : 0f;

            float rawValue = noise * strength;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            return Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);
        }

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
            dst.NoiseParams.AlgorithmId = (int)NoiseAlgorithmId.Perlin;
        }

        private void OnNoiseScaleChanged()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
        }

        private void OnUniformScaleToggled()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
        }

        private void OnValidate()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
        }
    }
}