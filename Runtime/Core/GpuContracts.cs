using System.Runtime.InteropServices;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuShoulderMaskParamsData
    {
        public float ShoulderWidthRatio;
        public float PositionRatio;
        public float ShoulderStrength;
        public float EdgeFalloff;
        public int EnableLeftShoulder;
        public int EnableRightShoulder;
        public Vector2 Tiling;
        public Vector2 Offset;
        public float OverallScale;
        public float Smooth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuNoiseMaskParamsData
    {
        public float Strength;
        public float Seed;
        public Vector2 Tiling;
        public Vector2 Offset;
        public float OverallScale;
        public float Smooth;
        public Vector2 NoiseScale;
        public float RotationRad;
        public int Octaves;
        public float Lacunarity;
        public float Gain;
        public bool UseAsymmetricEdges;
        public float EdgeLow;
        public float EdgeHigh;
        public float Period;
        public float Jitter;
        public int Invert;
        public int Variant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuMaskParamsData
    {
        public int MaskType;
        public float Strength;
        public GpuNoiseMaskParamsData NoiseParams;
        public GpuShoulderMaskParamsData ShoulderParams;
    }
}
