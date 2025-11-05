#ifndef BLEND_MASK_LIBRARY_INCLUDED
#define BLEND_MASK_LIBRARY_INCLUDED

// GPU数据结构定义，与新GPU管线V2对齐
struct GpuNoiseMaskParams {
	float  Strength;
	float  Seed;
	float2 Tiling; // 平铺重复次数（X=横向重复，Y=纵向重复），与 CPU tiling 对齐
	float2 Offset; // UV 偏移
	float  OverallScale; // 统一的整体缩放
	float  Smooth; // 平滑/软化系数
	float2 NoiseScale; // 二次细节缩放
	float  RotationRad; // 弧度
	int    Octaves; // fBm 层数
	float  Lacunarity; // 频率增长系数
	float  Gain; // 幅度衰减系数
	int    UseAsymmetricEdges; // 是否启用非对称平滑
	float  EdgeLow; // Edge1（低阈值）
	float  EdgeHigh; // Edge2（高阈值）
	float  Pad1; // 对齐
};

struct GpuShoulderMaskParams
{
    float Width;
    float Softness;
    float Strength;
    float OverallScale;
    float Smooth;
    float Pad;
};

struct GpuMaskParams {
	int                   Type; // MASK_TYPE_*
	float                 Strength; // 顶层遮罩强度
	float2                Padding; // 对齐填充，保持后续字段在 16B 边界上
	GpuNoiseMaskParams    Noise;
	GpuShoulderMaskParams Shoulder;
};

// 掩码类型常量（需与 C# / MaskAtlas.compute 保持一致）
static const int MASK_TYPE_NONE = 0;
static const int MASK_TYPE_SHOULDER = 1;
static const int MASK_TYPE_NOISE = 2;

// 由 Compute 注入的路径总长度（米）。
// 在 PaintSplatmapCompute.compute 中通过 SetFloat("_PathLength", ...) 传入。
extern float _PathLength;

// 根据 CPU 的 TransformPosition/TransformPathPosition 计算统一 UV
inline float2 ComputeMaskUV(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
{
	float halfRoad = max(1e-5, roadWidth * 0.5);
	float xNorm = clamp(signedDistance / halfRoad, -1.0, 1.0);
	float u01 = 0.5 * (xNorm + 1.0);

	float repeatX = p.Tiling.x;
	float repeatY = p.Tiling.y;
	// 与 CPU TransformPosition/TransformPathPosition 的容差一致（1e-4），并保留符号以支持镜像
	float denomX = (abs(repeatX) < 1e-4) ? (1e-4 * ((repeatX == 0.0) ? 1.0 : sign(repeatX))) : repeatX;
	float denomY = (abs(repeatY) < 1e-4) ? (1e-4 * ((repeatY == 0.0) ? 1.0 : sign(repeatY))) : repeatY;

	// 重复次数语义：U/V 直接按重复次数累加，不再乘以米制长度
	float u = u01 * denomX + p.Offset.x;
	float v = progress * denomY + p.Offset.y;
	return float2(u, v);
}

// 旋转工具（供 EvaluateNoise 使用）
inline float2 rotate2(float2 p, float a)
{
    float s = sin(a), c = cos(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// 统一噪声源：外部 Noise LUT（CPU/GPU 共享）
Texture2D<float> _NoiseLUT;   // R 通道 0..1 噪声
int              _NoiseLutSize; // LUT 尺寸（正方形）

// 计算着色器中无法依赖采样器状态，这里手写重复环绕的双线性采样
inline float SampleNoiseLUT(float2 uv)
{
    if (_NoiseLutSize <= 0) return 0.5;
    float2 st = frac(uv) * (float)_NoiseLutSize - 0.5;
    uint2 i0 = (uint2)floor(st);
    float2 f = frac(st);
    uint2 i1 = i0 + uint2(1, 0);
    uint2 i2 = i0 + uint2(0, 1);
    uint2 i3 = i0 + uint2(1, 1);
    int size = _NoiseLutSize;
    uint2 wrap = uint2(size, size);
    uint2 w0 = (i0 % wrap + wrap) % wrap;
    uint2 w1 = (i1 % wrap + wrap) % wrap;
    uint2 w2 = (i2 % wrap + wrap) % wrap;
    uint2 w3 = (i3 % wrap + wrap) % wrap;
    float c00 = _NoiseLUT.Load(uint3(w0, 0)).r;
    float c10 = _NoiseLUT.Load(uint3(w1, 0)).r;
    float c01 = _NoiseLUT.Load(uint3(w2, 0)).r;
    float c11 = _NoiseLUT.Load(uint3(w3, 0)).r;
    float cx0 = lerp(c00, c10, f.x);
    float cx1 = lerp(c01, c11, f.x);
    return lerp(cx0, cx1, f.y);
}

inline float EvaluateNoise(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
{
    float2 uv = ComputeMaskUV(progress, signedDistance, roadWidth, p);

    // 细节缩放与旋转（OverallScale 不再缩放 UV）
    float2 m = uv;
    m = m * float2(max(p.NoiseScale.x, 1e-6), max(p.NoiseScale.y, 1e-6));
    m = rotate2(m, p.RotationRad);
    m += float2(p.Seed * 17.0, p.Seed * 29.0);

    // 使用共享 Noise LUT 的 fBm 采样（统一 CPU/GPU）
    float amplitude = 1.0;
    float frequency = 1.0;
    float sum = 0.0;
    float norm = 0.0;
    int   oct = max(p.Octaves, 1);
    [loop] for(int i = 0; i < oct; ++i)
    {
        float s = SampleNoiseLUT(m * frequency);
        sum += s * amplitude;
        norm += amplitude;
        frequency *= max(1.0, p.Lacunarity);
        amplitude *= saturate(p.Gain);
    }
    float n01 = (norm > 1e-5) ? (sum / norm) : 0.0;

    // 与 CPU ApplySmoothing 一致：先整体缩放，再做平滑
    float pre = saturate(n01 * p.Strength) * p.OverallScale;
    if (p.Smooth <= 1e-5 && p.UseAsymmetricEdges == 0)
    {
        return saturate(pre);
    }
    // 非对称优先：当启用时直接使用 EdgeLow/EdgeHigh
    if (p.UseAsymmetricEdges != 0)
    {
        float e0 = p.EdgeLow;
        float e1 = p.EdgeHigh;
        if (e0 > e1)
        {
            float t = e0; e0 = e1; e1 = t;
        }
        return saturate(smoothstep(e0, e1, pre));
    }
    // 退回对称 Smooth 模式
    float edge0 = p.Smooth * 0.5;
    float edge1 = 1.0 - p.Smooth * 0.5;
    return saturate(smoothstep(edge0, edge1, pre));
}

// 肩部遮罩：根据与路径的距离（signedDistance）生成两侧肩部影响
// roadWidth: 道路总宽度（世界单位）；signedDistance: 像素到道路中心线的带符号距离
inline float EvaluateShoulder(float signedDistance, float roadWidth, GpuShoulderMaskParams p)
{
	float halfRoad = max(0.00001, roadWidth * 0.5);
	// 将传入的比例宽度转为实际宽度
	float shoulderWidth = max(0.00001, p.Width * roadWidth);
	// 在道路边缘之外的区域，按 shoulderWidth 制造一个带 Softness 的平台
	float d = abs(signedDistance);
	float outside = saturate((d - halfRoad) / max(0.00001, shoulderWidth));
	// 软化过渡（Softness 越大边缘越柔）
	float softened = smoothstep(0.0, max(0.00001, p.Softness), outside);

	// 与 CPU ApplySmoothing 一致：Strength/OverallScale 后按 Smooth 再次平滑
	float pre = saturate(softened * p.Strength) * p.OverallScale;
	if (p.Smooth <= 1e-5)
	{
		return saturate(pre);
	}
	float edge0 = p.Smooth * 0.5;
	float edge1 = 1.0 - p.Smooth * 0.5;
	return saturate(smoothstep(edge0, edge1, pre));
}

// 主评估函数（统一坐标）：使用 progress / signedDistance / roadWidth
inline float EvaluateMask(float progress, float signedDistance, float roadWidth, GpuMaskParams mask)
{
    // 无遮罩时返回 1.0，使图层仅受 falloff 与不透明度控制，避免被强制清零
    // 与当前 GPU 管线（未绑定 MaskAtlas）保持可用的默认行为
    if(mask.Type == MASK_TYPE_NONE) return 1.0;

    float m = 1.0;
    if(mask.Type == MASK_TYPE_SHOULDER)
    {
        m = EvaluateShoulder(signedDistance, roadWidth, mask.Shoulder);
	}
	else if(mask.Type == MASK_TYPE_NOISE)
	{
		m = EvaluateNoise(progress, signedDistance, roadWidth, mask.Noise);
	}

	return saturate(m * mask.Strength);
}

// 兼容旧签名：保持 compute 侧不改动
inline float EvaluateMask(GpuMaskParams maskParams, float2 worldPos, float progress, float distanceFromPath, float roadWidth)
{
	return EvaluateMask(progress, distanceFromPath, roadWidth, maskParams);
}

#endif // BLEND_MASK_LIBRARY_INCLUDED
