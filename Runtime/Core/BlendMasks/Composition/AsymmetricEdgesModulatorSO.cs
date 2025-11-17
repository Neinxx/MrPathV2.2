using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Modulators/Asymmetric Edges")]
    public class AsymmetricEdgesModulatorSO : ModulatorSO
    {
        public bool useAsymmetricEdges = true;
        [Range(0f,1f)] public float edgeLow = 0.25f;
        [Range(0f,1f)] public float edgeHigh = 0.75f;
        public override float ApplyCpu(float value)
        {
            if (!useAsymmetricEdges) return value;
            var e0 = Mathf.Clamp01(edgeLow);
            var e1 = Mathf.Clamp01(edgeHigh);
            if (e1 < e0){ var t=e0; e0=e1; e1=t; }
            if (value <= e0) return 0f;
            if (value >= e1) return 1f;
            var tt = (value - e0) / Mathf.Max(1e-6f, e1 - e0);
            var sm = tt * tt * (3f - 2f * tt);
            return Mathf.Clamp01(sm);
        }
        public override void ApplyGpuNoise(ref GpuMaskParamsData dst)
        {
            dst.NoiseParams.UseAsymmetricEdges = useAsymmetricEdges;
            dst.NoiseParams.EdgeLow = edgeLow;
            dst.NoiseParams.EdgeHigh = edgeHigh;
        }
    }
}
