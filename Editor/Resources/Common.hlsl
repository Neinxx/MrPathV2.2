#ifndef COMMON_HLSL
#define COMMON_HLSL

// Common constants
#define PI 3.14159265359
#define TWO_PI 6.28318530718
#define HALF_PI 1.57079632679
#define INV_PI 0.31830988618
#define EPSILON 1e-6

// Common utility functions
float saturate(float x)
{
	return clamp(x, 0.0, 1.0);
}

float2 saturate(float2 x)
{
	return clamp(x, 0.0, 1.0);
}

float3 saturate(float3 x)
{
	return clamp(x, 0.0, 1.0);
}

float4 saturate(float4 x)
{
	return clamp(x, 0.0, 1.0);
}

// Smoothstep function
float smoothstep(float edge0, float edge1, float x)
{
	float t = saturate((x - edge0) / (edge1 - edge0));
	return t * t * (3.0 - 2.0 * t);
}

// Linear interpolation
float lerp(float a, float b, float t)
{
	return a + t * (b - a);
}

float2 lerp(float2 a, float2 b, float t)
{
	return a + t * (b - a);
}

float3 lerp(float3 a, float3 b, float t)
{
	return a + t * (b - a);
}

float4 lerp(float4 a, float4 b, float t)
{
	return a + t * (b - a);
}

// Distance functions
float distance(float2 a, float2 b)
{
	return length(a - b);
}

float distance(float3 a, float3 b)
{
	return length(a - b);
}

// Remap function
float remap(float value, float fromMin, float fromMax, float toMin, float toMax)
{
	return toMin + (value - fromMin) * (toMax - toMin) / (fromMax - fromMin);
}

// Safe division
float safeDivide(float a, float b, float fallback = 0.0)
{
	return abs(b) > EPSILON ? a / b : fallback;
}

// Angle utilities
float normalizeAngle(float angle)
{
	while(angle > PI) angle -= TWO_PI;
	while(angle < -PI) angle += TWO_PI;
	return angle;
}

// Vector utilities
float2 rotate2D(float2 v, float angle)
{
	float cosA = cos(angle);
	float sinA = sin(angle);
	return float2(v.x * cosA - v.y * sinA, v.x * sinA + v.y * cosA);
}

float2 perpendicular(float2 v)
{
	return float2(-v.y, v.x);
}

float2 normalize(float2 v)
{
	float len = length(v);
	return len > EPSILON ? v / len : float2(0, 0);
}

float3 normalize(float3 v)
{
	float len = length(v);
	return len > EPSILON ? v / len : float3(0, 0, 0);
}

// Hash functions for noise
float hash(float n)
{
	return frac(sin(n) * 43758.5453);
}

float hash(float2 p)
{
	return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

// Fade function for smooth noise
float fade(float t)
{
	return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

#endif // COMMON_HLSL
