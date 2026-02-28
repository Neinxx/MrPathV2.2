using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise Mask")]
    public class NoiseMask : ProceduralMaskBase
    {
        public float scale = 10f;


        public override float Evaluate(float horizontalPosition,float worldWidth)
        {
            
            float inputX = TransformPosition(horizontalPosition,worldWidth);

           
            float inputY = seed + offset.y;
            // 修复：直接使用 inputX 和 scale，避免双重缩放
            float noise = (Mathf.PerlinNoise(inputX * Mathf.Max(0.0001f, scale), inputY * 0.5f) - 0.5f) * 2f;
            float rawValue = Mathf.Clamp01(noise * strength);
            return ApplySmoothing(rawValue);

        }
    }
}