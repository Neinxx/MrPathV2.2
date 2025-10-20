using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise Mask")]
    public class NoiseMask : ProceduralMaskBase
    {
        public float scale = 10f;


        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            
            float inputX = TransformPosition(horizontalPosition, worldWidth);

            // 使用TransformPathPosition计算Y轴坐标
            // 在遮罩评估中，我们假设沿路径的进度为0.5（中点），实际应用中可能需要传递真实的路径进度
            float pathProgress = 0.5f; // 临时使用中点，实际应该从上下文获取
            float inputY = TransformPathPosition(pathProgress, pathLength);
            
            // 修复：直接使用 inputX 和 scale，避免双重缩放
            float noise = (Mathf.PerlinNoise(inputX * Mathf.Max(0.0001f, scale), inputY * 0.5f) - 0.5f) * 2f;
            float rawValue = Mathf.Clamp01(noise * strength);
            return ApplySmoothing(rawValue);

        }
    }
}