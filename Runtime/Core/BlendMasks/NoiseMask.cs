using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise Mask")]
    public class NoiseMask : BlendMaskBase
    {
        public float scale = 10f;
        [Range(0,1)] public float strength = 1f;

        public override float Evaluate(float horizontalPosition)
        {
            float noise = (Mathf.PerlinNoise(horizontalPosition * Mathf.Max(0.0001f, scale), 0.5f) - 0.5f) * 2f;
            return Mathf.Clamp01(noise * strength);
        }
    }
}