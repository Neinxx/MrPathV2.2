using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using UnityEngine;
using __temp.MrPathV2._2.Runtime.Core; // for GpuMaskParamsData
using Sirenix.OdinInspector;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise Mask")]
    public class NoiseMask : ProceduralMaskBase
    {
        // XY维度的无量纲缩放（类似 ShaderGraph 的 Scale），用于在米制 tiling 之外做细调
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

        /// <summary>
        /// Evaluate mask value at the given horizontal position and path progress.
        /// </summary>
        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            // 基于米制 tiling 的UV（主频率），再乘以 noiseScale（细调）并应用旋转
            float u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            float v = TransformPathPosition(pathProgress, pathLength);

            Vector2 scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            Vector2 uv = new Vector2(u * Mathf.Max(1e-5f, scale.x), v * Mathf.Max(1e-5f, scale.y));

            float rad = rotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            Vector2 ruv = new Vector2(uv.x * cos - uv.y * sin, uv.x * sin + uv.y * cos);

            // 用 seed 产生确定性的偏移，保证随机但可复现
            float sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            float sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            Vector2 seedOffset = new Vector2(sx - Mathf.Floor(sx), sy - Mathf.Floor(sy));
            ruv += seedOffset;

            // fBm 采样（Perlin 的多层叠加）：支持 Octaves / Lacunarity / Gain
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
            float noise = (norm > 1e-5f) ? (sum / norm) : 0f; // 归一化到 0..1

            float rawValue = noise * strength;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }

        // --- GPU 参数打包 ---
        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            dst.MaskType = 2; // MASK_TYPE_NOISE
            dst.Strength = Mathf.Max(0f, strength);

            // 保持 tiling 原值（HLSL 已做安全处理）
            dst.NoiseParams.Strength = Mathf.Max(0f, strength);
            dst.NoiseParams.Seed = seed;
            dst.NoiseParams.Tiling = tiling;
            dst.NoiseParams.Offset = offset;
            dst.NoiseParams.OverallScale = overallScale;
            dst.NoiseParams.Smooth = smooth;
            // 统一缩放时将XY锁定为相同值
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