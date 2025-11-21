using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Gradient Mask")]
    public class GradientMask : BlendMaskBase
    {
        public override bool SupportsGpu => false;
        public AnimationCurve gradient = AnimationCurve.Linear(-1, 1, 1, 1);

        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength) => Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);
        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            // 使用基类的 TransformPosition 接入 tiling（频率）与 offset
            var u = TransformPosition(horizontalPosition, worldWidth, pathLength);
            var u01 = Mathf.Repeat(u, 1f); // 0..1
            var x = u01 * 2f - 1f; // 映射回 -1..1 以适配曲线域

            var rawValue = gradient != null ? gradient.Evaluate(x) : 1f;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }
    }
}
