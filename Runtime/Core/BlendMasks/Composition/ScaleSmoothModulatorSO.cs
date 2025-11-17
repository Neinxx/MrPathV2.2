using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Modulators/Scale & Smooth")]
    public class ScaleSmoothModulatorSO : ModulatorSO
    {
        [Range(0f, 2f)] public float overallScale = 1f;
        [Range(0f, 1f)] public float smooth = 0.1f;
        public override float ApplyCpu(float value)
        {
            value *= overallScale;
            if (smooth <= 1e-5f) return Mathf.Clamp01(value);
            var edge0 = smooth * 0.5f;
            var edge1 = 1f - smooth * 0.5f;
            if (value <= edge0) return 0f;
            if (value >= edge1) return 1f;
            var t = (value - edge0) / Mathf.Max(1e-6f, edge1 - edge0);
            var sm = t * t * (3f - 2f * t);
            return Mathf.Clamp01(sm);
        }
        public override void ApplyGpuNoise(ref GpuMaskParamsData dst)
        {
            dst.NoiseParams.OverallScale = overallScale;
            dst.NoiseParams.Smooth = smooth;
        }
        public override void ApplyGpuShoulder(ref GpuMaskParamsData dst)
        {
            dst.ShoulderParams.OverallScale = overallScale;
            dst.ShoulderParams.Smooth = smooth;
        }
    }
}
