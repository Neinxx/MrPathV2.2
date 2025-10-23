#ifndef BLEND_MASK_LIBRARY_HLSL
#define BLEND_MASK_LIBRARY_HLSL

// Mask types
#define MASK_TYPE_NONE 0
#define MASK_TYPE_SHOULDER 1
#define MASK_TYPE_NOISE 2
#define MASK_TYPE_GRADIENT 3

// Shoulder mask parameters (match C# layout)
struct GpuShoulderMaskParams
{
    float ShoulderWidthRatio;
    float ShoulderStrength;
    float EdgeFalloff;
    int EnableLeftShoulder;
    int EnableRightShoulder;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
    float Pad1;
    float Pad2;
};

// Noise mask parameters (match C# layout)
struct GpuNoiseMaskParams
{
    float Strength;
    float Seed;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
    float Pad1;
};

// Main mask parameters (match C# layout)
struct GpuMaskParams
{
    int maskType;
    float strength;
    float2 padding;
    GpuShoulderMaskParams shoulderParams;
    GpuNoiseMaskParams noiseParams;
};

// Simple noise function (placeholder - replace with proper noise)
float SimpleNoise(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

// Perlin-like noise (simplified)
float PerlinNoise(float2 uv, float scale)
{
    uv *= max(1e-5, scale);
    float2 i = floor(uv);
    float2 f = frac(uv);
    
    float a = SimpleNoise(i);
    float b = SimpleNoise(i + float2(1.0, 0.0));
    float c = SimpleNoise(i + float2(0.0, 1.0));
    float d = SimpleNoise(i + float2(1.0, 1.0));
    
    float2 u = f * f * (3.0 - 2.0 * f);
    
    float res = lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    return res;
}

// Apply smoothing to mask value
float ApplySmoothing(float value, float smoothing)
{
    return smoothstep(0.0, 1.0, value * (1.0 + smoothing));
}

// Transform position for mask evaluation
float2 TransformPosition(float2 worldPos, float2 offset, float rotation)
{
    float2 pos = worldPos + offset;
    
    if (abs(rotation) > 0.001)
    {
        float cosR = cos(rotation);
        float sinR = sin(rotation);
        pos = float2(
            pos.x * cosR - pos.y * sinR,
            pos.x * sinR + pos.y * cosR
        );
    }
    
    return pos;
}

// Transform position along path
float2 TransformPathPosition(float2 worldPos, float progress, float pathWidth)
{
    // Simple transformation - can be enhanced with proper path tangent/normal
    return worldPos;
}

// Evaluate shoulder mask using path distance and road width
float EvaluateShoulderMask(GpuShoulderMaskParams p, float distanceFromPath, float roadWidth)
{
    float coreHalfWidth = roadWidth * 0.5;
    float d = abs(distanceFromPath) - coreHalfWidth;
    if (d <= 0.0) return 0.0; // inside main road

    float shoulderWidth = max(1e-5, p.ShoulderWidthRatio * roadWidth);
    if (d <= shoulderWidth) return saturate(p.ShoulderStrength);

    float beyond = d - shoulderWidth;
    float falloff = 1.0 - saturate(beyond / max(1e-5, p.EdgeFalloff));
    return saturate(falloff * p.ShoulderStrength);
}

// Evaluate noise mask with tiling/offset/scale and smoothing
float EvaluateNoiseMask(GpuNoiseMaskParams p, float2 worldPos)
{
    // 米制 tiling 语义：tiling 为重复块的物理尺寸（米），允许负值镜像
    float tileX = p.Tiling.x;
    float tileY = p.Tiling.y;
    float denomX = (abs(tileX) < 1e-5) ? (1e-5 * ((tileX == 0.0) ? 1.0 : sign(tileX))) : tileX;
    float denomY = (abs(tileY) < 1e-5) ? (1e-5 * ((tileY == 0.0) ? 1.0 : sign(tileY))) : tileY;

    float2 uv;
    uv.x = worldPos.x / denomX + p.Offset.x;
    uv.y = worldPos.y / denomY + p.Offset.y;

    // 保持与 Runtime/Compute 一致的频率与幅值语义
    float n = PerlinNoise(uv, 1.0);

    // 先乘强度再乘总体缩放，然后按 smooth 平滑
    float valuePre = saturate(n * p.Strength) * p.OverallScale;
    if (p.Smooth <= 1e-5)
    {
        return saturate(valuePre);
    }
    float edge0 = p.Smooth * 0.5;
    float edge1 = 1.0 - p.Smooth * 0.5;
    return saturate(smoothstep(edge0, edge1, valuePre));
}

// Evaluate gradient mask (based on progress along path)
float EvaluateGradientMask(float progress, float strength)
{
    return progress * strength;
}

// Main mask evaluation function (now takes distance & roadWidth)
float EvaluateMask(GpuMaskParams maskParams, float2 worldPos, float progress, float distanceFromPath, float roadWidth)
{
    float maskValue = 1.0;
    switch (maskParams.maskType)
    {
        case MASK_TYPE_SHOULDER:
        {
            maskValue = EvaluateShoulderMask(maskParams.shoulderParams, distanceFromPath, roadWidth);
            break;
        }
        case MASK_TYPE_NOISE:
        {
            maskValue = EvaluateNoiseMask(maskParams.noiseParams, worldPos);
            break;
        }
        case MASK_TYPE_GRADIENT:
        {
            maskValue = EvaluateGradientMask(progress, 1.0);
            break;
        }
        case MASK_TYPE_NONE:
        default:
            maskValue = 1.0;
            break;
    }
    return saturate(maskValue * maskParams.strength);
}

#endif // BLEND_MASK_LIBRARY_HLSL