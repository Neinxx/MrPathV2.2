#ifndef BLENDING_LIBRARY_INCLUDED
#define BLENDING_LIBRARY_INCLUDED

// --- Blend Modes Enum (match C# BlendMode) ---
// Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6

// Helper for Overlay/Screen
float BlendOverlayf(float b, float l) { return b < 0.5 ? (2.0 * b * l) : (1.0 - 2.0 * (1.0 - b) * (1.0 - l)); }
float BlendScreenf(float b, float l) { return 1.0 - (1.0 - b) * (1.0 - l); }

// Main Blend Function (returns blended WEIGHT)
float BlendWeight(float baseWeight, float layerWeight, int blendMode)
{
    switch(blendMode)
    {
    case 1: return baseWeight * layerWeight;                  // Multiply
    case 2: return saturate(baseWeight + layerWeight);        // Add
    case 3: return BlendOverlayf(baseWeight, layerWeight);    // Overlay
    case 4: return BlendScreenf(baseWeight, layerWeight);     // Screen
    case 5: return lerp(baseWeight, layerWeight, saturate(layerWeight)); // Lerp (using layer as alpha)
    case 6: return saturate(baseWeight + layerWeight);        // Additive (same as Add)
    default: return layerWeight;                              // Normal (override)
    }
}

// Optional: Color Blend Function (if GPU calculates final color)
float4 BlendColor(float4 baseColor, float4 layerColor, int blendMode)
{
    // TODO: Implement color blending for each mode if needed
    return layerColor; // Placeholder
}


#endif // BLENDING_LIBRARY_INCLUDED