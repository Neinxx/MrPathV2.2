using UnityEngine;
using MrPathV2.Runtime.Core.NoiseRuntime;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise/Fbm Variant")]
    public class FbmNoiseVariantSO : NoiseVariantSO
    {
        [Range(1,8)] public int octaves = 3;
        [Range(1f,4f)] public float lacunarity = 2f;
        [Range(0f,1f)] public float gain = 0.5f;
        public override int VariantId => 0;
        public override NoiseParamsDto BuildBaseDto(float rotRad, Vector2 scale, int oct, float lac, float g, float seedX, float seedY)
        {
            return new NoiseParamsDto { ScaleX = scale.x, ScaleY = scale.y, Cos = Mathf.Cos(rotRad), Sin = Mathf.Sin(rotRad), Octaves = oct, Lacunarity = lac, Gain = g, SeedX = seedX, SeedY = seedY };
        }
        public override object BuildVariantDto(Vector2 scale, float rotRad) { return null; }
    }
}
