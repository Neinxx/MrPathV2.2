using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Runtime.Core.NoiseRuntime.Variants
{
    public interface INoiseGpuParamWriter
    {
        void Write(in NoiseParamsDto baseDto, object variantDto, ref GpuMaskParamsData dst);
    }
}
