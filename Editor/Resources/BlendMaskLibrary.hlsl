#ifndef BLEND_MASK_LIBRARY_HLSL
#define BLEND_MASK_LIBRARY_HLSL

// Mask types
#define MASK_TYPE_NONE 0
#define MASK_TYPE_SHOULDER 1
#define MASK_TYPE_NOISE 2
#define MASK_TYPE_GRADIENT 3

// Shoulder mask parameters
struct GpuShoulderMaskParams
{
    float shoulderWidth;
    float shoulderFalloff;
    float shoulderStrength;
    float padding;
};

// Noise mask parameters
struct GpuNoiseMaskParams
{
    float noiseScale;
    float noiseStrength;
    float2 noiseOffset;
};

// Main mask parameters
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
    uv *= scale;
    float2 i = floor(uv);
    float2 f = frac(uv);
    
    float a = SimpleNoise(i);
    float b = SimpleNoise(i + float2(1.0, 0.0));
    float c = SimpleNoise(i + float2(0.0, 1.0));
    float d = SimpleNoise(i + float2(1.0, 1.0));
    
    float2 u = f * f * (3.0 - 2.0 * f);
    
    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
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

// Evaluate shoulder mask
float EvaluateShoulderMask(GpuShoulderMaskParams params, float2 worldPos, float progress, float distanceFromPath)
{
    float shoulderDistance = abs(distanceFromPath) - params.shoulderWidth * 0.5;
    if (shoulderDistance <= 0.0) return 0.0; // Inside main path
    
    float falloff = 1.0 - saturate(shoulderDistance / params.shoulderFalloff);
    return falloff * params.shoulderStrength;
}

// Evaluate noise mask
float EvaluateNoiseMask(GpuNoiseMaskParams params, float2 worldPos, float progress)
{
    float2 noiseUV = worldPos + params.noiseOffset;
    float noise = PerlinNoise(noiseUV, params.noiseScale);
    return noise * params.noiseStrength;
}

// Evaluate gradient mask (based on progress along path)
float EvaluateGradientMask(float progress, float strength)
{
    // Simple linear gradient - can be enhanced with animation curves
    return progress * strength;
}

// Main mask evaluation function
float EvaluateMask(GpuMaskParams maskParams, float2 worldPos, float progress)
{
    float maskValue = 1.0;
    
    switch (maskParams.maskType)
    {
        case MASK_TYPE_SHOULDER:
        {
            float distanceFromPath = length(worldPos); // Simplified - should use actual distance from path
            maskValue = EvaluateShoulderMask(maskParams.shoulderParams, worldPos, progress, distanceFromPath);
            break;
        }
        case MASK_TYPE_NOISE:
        {
            maskValue = EvaluateNoiseMask(maskParams.noiseParams, worldPos, progress);
            break;
        }
        case MASK_TYPE_GRADIENT:
        {
            maskValue = EvaluateGradientMask(progress, maskParams.strength);
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