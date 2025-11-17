using UnityEngine;
using MrPathV2.Runtime.Core.NoiseRuntime;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise/Worley Variant")]
    public class WorleyNoiseVariantSO : NoiseVariantSO
    {
        [Range(4,128)] public int cellPeriod = 32;
        [Range(0f,1f)] public float jitter = 0.5f;
        public bool invert;
        public override int VariantId => 2;
        public override NoiseParamsDto BuildBaseDto(float rotRad, Vector2 scale, int oct, float lac, float gain, float seedX, float seedY)
        {
            return new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotRad), Sin = Mathf.Sin(rotRad), Octaves = 1, Lacunarity = 1f, Gain = 1f, SeedX = seedX, SeedY = seedY };
        }
        public override object BuildVariantDto(Vector2 scale, float rotRad)
        {
            return new WorleyParamsDto { CellPeriod = cellPeriod, Jitter = jitter, Invert = invert, ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotRad), Sin = Mathf.Sin(rotRad), SeedX = 0f, SeedY = 0f };
        }
    }
}
