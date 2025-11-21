#ifndef BLENDING_LIBRARY_HLSL
#define BLENDING_LIBRARY_HLSL

// Blending modes
#define BLEND_MODE_NORMAL 0
#define BLEND_MODE_MULTIPLY 1
#define BLEND_MODE_OVERLAY 2
#define BLEND_MODE_SOFT_LIGHT 3
#define BLEND_MODE_HARD_LIGHT 4

inline float BlendOverlayf(float b, float l) { return (b < 0.5) ? (2.0 * b * l) : (1.0 - 2.0 * (1.0 - b) * (1.0 - l)); }
inline float BlendScreenf(float b, float l) { return 1.0 - (1.0 - b) * (1.0 - l); }

// Normal blending
inline float4 BlendNormal(float4 base, float4 overlay, float strength)
{
	return lerp(base, overlay, saturate(strength));
}

// Multiply blending
inline float4 BlendMultiply(float4 base, float4 overlay, float strength)
{
	float4 result = base * overlay;
	return lerp(base, result, saturate(strength));
}

// Overlay blending
inline float4 BlendOverlay(float4 base, float4 overlay, float strength)
{
	float4 result;
	result.r = (base.r < 0.5) ? (2.0 * base.r * overlay.r) : (1.0 - 2.0 * (1.0 - base.r) * (1.0 - overlay.r));
	result.g = (base.g < 0.5) ? (2.0 * base.g * overlay.g) : (1.0 - 2.0 * (1.0 - base.g) * (1.0 - overlay.g));
	result.b = (base.b < 0.5) ? (2.0 * base.b * overlay.b) : (1.0 - 2.0 * (1.0 - base.b) * (1.0 - overlay.b));
	result.a = (base.a < 0.5) ? (2.0 * base.a * overlay.a) : (1.0 - 2.0 * (1.0 - base.a) * (1.0 - overlay.a));
	return lerp(base, result, saturate(strength));
}

// Soft light blending
inline float4 BlendSoftLight(float4 base, float4 overlay, float strength)
{
	float softR = (overlay.r < 0.5)
		              ? (2.0 * base.r * overlay.r + base.r * base.r * (1.0 - 2.0 * overlay.r))
		              : (sqrt(max(0.0, base.r)) * (2.0 * overlay.r - 1.0) + 2.0 * base.r * (1.0 - overlay.r));
	float softG = (overlay.g < 0.5)
		              ? (2.0 * base.g * overlay.g + base.g * base.g * (1.0 - 2.0 * overlay.g))
		              : (sqrt(max(0.0, base.g)) * (2.0 * overlay.g - 1.0) + 2.0 * base.g * (1.0 - overlay.g));
	float softB = (overlay.b < 0.5)
		              ? (2.0 * base.b * overlay.b + base.b * base.b * (1.0 - 2.0 * overlay.b))
		              : (sqrt(max(0.0, base.b)) * (2.0 * overlay.b - 1.0) + 2.0 * base.b * (1.0 - overlay.b));
	float softA = (overlay.a < 0.5)
		              ? (2.0 * base.a * overlay.a + base.a * base.a * (1.0 - 2.0 * overlay.a))
		              : (sqrt(max(0.0, base.a)) * (2.0 * overlay.a - 1.0) + 2.0 * base.a * (1.0 - overlay.a));
	float4 result = float4(softR, softG, softB, softA);
	return lerp(base, result, saturate(strength));
}

// Hard light blending
inline float4 BlendHardLight(float4 base, float4 overlay, float strength)
{
	float4 result;
	result.r = (overlay.r < 0.5) ? (2.0 * base.r * overlay.r) : (1.0 - 2.0 * (1.0 - base.r) * (1.0 - overlay.r));
	result.g = (overlay.g < 0.5) ? (2.0 * base.g * overlay.g) : (1.0 - 2.0 * (1.0 - base.g) * (1.0 - overlay.g));
	result.b = (overlay.b < 0.5) ? (2.0 * base.b * overlay.b) : (1.0 - 2.0 * (1.0 - base.b) * (1.0 - overlay.b));
	result.a = (overlay.a < 0.5) ? (2.0 * base.a * overlay.a) : (1.0 - 2.0 * (1.0 - base.a) * (1.0 - overlay.a));
	return lerp(base, result, saturate(strength));
}

// Main blending function
inline float4 ApplyBlending(float4 base, float4 overlay, float strength, int blendMode)
{
	switch(blendMode)
	{
	case BLEND_MODE_MULTIPLY: return BlendMultiply(base, overlay, strength);
	case BLEND_MODE_OVERLAY: return BlendOverlay(base, overlay, strength);
	case BLEND_MODE_SOFT_LIGHT: return BlendSoftLight(base, overlay, strength);
	case BLEND_MODE_HARD_LIGHT: return BlendHardLight(base, overlay, strength);
	case BLEND_MODE_NORMAL:
	default: return BlendNormal(base, overlay, strength);
	}
}

inline float BlendWeight(float baseWeight, float layerWeight, int blendMode)
{
	// Layer weight acts as the overlay intensity; keep baseWeight for continuity
	switch(blendMode)
	{
	case 1: return saturate(baseWeight * layerWeight); // Multiply
	case 2: return saturate(baseWeight + layerWeight); // Add
	case 3: return saturate(BlendOverlayf(baseWeight, layerWeight)); // Overlay
	case 4: return saturate(BlendScreenf(baseWeight, layerWeight)); // Screen
	case 5: return lerp(baseWeight, layerWeight, saturate(layerWeight)); // Lerp by layer alpha
	case 6: return saturate(baseWeight + layerWeight); // Additive
	default: return saturate(layerWeight); // Normal/override
	}
}

// Normalize RGBA weights while keeping ratios; do not erase colors
inline float4 NormalizeWeightsKeep(float4 w)
{
	const float threshold = 1e-4;
	float       sum = w.r + w.g + w.b + w.a;
	int         painted = ((w.r > threshold) ? 1 : 0)
	+ ((w.g > threshold) ? 1 : 0)
	+ ((w.b > threshold) ? 1 : 0)
	+ ((w.a > threshold) ? 1 : 0);
	if(painted > 1 && sum > 1e-5)
	{
		w *= (1.0 / sum);
	}
	return saturate(w);
}

#endif // BLENDING_LIBRARY_HLSL
