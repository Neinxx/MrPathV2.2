using MrPathV2.Runtime.Core.NoiseRuntime;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Worley Noise Mask")]
    public class WorleyNoiseMask : ProceduralMaskBase
    {
        public override bool SupportsGpu => true;

        public Vector2 noiseScale = new Vector2(1f, 1f);
        public bool uniformScale = true;
        [Range(-180f, 180f)] public float rotationDeg;
        [Range(4, 128)] public int cellPeriod = 32;
        [Range(0f, 1f)] public float jitter = 0.5f;
        public bool invert;

        public bool useAsymmetricEdges;
        [Range(0f, 1f)] public float edgeLow = 0.25f;
        [Range(0f, 1f)] public float edgeHigh = 0.75f;

        private float _scaleX, _scaleY, _cos, _sin, _seedX, _seedY;
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
            cellPeriod = Mathf.Clamp(cellPeriod, 4, 128);
            jitter = Mathf.Clamp01(jitter);
            EnsurePrecomputed();
            MaskChangeEvents.RaiseChanged(this);
        }

        private void EnsurePrecomputed()
        {
            _scaleX = noiseScale.x;
            _scaleY = noiseScale.y;
            var rad = rotationDeg * Mathf.Deg2Rad;
            _cos = Mathf.Cos(rad);
            _sin = Mathf.Sin(rad);
            var sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            var sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            _seedX = sx - Mathf.Floor(sx);
            _seedY = sy - Mathf.Floor(sy);
            _ready = true;
        }

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            if (strength <= 0f) return 0f;
            if (!_ready) EnsurePrecomputed();
            var u = TransformPosition(horizontalPosition, worldWidth, pathLength) + _seedX;
            var v = TransformPathPosition(pathProgress, pathLength) + _seedY;
            var p = new WorleyParamsDto
            {
                ScaleX = uniformScale ? _scaleX : noiseScale.x,
                ScaleY = uniformScale ? _scaleX : _scaleY,
                Cos = _cos,
                Sin = _sin,
                SeedX = 0f,
                SeedY = 0f,
                CellPeriod = cellPeriod,
                Jitter = jitter,
                Invert = invert
            };
            var n = NoiseEvalUtils.EvaluateWorley01(p, u, v);
            var raw = Mathf.Clamp01(n * Mathf.Max(0f, strength));
            var value = raw * overallScale;
            if (useAsymmetricEdges) return ApplyAsymmetricSmoothing(value, edgeLow, edgeHigh);
            return ApplySmoothing(value);
        }

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength) =>
            Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);

        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            dst.MaskType = 2;
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
            dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
            dst.NoiseParams.EdgeLow = edgeLow;
            dst.NoiseParams.EdgeHigh = edgeHigh;
            var baseDto = new NoiseParamsDto
            {
                ScaleX = scale.x,
                ScaleY = scale.y,
                Cos = Mathf.Cos(rotationDeg * Mathf.Deg2Rad),
                Sin = Mathf.Sin(rotationDeg * Mathf.Deg2Rad),
                Octaves = 1,
                Lacunarity = 1f,
                Gain = 1f,
                SeedX = 0f,
                SeedY = 0f
            };
            var varDto = new MrPathV2.Runtime.Core.NoiseRuntime.WorleyParamsDto { CellPeriod = cellPeriod, Jitter = jitter, Invert = invert, ScaleX = scale.x, ScaleY = scale.y, Cos = baseDto.Cos, Sin = baseDto.Sin, SeedX = 0f, SeedY = 0f };
            MrPathV2.Runtime.Core.NoiseRuntime.Variants.NoiseVariantRegistry.TryGetGpu(2, out var writer);
            writer?.Write(baseDto, varDto, ref dst);
        }
    }
}
