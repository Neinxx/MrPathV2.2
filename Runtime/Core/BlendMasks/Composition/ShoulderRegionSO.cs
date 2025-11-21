using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Regions/Shoulder Region")]
    public class ShoulderRegionSO : RegionSelectorSO
    {
        [Range(0f,0.5f)] public float shoulderWidthRatio = 0.12f;
        [Range(0f,1f)] public float positionRatio = 0.1f;
        [Range(0f,1f)] public float shoulderStrength = 1f;
        [Range(0f,0.3f)] public float edgeFalloff = 0.05f;
        public bool enableLeft = true;
        public bool enableRight = true;

        public override float Weight(float horizontalPosition, float worldWidth)
        {
            var half = worldWidth * 0.5f;
            var inset = Mathf.Clamp01(positionRatio) * half;
            var leftC = -half + inset;
            var rightC = half - inset;
            var stripeHalf = Mathf.Max(1e-5f, shoulderWidthRatio * worldWidth * 0.5f);
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
            return Mathf.Max(wl, wr) * shoulderStrength;
        }

        public override void FillGpu(ref GpuMaskParamsData dst)
        {
            dst.ShoulderParams.ShoulderWidthRatio = shoulderWidthRatio;
            dst.ShoulderParams.PositionRatio = positionRatio;
            dst.ShoulderParams.ShoulderStrength = shoulderStrength;
            dst.ShoulderParams.EdgeFalloff = edgeFalloff;
            dst.ShoulderParams.EnableLeftShoulder = enableLeft ? 1 : 0;
            dst.ShoulderParams.EnableRightShoulder = enableRight ? 1 : 0;
        }
    }
}
