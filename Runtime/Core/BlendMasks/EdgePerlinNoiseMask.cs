using MrPathV2.Runtime.Core.Noise;
using MrPathV2.Runtime.Core.NoiseRuntime;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Edge Perlin Noise Mask")]
    public class EdgePerlinNoiseMask : ProceduralMaskBase
    {
        public override bool SupportsGpu => true;

        [Range(0f, 0.5f)] public float edgeBandWidthRatio = 0.08f;
        [Range(0f, 0.3f)] public float edgeFalloff = 0.05f;
        public bool enableLeftEdge = true;
        public bool enableRightEdge = true;

        public Vector2 noiseScale = new Vector2(1f, 1f);
        public bool uniformScale = true;
        [Range(-180f, 180f)] public float rotationDeg;

        [Range(1, 8)] public int octaves = 3;
        [Range(1f, 4f)] public float lacunarity = 2f;
        [Range(0f, 1f)] public float gain = 0.5f;

        public bool useAsymmetricEdges;
        [Range(0f, 1f)] public float edgeLow = 0.25f;
        [Range(0f, 1f)] public float edgeHigh = 0.75f;

        private float _scaleX, _scaleY, _cos, _sin, _lac, _gain, _seedX, _seedY;
        private int _oct;

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
            edgeBandWidthRatio = Mathf.Clamp(edgeBandWidthRatio, 0f, 0.5f);
            edgeFalloff = Mathf.Clamp(edgeFalloff, 0f, 0.3f);
            EnsurePrecomputed();
            MaskChangeEvents.RaiseChanged(this);
        }

        private bool _ready;

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

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            if (strength <= 0f) return 0f;
            if (!enableLeftEdge && !enableRightEdge) return 0f;
            if (!_ready) EnsurePrecomputed();

            var halfRoad = worldWidth * 0.5f;
            var leftCenter = -halfRoad;
            var rightCenter = halfRoad;
            var stripeHalf = Mathf.Max(1e-5f, edgeBandWidthRatio * worldWidth * 0.5f);
            var falloffWorld = Mathf.Max(1e-5f, edgeFalloff * worldWidth);
            var distFromCenter = horizontalPosition * halfRoad;

            float StripeWeight(float center)
            {
                var d = Mathf.Abs(distFromCenter - center);
                if (falloffWorld <= 1e-6f) return d <= stripeHalf ? 1f : 0f;
                var t = 1f - Mathf.Clamp01((d - stripeHalf) / falloffWorld);
                return Mathf.Clamp01(t);
            }

            var wLeft = enableLeftEdge ? StripeWeight(leftCenter) : 0f;
            var wRight = enableRightEdge ? StripeWeight(rightCenter) : 0f;
            var edgeShape = Mathf.Max(wLeft, wRight);
            if (edgeShape <= 0f) return 0f;

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
            var raw = edgeShape * Mathf.Clamp01(noise01 * Mathf.Max(0f, strength));
            return Mathf.Clamp01(ApplyCombinedSmoothing(raw));
        }

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength) =>
            Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);

        private float ApplyCombinedSmoothing(float value)
        {
            value *= overallScale;
            if (useAsymmetricEdges) return ApplyAsymmetricSmoothing(value, edgeLow, edgeHigh);
            return ApplySmoothing(value);
        }

        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            dst.MaskType = 4;
            dst.Strength = 1.0f;
            dst.ShoulderParams.ShoulderWidthRatio = edgeBandWidthRatio;
            dst.ShoulderParams.ShoulderStrength = 1.0f;
            dst.ShoulderParams.EdgeFalloff = edgeFalloff;
            dst.ShoulderParams.EnableLeftShoulder = enableLeftEdge ? 1 : 0;
            dst.ShoulderParams.EnableRightShoulder = enableRightEdge ? 1 : 0;
            dst.ShoulderParams.Tiling = tiling;
            dst.ShoulderParams.Offset = offset;
            dst.ShoulderParams.OverallScale = overallScale;
            dst.ShoulderParams.Smooth = smooth;
            dst.ShoulderParams.PositionRatio = 0.0f;

            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            dst.NoiseParams.Strength = Mathf.Max(0f, strength);
            dst.NoiseParams.Seed = seed;
            dst.NoiseParams.Tiling = tiling;
            dst.NoiseParams.Offset = offset;
            dst.NoiseParams.OverallScale = 1.0f;
            dst.NoiseParams.Smooth = 0.0f;
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
