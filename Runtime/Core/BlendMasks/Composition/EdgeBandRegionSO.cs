using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Regions/Edge Band Region")]
    public class EdgeBandRegionSO : RegionSelectorSO
    {
        [Range(0f,0.5f)] public float bandWidthRatio = 0.08f;
        [Range(0f,0.3f)] public float edgeFalloff = 0.05f;
        public bool enableLeft = true;
        public bool enableRight = true;

        public override float Weight(float horizontalPosition, float worldWidth)
        {
            var half = worldWidth * 0.5f;
            var leftC = -half;
            var rightC = half;
            var stripeHalf = Mathf.Max(1e-5f, bandWidthRatio * worldWidth * 0.5f);
            var falloffW = Mathf.Max(1e-5f, edgeFalloff * worldWidth);
            var dist = horizontalPosition * half;
            float Stripe(float center)
            {
                var d = Mathf.Abs(dist - center);
                if (falloffW <= 1e-6f) return d <= stripeHalf ? 1f : 0f;
                var t = 1f - Mathf.Clamp01((d - stripeHalf) / falloffW);
                return Mathf.Clamp01(t);
            }
            var wl = enableLeft ? Stripe(leftC) : 0f;
            var wr = enableRight ? Stripe(rightC) : 0f;
            return Mathf.Max(wl, wr);
        }

        public override void FillGpu(ref GpuMaskParamsData dst)
        {
            dst.ShoulderParams.ShoulderWidthRatio = bandWidthRatio;
            dst.ShoulderParams.PositionRatio = 0f;
            dst.ShoulderParams.ShoulderStrength = 1f;
            dst.ShoulderParams.EdgeFalloff = edgeFalloff;
            dst.ShoulderParams.EnableLeftShoulder = enableLeft ? 1 : 0;
            dst.ShoulderParams.EnableRightShoulder = enableRight ? 1 : 0;
        }
    }
}
