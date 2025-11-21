using MrPathV2.Runtime.Core.Noise;
using MrPathV2.Runtime.Core.NoiseRuntime;
using UnityEngine;
// for GpuMaskParamsData

namespace MrPathV2.Runtime.Core.BlendMasks
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
        public bool useAsymmetricEdges;

        [Range(0f, 1f)] [Tooltip("下阈值（Edge1）。建议 < EdgeHigh。")]
        public float edgeLow = 0.25f;

        [Range(0f, 1f)] [Tooltip("上阈值（Edge2）。建议 > EdgeLow。")]
        public float edgeHigh = 0.75f;

        private float _scaleX, _scaleY, _cos, _sin, _seedX, _seedY;
        private int _oct;
        private float _lac, _gain;
        private bool _ready;

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
            _ready = false;
        }

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            if (strength <= 0f) return 0f;
            if (!_ready) EnsurePrecomputed();

            var u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            var v = TransformPathPosition(pathProgress, pathLength);

            var p = new NoiseParamsDto
            {
                ScaleX = uniformScale ? _scaleX : noiseScale.x,
                ScaleY = uniformScale ? _scaleX : _scaleY,
                Cos = _cos,
                Sin = _sin,
                Octaves = _oct,
                Lacunarity = _lac,
                Gain = _gain,
                SeedX = _seedX,
                SeedY = _seedY
            };
            var noise01 = NoiseEvalUtils.EvaluateFbm01(p, u, v);
            var raw = Mathf.Clamp01(noise01 * Mathf.Max(0f, strength));
            return ApplyNoiseSmoothing(raw);
        }

        private void EnsurePrecomputed()
        {
            _scaleX = noiseScale.x;
            _scaleY = noiseScale.y;
            var rad = rotationDeg * Mathf.Deg2Rad;
            _cos = Mathf.Cos(rad);
            _sin = Mathf.Sin(rad);
            _oct = Mathf.Max(1, octaves);
            _lac = Mathf.Max(1f, lacunarity);
            _gain = Mathf.Clamp01(gain);
            var sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            var sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            _seedX = sx - Mathf.Floor(sx);
            _seedY = sy - Mathf.Floor(sy);
            _ready = true;
        }

        private float ApplyNoiseSmoothing(float maskValue)
        {
            maskValue *= overallScale;
            if (useAsymmetricEdges) return ApplyAsymmetricSmoothing(maskValue, edgeLow, edgeHigh);
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
