using UnityEngine;
using MrPathV2.Runtime.Core.NoiseRuntime;
using MrPathV2.Runtime.Core.NoiseRuntime.Variants;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    public abstract class NoiseVariantSO : ScriptableObject
    {
        public abstract int VariantId { get; }
        public abstract NoiseParamsDto BuildBaseDto(float rotRad, Vector2 scale, int oct, float lac, float gain, float seedX, float seedY);
        public abstract object BuildVariantDto(Vector2 scale, float rotRad);
        public virtual void WriteGpu(object variantDto, ref GpuMaskParamsData dst, NoiseParamsDto baseDto)
        {
            if (NoiseVariantRegistry.TryGetGpu(VariantId, out var w)) w.Write(baseDto, variantDto, ref dst);
        }
        public virtual float Evaluate(float u, float v, NoiseParamsDto baseDto, object variantDto)
        {
            if (NoiseVariantRegistry.TryGetCpu(VariantId, out var e)) return e.Evaluate(baseDto, u, v, variantDto);
            return 0.5f;
        }
    }
}
