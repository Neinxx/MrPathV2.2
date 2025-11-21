namespace MrPathV2.Runtime.Core.NoiseRuntime.Variants
{
    public interface INoiseCpuEvaluator
    {
        float Evaluate(in NoiseParamsDto baseDto, float u, float v, object variantDto);
    }
}
