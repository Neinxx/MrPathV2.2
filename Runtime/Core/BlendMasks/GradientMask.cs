using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Gradient Mask")]
    public class GradientMask : BlendMaskBase
    {
        public AnimationCurve gradient = AnimationCurve.Linear(-1, 1, 1, 1);
        
        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            float rawValue = gradient != null ? gradient.Evaluate(horizontalPosition) : 1f;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }
        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            float rawValue = gradient != null ? gradient.Evaluate(horizontalPosition) : 1f;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }
    }
}