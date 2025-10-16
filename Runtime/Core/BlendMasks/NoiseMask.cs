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
            float noise = (Mathf.PerlinNoise(horizontalPosition * Mathf.Max(0.0001f, scale) * inputX, inputY * 0.5f) - 0.5f) * 2f;
            return Mathf.Clamp01(noise * strength);

        }
    }
}