using UnityEngine;

namespace MrPathV2.Runtime.Core.Gpu
{
    /// <summary>
    ///     运行时侧用于打包遮罩参数的通用数据结构。Editor/Runtime 可将其拷贝到各自的 GPU 结构体。
    ///     该数据结构不要求与 HLSL 完全内存对齐，仅作为桥接 DTO 使用。
    /// </summary>
    public struct GpuShoulderMaskParamsData
    {
        public float ShoulderWidthRatio;
        public float ShoulderStrength;
        public float EdgeFalloff;
        public bool EnableLeftShoulder;
        public bool EnableRightShoulder;
        public Vector2 Tiling;
        public Vector2 Offset;
        public float OverallScale;
        public float Smooth;
    }

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
        public int AlgorithmId; // 统一调度的算法ID
        // 非对称平滑支持
        public bool UseAsymmetricEdges;
        public float EdgeLow;
        public float EdgeHigh;
    }

    // 定义噪声算法的统一枚举，避免魔法数字
    public enum NoiseAlgorithmId
    {
        Perlin = 0,
        Simple = 1
    }

    public struct GpuMaskParamsData
    {
        public int MaskType; // 与 HLSL MASK_TYPE_* 对应
        public float Strength; // 顶层强度（通常等于具体遮罩的强度）

        public GpuShoulderMaskParamsData ShoulderParams;
        public GpuNoiseMaskParamsData NoiseParams;
        // 如需扩展：Gradient/RoadSurface 等可在此追加字段
    }
}
