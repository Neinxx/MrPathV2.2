#ifndef BLEND_MASK_LIBRARY_INCLUDED
#define BLEND_MASK_LIBRARY_INCLUDED

// 掩码类型常量（保持与 C# / MaskAtlas.compute 一致）
static const int MASK_TYPE_NONE = 0;
static const int MASK_TYPE_SHOULDER = 1;
static const int MASK_TYPE_NOISE = 2;
static const int MASK_TYPE_GRADIENT = 3;
static const int MASK_TYPE_SHOULDER_NOISE = 4;
static const int NOISE_VARIANT_FBM = 0;
static const int NOISE_VARIANT_STRIPE = 1;
static const int NOISE_VARIANT_WORLEY = 2;

// --- GPU 结构体（与 Runtime/Core/MaskAtlasGenerator.cs 完全对齐） ---
struct GpuShoulderMaskParams {
    float  ShoulderWidthRatio;
    float  PositionRatio;      // 0..1 路肩中心向内位移比例
    float  ShoulderStrength;
    float  EdgeFalloff;        // 相对道路宽度的软化距离比例
    int    EnableLeftShoulder;
    int    EnableRightShoulder;
    float2 Tiling;             // 统一 UV 平铺（重复次数语义）
    float2 Offset;             // 统一 UV 偏移
    float  OverallScale;       // 顶层整体缩放
    float  Smooth;             // 顶层平滑
    float  Pad1;               // 对齐填充
    float  Pad2;               // 对齐填充
};

struct GpuNoiseMaskParams {
    float  Strength;
    float  Seed;
    float2 Tiling;
    float2 Offset;
    float  OverallScale;
    float  Smooth;
    float2 NoiseScale;
    float  RotationRad;
    int    Octaves;
    float  Lacunarity;
    float  Gain;
    int    UseAsymmetricEdges;
    float  EdgeLow;
    float  EdgeHigh;
    float  Period;
    float  Jitter;
    int    Invert;
    int    Variant;
};

struct GpuMaskParams {
    int                   Type;      // C#: MaskType
    float                 Strength;  // 顶层强度
    float2                Padding;   // C#: Pad2/Pad3
    GpuNoiseMaskParams    Noise;     // C#: NoiseParams
    GpuShoulderMaskParams Shoulder;  // C#: ShoulderParams
};

// 外部 Noise LUT（CPU/GPU 共享）
Texture2D<float> _NoiseLUT;
int              _NoiseLutSize;

// 计算着色器中手写重复环绕的双线性采样
inline float SampleNoiseLUT(float2 uv)
{
    if(_NoiseLutSize <= 0) return 0.5;
    float2 st = frac(uv) * (float)_NoiseLutSize - 0.5;
    uint2  i0 = (uint2)floor(st);
    float2 f = frac(st);
    uint2  i1 = i0 + uint2(1, 0);
    uint2  i2 = i0 + uint2(0, 1);
    uint2  i3 = i0 + uint2(1, 1);
    int    size = _NoiseLutSize;
    uint2  wrap = uint2(size, size);
    uint2  w0 = (i0 % wrap + wrap) % wrap;
    uint2  w1 = (i1 % wrap + wrap) % wrap;
    uint2  w2 = (i2 % wrap + wrap) % wrap;
    uint2  w3 = (i3 % wrap + wrap) % wrap;
    float  c00 = _NoiseLUT.Load(uint3(w0, 0)).r;
    float  c10 = _NoiseLUT.Load(uint3(w1, 0)).r;
    float  c01 = _NoiseLUT.Load(uint3(w2, 0)).r;
    float  c11 = _NoiseLUT.Load(uint3(w3, 0)).r;
    float  cx0 = lerp(c00, c10, f.x);
    float  cx1 = lerp(c01, c11, f.x);
    return lerp(cx0, cx1, f.y);
}

// 统一 UV 计算：与 CPU TransformPosition/TransformPathPosition 对齐（重复次数语义）
inline float2 ComputeMaskUV(float progress, float signedDistance, float roadWidth, float2 tiling, float2 offset)
{
    float halfRoad = max(1e-4, roadWidth * 0.5);
    float xNorm = clamp(signedDistance / halfRoad, -1.0, 1.0);
    float u01 = 0.5 * (xNorm + 1.0);

    float repeatX = tiling.x;
    float repeatY = tiling.y;
    float denomX = (abs(repeatX) < 1e-4) ? (1e-4 * ((repeatX == 0.0) ? 1.0 : sign(repeatX))) : repeatX;
    float denomY = (abs(repeatY) < 1e-4) ? (1e-4 * ((repeatY == 0.0) ? 1.0 : sign(repeatY))) : repeatY;

    float u = u01 * denomX + offset.x;
    float v = progress * denomY + offset.y;
    return float2(u, v);
}

// 重载：允许直接传入 GpuNoiseMaskParams，提取其 Tiling/Offset
inline float2 ComputeMaskUV(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
{
    return ComputeMaskUV(progress, signedDistance, roadWidth, p.Tiling, p.Offset);
}

inline float2 rotate2(float2 p, float a)
{
    float s = sin(a), c = cos(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// 噪声遮罩评估（与 CPU 语义一致：先 overallScale，再平滑）
inline float EvaluateNoise(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
{
    float2 uv = ComputeMaskUV(progress, signedDistance, roadWidth, p.Tiling, p.Offset);
    float2 m = uv * float2(max(p.NoiseScale.x, 1e-6), max(p.NoiseScale.y, 1e-6));
    m = rotate2(m, p.RotationRad);
    m += float2(p.Seed * 17.0, p.Seed * 29.0);
    float n01 = 0.0;
    if(p.Variant == NOISE_VARIANT_FBM)
    {
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
        n01 = (norm > 1e-5) ? (sum / norm) : 0.0;
    }
    else if(p.Variant == NOISE_VARIANT_STRIPE)
    {
        float phase = m.x * p.Period + SampleNoiseLUT(m) * p.Jitter;
        n01 = 0.5 * (sin(phase) + 1.0);
    }
    else if(p.Variant == NOISE_VARIANT_WORLEY)
    {
        float px = m.x * p.Period;
        float py = m.y * p.Period;
        float ix = floor(px);
        float iy = floor(py);
        float fx = px - ix;
        float fy = py - iy;
        float dmin = 1e9;
        for(int dy = 0; dy <= 1; ++dy)
        {
            for(int dx = 0; dx <= 1; ++dx)
            {
                float cx = ix + dx;
                float cy = iy + dy;
                float h = SampleNoiseLUT(float2(cx * 0.071, cy * 0.113));
                float jx = frac(h * 1.618);
                float jy = frac(h * 2.414);
                jx = lerp(0.5, jx, p.Jitter);
                jy = lerp(0.5, jy, p.Jitter);
                float vx = dx + jx - fx;
                float vy = dy + jy - fy;
                float d = sqrt(vx * vx + vy * vy);
                dmin = (d < dmin) ? d : dmin;
            }
        }
        n01 = saturate(dmin);
        if(p.Invert != 0) n01 = 1.0 - n01;
    }

    float pre = saturate(n01 * p.Strength) * p.OverallScale;
    if(p.UseAsymmetricEdges != 0)
    {
        float e0 = p.EdgeLow;
        float e1 = p.EdgeHigh;
        if(e0 > e1) { float t = e0; e0 = e1; e1 = t; }
        return saturate(smoothstep(e0, e1, pre));
    }
    if(p.Smooth <= 1e-5) return saturate(pre);
    float edge0 = p.Smooth * 0.5;
    float edge1 = 1.0 - p.Smooth * 0.5;
    return saturate(smoothstep(edge0, edge1, pre));
}

inline float ShoulderStripeWeight(float distanceFromPath, float center, float stripeHalf, float falloffW)
{
    float d = abs(distanceFromPath - center);
    if(falloffW <= 1e-6) return (d <= stripeHalf) ? 1.0 : 0.0;
    float t = 1.0 - saturate((d - stripeHalf) / falloffW);
    return saturate(t);
}

// 路肩遮罩评估：左右两条路肩条带的最大值
inline float EvaluateShoulder(float signedDistance, float roadWidth, GpuShoulderMaskParams p)
{
    float halfRoad = max(1e-5, roadWidth * 0.5);
    float inset    = saturate(p.PositionRatio) * halfRoad;
    float leftC    = -halfRoad + inset;
    float rightC   =  halfRoad - inset;
    float stripeHalf = max(1e-5, p.ShoulderWidthRatio * roadWidth * 0.5);
    float falloffW   = max(1e-5, p.EdgeFalloff * roadWidth);

    float wLeft  = (p.EnableLeftShoulder  != 0) ? ShoulderStripeWeight(signedDistance, leftC,  stripeHalf, falloffW)  : 0.0;
    float wRight = (p.EnableRightShoulder != 0) ? ShoulderStripeWeight(signedDistance, rightC, stripeHalf, falloffW) : 0.0;
    float shoulderShape = max(wLeft, wRight) * p.ShoulderStrength;

    float pre = saturate(shoulderShape) * p.OverallScale;
    if(p.Smooth <= 1e-5) return saturate(pre);
    float edge0 = p.Smooth * 0.5;
    float edge1 = 1.0 - p.Smooth * 0.5;
    return saturate(smoothstep(edge0, edge1, pre));
}

// 组合：路肩条带 × 噪声
inline float EvaluateShoulderNoise(GpuShoulderMaskParams sp, GpuNoiseMaskParams np, float2 worldPos, float progress, float signedDistance, float roadWidth)
{
    float shoulderVal = EvaluateShoulder(signedDistance, roadWidth, sp);
    float noiseVal    = EvaluateNoise(progress, signedDistance, roadWidth, np);
    float pre         = saturate(shoulderVal * noiseVal);
    if(sp.Smooth <= 1e-5) return pre * sp.OverallScale;
    float edge0 = sp.Smooth * 0.5;
    float edge1 = 1.0 - sp.Smooth * 0.5;
    return saturate(smoothstep(edge0, edge1, pre) * sp.OverallScale);
}

inline float EvaluateGradient(float progress)
{
    // 简化版渐变：与 MaskAtlas.compute 保持一致；复杂曲线留给 CPU 路径
    return saturate(progress);
}

// 统一评估入口
inline float EvaluateMask(GpuMaskParams mask, float2 worldPos, float progress, float signedDistance, float roadWidth)
{
    if(mask.Type == MASK_TYPE_NONE) return 1.0;

    float m = 1.0;
    if(mask.Type == MASK_TYPE_SHOULDER)
        m = EvaluateShoulder(signedDistance, roadWidth, mask.Shoulder);
    else if(mask.Type == MASK_TYPE_NOISE)
        m = EvaluateNoise(progress, signedDistance, roadWidth, mask.Noise);
    else if(mask.Type == MASK_TYPE_SHOULDER_NOISE)
        m = EvaluateShoulderNoise(mask.Shoulder, mask.Noise, worldPos, progress, signedDistance, roadWidth);
    else if(mask.Type == MASK_TYPE_GRADIENT)
        m = EvaluateGradient(progress);

    return saturate(m * mask.Strength);
}

#endif // BLEND_MASK_LIBRARY_INCLUDED
