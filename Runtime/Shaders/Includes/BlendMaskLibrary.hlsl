#ifndef BLEND_MASK_LIBRARY_INCLUDED
#define BLEND_MASK_LIBRARY_INCLUDED

// -----------------------------------------------------------------------------
// Runtime BlendMaskLibrary.hlsl
// Unified with Editor version: struct layouts, mask type constants, and
// Evaluate* APIs. Includes backward-compatible wrapper for old signatures.
// -----------------------------------------------------------------------------

// Mask types (keep consistent across editor/runtime)
#define MASK_TYPE_NONE 0
#define MASK_TYPE_SHOULDER 1
#define MASK_TYPE_NOISE 2
#define MASK_TYPE_GRADIENT 3

// --- GPU Structs (must match C# memory layout exactly) ---
struct GpuShoulderMaskParams {
    float ShoulderWidthRatio;
    float ShoulderStrength;
    float EdgeFalloff;
    int   EnableLeftShoulder;
    int   EnableRightShoulder;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
    float Pad1;
    float Pad2;
};

struct GpuNoiseMaskParams {
    float Strength;
    float Seed;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
    float Pad1;
};

struct GpuMaskParams {
    int   maskType;   // C#: MaskType
    float strength;   // C#: Strength
    float2 padding;   // C#: Pad2, Pad3
    GpuShoulderMaskParams shoulderParams;
    GpuNoiseMaskParams    noiseParams;
};

// Provide name aliases to ease transition from older HLSL using MaskType/Strength
#define MaskType  maskType
#define Strength  strength

// --- Simple noise helpers (placeholder, fast pseudo-perlin) ---
float SimpleNoise(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

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

// --- Smoothing helper ---
float ApplySmoothing(float value, float smoothing)
{
    return smoothstep(0.0, 1.0, value * (1.0 + smoothing));
}

// --- Evaluate shoulder mask (distance-based, unified) ---
float EvaluateShoulderMask(GpuShoulderMaskParams p, float distanceFromPath, float roadWidth)
{
    // Inside road core: no shoulder
    float coreHalfWidth = roadWidth * 0.5;
    float d = abs(distanceFromPath) - coreHalfWidth;
    if (d <= 0.0) return 0.0;

    // Shoulder span
    float shoulderWidth = max(1e-5, p.ShoulderWidthRatio * roadWidth);
    if (d <= shoulderWidth) return saturate(p.ShoulderStrength);

    // Edge falloff beyond shoulder
    float beyond = d - shoulderWidth;
    float falloff = 1.0 - saturate(beyond / max(1e-5, p.EdgeFalloff));
    return saturate(falloff * p.ShoulderStrength);
}

// --- Evaluate procedural noise mask (world-space) ---
float EvaluateNoiseMask(GpuNoiseMaskParams p, float2 worldPos)
{
    float2 uv = (worldPos * p.OverallScale) * p.Tiling + p.Offset + p.Seed;
    float n = PerlinNoise(uv, max(1e-5, p.OverallScale));
    n = ApplySmoothing(n, p.Smooth);
    return saturate(n * p.Strength);
}

// --- Evaluate gradient (progress parameter) ---
float EvaluateGradientMask(float progress, float strength)
{
    return progress * strength;
}

// --- Main unified EvaluateMask ---
float EvaluateMask(GpuMaskParams maskParams, float2 worldPos, float progress, float distanceFromPath, float roadWidth)
{
    float maskValue = 1.0;
    switch (maskParams.maskType)
    {
        case MASK_TYPE_SHOULDER:
            maskValue = EvaluateShoulderMask(maskParams.shoulderParams, distanceFromPath, roadWidth);
            break;
        case MASK_TYPE_NOISE:
            maskValue = EvaluateNoiseMask(maskParams.noiseParams, worldPos);
            break;
        case MASK_TYPE_GRADIENT:
            maskValue = EvaluateGradientMask(progress, 1.0);
            break;
        case MASK_TYPE_NONE:
        default:
            maskValue = 1.0;
            break;
    }
    return saturate(maskValue * maskParams.strength);
}

// --- Backward-compatible wrapper (old signature) ---
// Maps posAcross/pathProgress/worldW/pathLen to new API.
float EvaluateMask(float posAcross, float pathProgress, float worldW, float pathLen, GpuMaskParams p)
{
    // Estimate signed distance from path center in world units
    float distanceFromPath = posAcross * (worldW * 0.5);
    // Derive a world-space pos (approx) for noise masks
    float2 worldPos = float2(posAcross * worldW, pathProgress * pathLen);
    return EvaluateMask(p, worldPos, pathProgress, distanceFromPath, worldW);
}

#endif // BLEND_MASK_LIBRARY_INCLUDED