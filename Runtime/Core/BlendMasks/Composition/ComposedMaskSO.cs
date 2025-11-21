using UnityEngine;
using MrPathV2.Runtime.Core.NoiseRuntime;
using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Composed Mask")]
    public class ComposedMaskSO : ProceduralMaskBase
    {
        public RegionSelectorSO region;
        public NoiseVariantSO noise;
        public ModulatorSO[] modulators;
        [System.Serializable]
        public struct SpecialNoiseEntry
        {
            public NoiseVariantSO variant;
            public Vector2 noiseScale;
            public bool uniformScale;
            [Range(-180f, 180f)] public float rotationDeg;
            [Range(0f, 1f)] public float strength;
            public CombineMode combineMode;
        }
        public enum CombineMode { Add, Multiply, Max, Min }
        public SpecialNoiseEntry[] specials;
        public Vector2 noiseScale = new Vector2(1f, 1f);
        public bool uniformScale = true;
        [Range(-180f, 180f)] public float rotationDeg;
        public bool useAsymmetricEdges;
        [Range(0f, 1f)] public float edgeLow = 0.25f;
        [Range(0f, 1f)] public float edgeHigh = 0.75f;

        private float _sx, _sy, _cos, _sin, _seedX, _seedY;

        private void OnValidate()
        {
            if (uniformScale) noiseScale.y = noiseScale.x;
            edgeLow = Mathf.Clamp01(edgeLow);
            edgeHigh = Mathf.Clamp01(edgeHigh);
            if (edgeHigh < edgeLow) { var t = edgeLow; edgeLow = edgeHigh; edgeHigh = t; }
            _sx = noiseScale.x; _sy = noiseScale.y;
            var rad = rotationDeg * Mathf.Deg2Rad; _cos = Mathf.Cos(rad); _sin = Mathf.Sin(rad);
            var sx = Mathf.Abs(Mathf.Sin(seed * 12.9898f) * 43758.5453f);
            var sy = Mathf.Abs(Mathf.Sin(seed * 78.233f) * 12345.678f);
            _seedX = sx - Mathf.Floor(sx); _seedY = sy - Mathf.Floor(sy);
            MaskChangeEvents.RaiseChanged(this);
        }

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            var rw = region ? region.Weight(horizontalPosition, worldWidth) : 1f;
            if (rw <= 0f || strength <= 0f) return 0f;
            var u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            var v = TransformPathPosition(pathProgress, pathLength);
            var scale = uniformScale ? new Vector2(_sx, _sx) : new Vector2(_sx, _sy);
            var baseDto = noise ? noise.BuildBaseDto(rotationDeg * Mathf.Deg2Rad, scale, 1, 1f, 1f, _seedX, _seedY) : new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = _cos, Sin = _sin, Octaves = 1, Lacunarity = 1f, Gain = 1f, SeedX = _seedX, SeedY = _seedY };
            var varDto = noise ? noise.BuildVariantDto(scale, rotationDeg * Mathf.Deg2Rad) : null;
            var nBase = noise ? noise.Evaluate(u, v, baseDto, varDto) : 1f;
            var nComb = Mathf.Clamp01(nBase);
            if (specials != null)
            {
                for (var i = 0; i < specials.Length; i++)
                {
                    var sp = specials[i];
                    if (sp.variant == null) continue;
                    var sScale = sp.uniformScale ? new Vector2(sp.noiseScale.x, sp.noiseScale.x) : sp.noiseScale;
                    var sDto = sp.variant.BuildBaseDto(sp.rotationDeg * Mathf.Deg2Rad, sScale, 1, 1f, 1f, _seedX, _seedY);
                    var sVarDto = sp.variant.BuildVariantDto(sScale, sp.rotationDeg * Mathf.Deg2Rad);
                    var nS = Mathf.Clamp01(sp.variant.Evaluate(u, v, sDto, sVarDto));
                    var nsW = Mathf.Clamp01(nS * Mathf.Max(0f, sp.strength));
                    switch (sp.combineMode)
                    {
                        case CombineMode.Add: nComb = Mathf.Clamp01(nComb + nsW); break;
                        case CombineMode.Multiply: nComb = Mathf.Clamp01(nComb * nsW); break;
                        case CombineMode.Max: nComb = Mathf.Max(nComb, nsW); break;
                        case CombineMode.Min: nComb = Mathf.Min(nComb, nsW); break;
                    }
                }
            }
            var val = rw * Mathf.Clamp01(nComb * Mathf.Max(0f, strength));
            if (modulators != null)
            {
                for (var i = 0; i < modulators.Length; i++) if (modulators[i]) val = modulators[i].ApplyCpu(val);
            }
            else
            {
                val *= overallScale;
                if (useAsymmetricEdges)
                {
                    var e0 = edgeLow; var e1 = edgeHigh; if (e1 < e0) { var t = e0; e0 = e1; e1 = t; }
                    if (val <= e0) return 0f; if (val >= e1) return 1f;
                    var tt = (val - e0) / Mathf.Max(1e-6f, e1 - e0);
                    var sm = tt * tt * (3f - 2f * tt);
                    return Mathf.Clamp01(sm);
                }
                return ApplySmoothing(val);
            }
            return Mathf.Clamp01(val);
        }

        public override void FillGpuParams(ref GpuMaskParamsData dst)
        {
            var scale = uniformScale ? new Vector2(noiseScale.x, noiseScale.x) : noiseScale;
            dst.Strength = 1f;
            if (!region)
            {
                dst.MaskType = 2;
                dst.NoiseParams.Strength = Mathf.Max(0f, strength);
                dst.NoiseParams.Seed = seed;
                dst.NoiseParams.Tiling = tiling;
                dst.NoiseParams.Offset = offset;
                dst.NoiseParams.OverallScale = overallScale;
                dst.NoiseParams.Smooth = smooth;
                dst.NoiseParams.NoiseScale = scale;
                dst.NoiseParams.RotationRad = rotationDeg * Mathf.Deg2Rad;
                dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
                dst.NoiseParams.EdgeLow = edgeLow;
                dst.NoiseParams.EdgeHigh = edgeHigh;
                if (modulators != null) for (var i = 0; i < modulators.Length; i++) modulators[i]?.ApplyGpuNoise(ref dst);
                var baseDto = noise ? noise.BuildBaseDto(rotationDeg * Mathf.Deg2Rad, scale, 1, 1f, 1f, 0f, 0f) : new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotationDeg * Mathf.Deg2Rad), Sin = Mathf.Sin(rotationDeg * Mathf.Deg2Rad), Octaves = 1, Lacunarity = 1f, Gain = 1f, SeedX = 0f, SeedY = 0f };
                var varDto = noise ? noise.BuildVariantDto(scale, rotationDeg * Mathf.Deg2Rad) : null;
                if (noise) noise.WriteGpu(varDto, ref dst, baseDto);
                return;
            }
            dst.MaskType = 4;
            dst.ShoulderParams.Tiling = tiling;
            dst.ShoulderParams.Offset = offset;
            dst.ShoulderParams.OverallScale = overallScale;
            dst.ShoulderParams.Smooth = smooth;
            region.FillGpu(ref dst);
            dst.NoiseParams.Strength = Mathf.Max(0f, strength);
            dst.NoiseParams.Seed = seed;
            dst.NoiseParams.Tiling = tiling;
            dst.NoiseParams.Offset = offset;
            dst.NoiseParams.OverallScale = 1f;
            dst.NoiseParams.Smooth = 0f;
            dst.NoiseParams.NoiseScale = scale;
            dst.NoiseParams.RotationRad = rotationDeg * Mathf.Deg2Rad;
            dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
            dst.NoiseParams.EdgeLow = edgeLow;
            dst.NoiseParams.EdgeHigh = edgeHigh;
            if (modulators != null)
            {
                for (var i = 0; i < modulators.Length; i++)
                {
                    modulators[i]?.ApplyGpuShoulder(ref dst);
                    modulators[i]?.ApplyGpuNoise(ref dst);
                }
            }
            var baseDto2 = noise ? noise.BuildBaseDto(rotationDeg * Mathf.Deg2Rad, scale, 1, 1f, 1f, 0f, 0f) : new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotationDeg * Mathf.Deg2Rad), Sin = Mathf.Sin(rotationDeg * Mathf.Deg2Rad), Octaves = 1, Lacunarity = 1f, Gain = 1f, SeedX = 0f, SeedY = 0f };
            var varDto2 = noise ? noise.BuildVariantDto(scale, rotationDeg * Mathf.Deg2Rad) : null;
            if (noise) noise.WriteGpu(varDto2, ref dst, baseDto2);
        }
        public override bool SupportsGpu => specials == null || specials.Length == 0;
    }
}
