// 文件路径: MrPathV2/Masks/PerlinNoiseMask.cs

using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(fileName = "New Perlin Noise Mask", menuName = "MrPathV2/Masks/Perlin Noise Mask")]
    public class PerlinNoiseMask : ProceduralMaskBase
    {
        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            // 1. 调用父类的辅助函数，并将 worldWidth 和 pathLength 传递下去
            float inputX = TransformPosition(horizontalPosition, worldWidth);
            
            // 使用TransformPathPosition计算Y轴坐标
            // 在遮罩评估中，我们假设沿路径的进度为0.5（中点），实际应用中可能需要传递真实的路径进度
            float pathProgress = 0.5f; // 临时使用中点，实际应该从上下文获取
            float inputY = TransformPathPosition(pathProgress, pathLength);
            
            float noiseValue = Mathf.PerlinNoise(inputX, inputY);
            
            float rawValue = noiseValue * strength;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }
    }
}