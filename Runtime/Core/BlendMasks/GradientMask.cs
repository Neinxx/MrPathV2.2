using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Gradient Mask")]
    public class GradientMask : BlendMaskBase
    {
        public AnimationCurve gradient = AnimationCurve.Linear(-1, 1, 1, 1);
        public override float Evaluate(float horizontalPosition)
        {
            return gradient != null ? gradient.Evaluate(horizontalPosition) : 1f;
        }
    }
}