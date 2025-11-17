using System.Collections.Generic;
using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Runtime.Core.NoiseRuntime.Variants
{
    public static class NoiseVariantRegistry
    {
        private static readonly Dictionary<int, INoiseCpuEvaluator> Cpu = new Dictionary<int, INoiseCpuEvaluator>();
        private static readonly Dictionary<int, INoiseGpuParamWriter> Gpu = new Dictionary<int, INoiseGpuParamWriter>();

        static NoiseVariantRegistry()
        {
            Register(0, new FbmEvaluator(), new FbmGpuWriter());
            Register(1, new StripeEvaluator(), new StripeGpuWriter());
            Register(2, new WorleyEvaluator(), new WorleyGpuWriter());
        }

        public static void Register(int id, INoiseCpuEvaluator cpu, INoiseGpuParamWriter gpu)
        {
            Cpu[id] = cpu;
            Gpu[id] = gpu;
        }

        public static bool TryGetCpu(int id, out INoiseCpuEvaluator eval) => Cpu.TryGetValue(id, out eval);
        public static bool TryGetGpu(int id, out INoiseGpuParamWriter writer) => Gpu.TryGetValue(id, out writer);
    }

    class FbmEvaluator : INoiseCpuEvaluator
    {
        public float Evaluate(in NoiseParamsDto baseDto, float u, float v, object variantDto)
        {
            return NoiseEvalUtils.EvaluateFbm01(baseDto, u, v);
        }
    }

    class FbmGpuWriter : INoiseGpuParamWriter
    {
        public void Write(in NoiseParamsDto baseDto, object variantDto, ref GpuMaskParamsData dst)
        {
            dst.NoiseParams.Period = 0f;
            dst.NoiseParams.Jitter = 0f;
            dst.NoiseParams.Invert = 0;
            dst.NoiseParams.Variant = 0;
        }
    }

    class StripeEvaluator : INoiseCpuEvaluator
    {
        public float Evaluate(in NoiseParamsDto baseDto, float u, float v, object variantDto)
        {
            var sp = (StripeParamsDto)variantDto;
            return NoiseEvalUtils.EvaluateStripe01(u, v, baseDto.ScaleX, baseDto.ScaleY, baseDto.Cos, baseDto.Sin, sp.Period, sp.Jitter);
        }
    }

    class StripeGpuWriter : INoiseGpuParamWriter
    {
        public void Write(in NoiseParamsDto baseDto, object variantDto, ref GpuMaskParamsData dst)
        {
            var sp = (StripeParamsDto)variantDto;
            dst.NoiseParams.Period = sp.Period;
            dst.NoiseParams.Jitter = sp.Jitter;
            dst.NoiseParams.Invert = 0;
            dst.NoiseParams.Variant = 1;
            dst.NoiseParams.Octaves = 1;
            dst.NoiseParams.Lacunarity = 1f;
            dst.NoiseParams.Gain = 1f;
        }
    }

    class WorleyEvaluator : INoiseCpuEvaluator
    {
        public float Evaluate(in NoiseParamsDto baseDto, float u, float v, object variantDto)
        {
            var wp = (WorleyParamsDto)variantDto;
            return NoiseEvalUtils.EvaluateWorley01(wp, u, v);
        }
    }

    class WorleyGpuWriter : INoiseGpuParamWriter
    {
        public void Write(in NoiseParamsDto baseDto, object variantDto, ref GpuMaskParamsData dst)
        {
            var wp = (WorleyParamsDto)variantDto;
            dst.NoiseParams.Period = wp.CellPeriod;
            dst.NoiseParams.Jitter = wp.Jitter;
            dst.NoiseParams.Invert = wp.Invert ? 1 : 0;
            dst.NoiseParams.Variant = 2;
            dst.NoiseParams.Octaves = 1;
            dst.NoiseParams.Lacunarity = 1f;
            dst.NoiseParams.Gain = 1f;
        }
    }
}
