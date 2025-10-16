// 文件路径: MrPathV2/Masks/PerlinNoiseMask.cs

using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(fileName = "New Perlin Noise Mask", menuName = "MrPathV2/Masks/Perlin Noise Mask")]
    public class PerlinNoiseMask : ProceduralMaskBase
    {
        /// <summary>
        /// 【修改】更新方法签名
        /// </summary>
        public override float Evaluate(float horizontalPosition, float worldWidth)
        {
            // 1. 调用父类的辅助函数，并将 worldWidth 传递下去
            float inputX = TransformPosition(horizontalPosition, worldWidth);
            
            float inputY = seed + offset.y;
            float noiseValue = Mathf.PerlinNoise(inputX, inputY);
            
            return noiseValue * strength;
        }
    }
}