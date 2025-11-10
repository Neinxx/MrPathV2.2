using MrPathV2.Runtime.Core.Noise;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    /// <summary>
    ///     路肩柏林噪声遮罩：仅在左右两条“路肩”区域内应用柏林噪声，
    ///     支持调节路肩位置与宽度；噪声不会影响其他区域。
    /// </summary>
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Shoulder Perlin Noise Mask")]
    public class ShoulderPerlinNoiseMask : ProceduralMaskBase
    {
        [Header("Shoulder Shape")]
        [Tooltip("路肩宽度占道路总宽度的比例 (0..0.5)。该值越大，两条路肩越宽。")]
        [Range(0f, 0.5f)] public float shoulderWidthRatio = 0.12f;

        [Tooltip("路肩中心相对道路边缘向内的位移比例 (0..1)。0=紧贴边缘，1=移到道路中心。")]
        [Range(0f, 1f)] public float shoulderPositionRatio = 0.1f;

        [Tooltip("路肩强度（与噪声相乘），用于控制路肩区域的整体权重上限。")]
        [Range(0f, 1f)] public float shoulderStrength = 1.0f;

        [Tooltip("路肩边缘的软化距离比例（相对道路宽度）。0=硬边；值越大边缘越柔和。")]
        [Range(0f, 0.3f)] public float edgeFalloff = 0.05f;

        [Tooltip("是否启用左侧路肩")]
        public bool enableLeftShoulder = true;

        [Tooltip("是否启用右侧路肩")]
        public bool enableRightShoulder = true;

        [Header("Perlin Noise / Scale & Rotation")]
        [Tooltip("二维噪声的无量纲缩放。小值=图案更大；大值=细节更密。")]
        public Vector2 noiseScale = new Vector2(1f, 1f);

        [Tooltip("锁定XY统一缩放，调整X时同步Y。")]
        public bool uniformScale = true;

        [Tooltip("噪声UV的旋转角度（度）")]
        [Range(-180f, 180f)] public float rotationDeg;

        [Header("Perlin Noise / fBm Detail")]
        [Tooltip("叠加的噪声层数（fBm Octaves）")] [Range(1, 8)] public int octaves = 3;
        [Tooltip("每层频率的倍增（Lacunarity，通常为2）")] [Range(1f, 4f)] public float lacunarity = 2f;
        [Tooltip("每层振幅的衰减（Gain/Persistence，0..1）")] [Range(0f, 1f)] public float gain = 0.5f;

        [Header("Smoothing / 非对称 Edge1/Edge2")]
        [Tooltip("启用后使用非对称阈值进行平滑（SmoothStep(edgeLow, edgeHigh)）。关闭则使用对称 smooth 参数。")]
        public bool useAsymmetricEdges;
        [Range(0f, 1f)] public float edgeLow = 0.25f;
        [Range(0f, 1f)] public float edgeHigh = 0.75f;

        private void OnValidate()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
            edgeLow = Mathf.Clamp01(edgeLow);
            edgeHigh = Mathf.Clamp01(edgeHigh);
            if (edgeHigh < edgeLow)
            {
                var t = edgeLow;
                edgeLow = edgeHigh;
                edgeHigh = t;
            }
            shoulderWidthRatio = Mathf.Clamp(shoulderWidthRatio, 0f, 0.5f);
            shoulderPositionRatio = Mathf.Clamp01(shoulderPositionRatio);
            edgeFalloff = Mathf.Clamp(edgeFalloff, 0f, 0.3f);
        }

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            // 1) 计算噪声（基于米制 tiling，再乘无量纲 noiseScale 并旋转）
            var u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            var v = TransformPathPosition(pathProgress, pathLength);

            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            var uv = new Vector2(u * Mathf.Max(1e-5f, scale.x), v * Mathf.Max(1e-5f, scale.y));
            var rad = rotationDeg * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);
            var ruv = new Vector2(uv.x * cos - uv.y * sin, uv.x * sin + uv.y * cos);

            // 用 seed 产生确定性的偏移，保证随机但可复现
            var sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            var sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            var seedOffset = new Vector2(sx - Mathf.Floor(sx), sy - Mathf.Floor(sy));
            ruv += seedOffset;

            // fBm 采样（Perlin 多层叠加）
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
            var noise01 = norm > 1e-5f ? sum / norm : 0f; // 0..1

            // 2) 计算两条路肩的形状权重，仅在路肩区域内返回>0
            var halfRoad = worldWidth * 0.5f;
            var inset = Mathf.Clamp01(shoulderPositionRatio) * halfRoad;
            var leftCenter = -halfRoad + inset;
            var rightCenter = halfRoad - inset;
            var stripeHalf = Mathf.Max(1e-5f, shoulderWidthRatio * worldWidth * 0.5f);
            var falloffWorld = Mathf.Max(1e-5f, edgeFalloff * worldWidth);

            var distFromCenter = horizontalPosition * halfRoad; // -halfRoad..halfRoad（世界单位）
            float StripeWeight(float center)
            {
                var d = Mathf.Abs(distFromCenter - center);
                if (falloffWorld <= 1e-6f)
                {
                    return d <= stripeHalf ? 1f : 0f;
                }
                var t = 1f - Mathf.Clamp01((d - stripeHalf) / falloffWorld);
                return Mathf.Clamp01(t);
            }

            var wLeft = enableLeftShoulder ? StripeWeight(leftCenter) : 0f;
            var wRight = enableRightShoulder ? StripeWeight(rightCenter) : 0f;
            var shoulderShape = Mathf.Max(wLeft, wRight) * shoulderStrength;

            // 3) 噪声仅在路肩内生效：形状 × 噪声 × procedural 强度
            var raw = shoulderShape * Mathf.Clamp01(noise01 * Mathf.Max(0f, strength));

            // 4) 应用平滑（支持非对称阈值），并做整体缩放
            float ApplyCombinedSmoothing(float value)
            {
                // 统一先整体缩放
                value *= overallScale;

                if (useAsymmetricEdges)
                {
                    var e0 = Mathf.Clamp01(edgeLow);
                    var e1 = Mathf.Clamp01(edgeHigh);
                    if (e1 < e0)
                    {
                        var t2 = e0;
                        e0 = e1;
                        e1 = t2;
                    }
                    if (value <= e0) return 0f;
                    if (value >= e1) return 1f;
                    var tt = (value - e0) / Mathf.Max(1e-6f, e1 - e0);
                    var sm = tt * tt * (3f - 2f * tt);
                    return Mathf.Clamp01(sm);
                }
                return ApplySmoothing(value);
            }

            return Mathf.Clamp01(ApplyCombinedSmoothing(raw));
        }

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength) =>
            Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);

        // --- GPU 参数打包 ---
        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            dst.MaskType = 4; // MASK_TYPE_SHOULDER_NOISE（Compute 中定义）
            dst.Strength = 1.0f; // 顶层强度保留给统一乘法

            // Shoulder params
            dst.ShoulderParams.ShoulderWidthRatio = shoulderWidthRatio;
            dst.ShoulderParams.ShoulderStrength = shoulderStrength;
            dst.ShoulderParams.EdgeFalloff = edgeFalloff;
            dst.ShoulderParams.EnableLeftShoulder = enableLeftShoulder;
            dst.ShoulderParams.EnableRightShoulder = enableRightShoulder;
            dst.ShoulderParams.Tiling = tiling;
            dst.ShoulderParams.Offset = offset;
            dst.ShoulderParams.OverallScale = overallScale;
            dst.ShoulderParams.Smooth = smooth;
            // 新增：路肩位置比例
            dst.ShoulderParams.PositionRatio = shoulderPositionRatio;

            // Noise params（与 Perlin/NoiseMask 一致）
            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            dst.NoiseParams.Strength = Mathf.Max(0f, strength);
            dst.NoiseParams.Seed = seed;
            dst.NoiseParams.Tiling = tiling;
            dst.NoiseParams.Offset = offset;
            dst.NoiseParams.OverallScale = 1.0f; // 结合型遮罩避免重复整体缩放，留给 Shoulder overallScale
            dst.NoiseParams.Smooth = 0.0f;       // 平滑由组合端处理
            dst.NoiseParams.NoiseScale = scale;
            dst.NoiseParams.RotationRad = rotationDeg * Mathf.Deg2Rad;
            dst.NoiseParams.Octaves = octaves;
            dst.NoiseParams.Lacunarity = lacunarity;
            dst.NoiseParams.Gain = gain;
            dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
            dst.NoiseParams.EdgeLow = edgeLow;
            dst.NoiseParams.EdgeHigh = edgeHigh;
        }
    }
}

