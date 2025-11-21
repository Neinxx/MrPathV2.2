using UnityEngine;
using MrPathV2.Runtime.Core.NoiseRuntime.Variants;
using MrPathV2.Runtime.Core.NoiseRuntime;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise/Stripe Variant")]
    public class StripeNoiseVariantSO : NoiseVariantSO
    {
        [Range(0.1f,64f)] public float period = 8f;
        [Range(0f,1f)] public float jitter = 0.3f;
        public override int VariantId => 1;
        public override NoiseParamsDto BuildBaseDto(float rotRad, Vector2 scale, int oct, float lac, float gain, float seedX, float seedY)
        {
            return new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotRad), Sin = Mathf.Sin(rotRad), Octaves = 1, Lacunarity = 1f, Gain = 1f, SeedX = seedX, SeedY = seedY };
        }
        public override object BuildVariantDto(Vector2 scale, float rotRad)
        {
            return new StripeParamsDto { Period = period, Jitter = jitter };
        }
    }
}
