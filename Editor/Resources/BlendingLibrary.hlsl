#ifndef BLENDING_LIBRARY_HLSL
#define BLENDING_LIBRARY_HLSL

// Blending modes
#define BLEND_MODE_NORMAL 0
#define BLEND_MODE_MULTIPLY 1
#define BLEND_MODE_OVERLAY 2
#define BLEND_MODE_SOFT_LIGHT 3
#define BLEND_MODE_HARD_LIGHT 4

// Normal blending
float4 BlendNormal(float4 base, float4 overlay, float strength)
{
    return lerp(base, overlay, strength);
}

// Multiply blending
float4 BlendMultiply(float4 base, float4 overlay, float strength)
{
    float4 result = base * overlay;
    return lerp(base, result, strength);
}

// Overlay blending
float4 BlendOverlay(float4 base, float4 overlay, float strength)
{
    float4 result;
    result.r = base.r < 0.5 ? 2.0 * base.r * overlay.r : 1.0 - 2.0 * (1.0 - base.r) * (1.0 - overlay.r);
    result.g = base.g < 0.5 ? 2.0 * base.g * overlay.g : 1.0 - 2.0 * (1.0 - base.g) * (1.0 - overlay.g);
    result.b = base.b < 0.5 ? 2.0 * base.b * overlay.b : 1.0 - 2.0 * (1.0 - base.b) * (1.0 - overlay.b);
    result.a = base.a < 0.5 ? 2.0 * base.a * overlay.a : 1.0 - 2.0 * (1.0 - base.a) * (1.0 - overlay.a);
    return lerp(base, result, strength);
}

// Soft light blending
float4 BlendSoftLight(float4 base, float4 overlay, float strength)
{
    float4 result;
    result.r = overlay.r < 0.5 ? 2.0 * base.r * overlay.r + base.r * base.r * (1.0 - 2.0 * overlay.r) : 
               sqrt(base.r) * (2.0 * overlay.r - 1.0) + 2.0 * base.r * (1.0 - overlay.r);
    result.g = overlay.g < 0.5 ? 2.0 * base.g * overlay.g + base.g * base.g * (1.0 - 2.0 * overlay.g) : 
               sqrt(base.g) * (2.0 * overlay.g - 1.0) + 2.0 * base.g * (1.0 - overlay.g);
    result.b = overlay.b < 0.5 ? 2.0 * base.b * overlay.b + base.b * base.b * (1.0 - 2.0 * overlay.b) : 
               sqrt(base.b) * (2.0 * overlay.b - 1.0) + 2.0 * base.b * (1.0 - overlay.b);
    result.a = overlay.a < 0.5 ? 2.0 * base.a * overlay.a + base.a * base.a * (1.0 - 2.0 * overlay.a) : 
               sqrt(base.a) * (2.0 * overlay.a - 1.0) + 2.0 * base.a * (1.0 - overlay.a);
    return lerp(base, result, strength);
}

// Hard light blending
float4 BlendHardLight(float4 base, float4 overlay, float strength)
{
    float4 result;
    result.r = overlay.r < 0.5 ? 2.0 * base.r * overlay.r : 1.0 - 2.0 * (1.0 - base.r) * (1.0 - overlay.r);
    result.g = overlay.g < 0.5 ? 2.0 * base.g * overlay.g : 1.0 - 2.0 * (1.0 - base.g) * (1.0 - overlay.g);
    result.b = overlay.b < 0.5 ? 2.0 * base.b * overlay.b : 1.0 - 2.0 * (1.0 - base.b) * (1.0 - overlay.b);
    result.a = overlay.a < 0.5 ? 2.0 * base.a * overlay.a : 1.0 - 2.0 * (1.0 - base.a) * (1.0 - overlay.a);
    return lerp(base, result, strength);
}

// Main blending function
float4 ApplyBlending(float4 base, float4 overlay, float strength, int blendMode)
{
    switch (blendMode)
    {
        case BLEND_MODE_MULTIPLY:
            return BlendMultiply(base, overlay, strength);
        case BLEND_MODE_OVERLAY:
            return BlendOverlay(base, overlay, strength);
        case BLEND_MODE_SOFT_LIGHT:
            return BlendSoftLight(base, overlay, strength);
        case BLEND_MODE_HARD_LIGHT:
            return BlendHardLight(base, overlay, strength);
        default:
            return BlendNormal(base, overlay, strength);
    }
}

#endif // BLENDING_LIBRARY_HLSL