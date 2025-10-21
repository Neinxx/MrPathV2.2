#ifndef BLEND_MASK_LIBRARY_INCLUDED
#define BLEND_MASK_LIBRARY_INCLUDED

// Include noise functions if needed (e.g., from Unity Mathematics or custom)
// #include "Packages/com.unity.mathematics/Unity.Mathematics/noise.hlsl"

// --- GPU Structs (Must match C# exactly) ---
struct GpuShoulderMaskParams {
    float ShoulderWidthRatio;
    float ShoulderStrength;
    float EdgeFalloff;
    int EnableLeftShoulder;
    int EnableRightShoulder;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
    // AnimationCurve needs approximation (e.g., sample points to LUT or polynomial)
};

struct GpuNoiseMaskParams {
    float Strength;
    float Seed;
    float2 Tiling;
    float2 Offset;
    float OverallScale;
    float Smooth;
};
// ... Structs for Gradient, RoadSurface etc. ...

struct GpuMaskParams {
    int MaskType; // 0=None, 1=Shoulder, 2=Noise, 3=Gradient, 4=RoadSurface...
    GpuShoulderMaskParams ShoulderParams;
    GpuNoiseMaskParams NoiseParams;
    // ... other mask structs ...
};

// --- Helper: Apply Smoothing (ported from C#) ---
float ApplySmoothing(float maskValue, float smooth)
{
    if (smooth <= 0.0f) return maskValue;
    float edge0 = smooth * 0.5f;
    float edge1 = 1.0f - smooth * 0.5f;
    float t = saturate((maskValue - edge0) / max(1e-6f, edge1 - edge0));
    return t * t * (3.0f - 2.0f * t);
}

// --- Helper: Transform Coords (ported from C#) ---
float TransformPosition(float horizontalPosition, float worldWidth, float2 tilingOffset)
{
    float u = (horizontalPosition + 1.0f) * 0.5f; // -1..1 => 0..1
    // Preserve sign of tiling.x to allow negative values (mirroring)
    float denom = (abs(tilingOffset.x) < 0.0001f) ? (0.0001f * (tilingOffset.x < 0.0f ? -1.0f : 1.0f)) : tilingOffset.x;
    float repeatCount = worldWidth / denom; // 保留符号以支持镜像
    return u * repeatCount + tilingOffset.y; // offset.x
}

float TransformPathPosition(float pathProgress, float pathLength, float2 tilingOffset)
{
    // Preserve sign of tiling.x (actually y passed in) to allow negative values (mirroring)
    float denom = (abs(tilingOffset.x) < 0.0001f) ? (0.0001f * (tilingOffset.x < 0.0f ? -1.0f : 1.0f)) : tilingOffset.x;
    float repeatCount = pathLength / denom; // 保留符号以支持镜像
    return pathProgress * repeatCount + tilingOffset.y; // offset.y
}


// --- Evaluate Functions (Implement logic here!) ---

float EvaluateShoulderMask(float posAcross, float pathProgress, float worldW, float pathLen, GpuShoulderMaskParams p)
{
    // TODO: Implement HLSL logic similar to ShoulderMask.cs Evaluate
    float absPos = abs(posAcross);
    float innerBoundary = 1.0 - p.ShoulderWidthRatio;
    if (absPos < innerBoundary) return 0.0f;

    bool isLeft = posAcross < 0.0f;
    bool enabled = (isLeft && p.EnableLeftShoulder > 0) || (!isLeft && p.EnableRightShoulder > 0);
    if (!enabled) return 0.0f;

    float shoulderWidth = max(1e-6f, p.ShoulderWidthRatio); // Avoid div by zero
    float relPos = saturate((absPos - innerBoundary) / shoulderWidth);

    // Evaluate profile curve (needs approximation/LUT)
    float profileValue = relPos; // Placeholder - Use LUT sampling or polynomial

    // Apply edge falloff
    if (p.EdgeFalloff > 0.0f) {
         float falloffDist = p.EdgeFalloff / shoulderWidth; // Normalize falloff
         if (relPos < falloffDist) {
             profileValue *= saturate(relPos / max(1e-6f, falloffDist));
         }
    }

    float finalValue = profileValue * p.ShoulderStrength * p.OverallScale;
    return ApplySmoothing(finalValue, p.Smooth);
}

float EvaluateNoiseMask(float posAcross, float pathProgress, float worldW, float pathLen, GpuNoiseMaskParams p)
{
    // TODO: Implement HLSL logic using noise functions
    float u = TransformPosition(posAcross, worldW, float2(p.Tiling.x, p.Offset.x));
    float v = TransformPathPosition(pathProgress, pathLen, float2(p.Tiling.y, p.Offset.y));

    // Use a noise function (e.g., Unity.Mathematics.noise.snoise)
    // float noiseVal = snoise(float2(u, v) + p.Seed); // Value in -1..1 range usually
    // noiseVal = noiseVal * 0.5 + 0.5; // Remap to 0..1
    float noiseVal = 0.5f; // Placeholder - Replace with actual noise call

    float finalValue = noiseVal * p.Strength * p.OverallScale;
    return ApplySmoothing(finalValue, p.Smooth);
}

// ... Implement Evaluate functions for Gradient, RoadSurface etc. ...

// --- Main Evaluate Function ---
float EvaluateMask(float posAcross, float pathProgress, float worldW, float pathLen, GpuMaskParams p)
{
    switch (p.MaskType)
    {
        case 1: return EvaluateShoulderMask(posAcross, pathProgress, worldW, pathLen, p.ShoulderParams);
        case 2: return EvaluateNoiseMask(posAcross, pathProgress, worldW, pathLen, p.NoiseParams);
        // case 3: return EvaluateGradientMask(...);
        // case 4: return EvaluateRoadSurfaceMask(...);
        default: return 0.0f; // No mask or unknown
    }
}


#endif // BLEND_MASK_LIBRARY_INCLUDED