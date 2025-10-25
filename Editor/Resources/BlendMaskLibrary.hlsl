#ifndef BLEND_MASK_LIBRARY_INCLUDED
#define BLEND_MASK_LIBRARY_INCLUDED

// 与 C# 侧 RecipeGpuDataManager.cs 的结构对齐
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
	int    AlgorithmId; // 0=Perlin, 1=Simple
	float  Pad1; // 对齐
};

struct GpuShoulderMaskParams {
	float Width; // 比例值：与道路宽度相乘得到实际肩宽
	float Softness; // 软化/边缘过渡系数
	float Strength; // 肩部影响强度
	float Pad;
};

struct GpuMaskParams {
	int                   Type; // MASK_TYPE_*
	float                 Strength; // 顶层遮罩强度
	GpuNoiseMaskParams    Noise;
	GpuShoulderMaskParams Shoulder;
};

// 掩码类型常量（需与 C# 保持一致）
static const int MASK_TYPE_NONE = 0;
static const int MASK_TYPE_NOISE = 1;
static const int MASK_TYPE_SHOULDER = 2;

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

// 噪声工具（置于库内部，供 EvaluateNoise 使用）
inline float2 rotate2(float2 p, float a)
{
	float s = sin(a), c = cos(a);
	return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

inline float noise_fade(float t) { return t * t * (3.0 - 2.0 * t); }

inline float hash21(float2 p)
{
	p = frac(p * float2(123.34, 345.45));
	p += dot(p, p + 34.345);
	return frac(p.x * p.y);
}

inline float grad2(float2 ip, float2 f)
{
	float  a = hash21(ip) * 6.28318530718;
	float2 g = float2(cos(a), sin(a));
	return dot(g, f);
}

inline float perlin2d(float2 p)
{
	float2 ip = floor(p);
	float2 f = frac(p);
	float2 u = float2(noise_fade(f.x), noise_fade(f.y));
	float  n00 = grad2(ip + float2(0, 0), f - float2(0, 0));
	float  n10 = grad2(ip + float2(1, 0), f - float2(1, 0));
	float  n01 = grad2(ip + float2(0, 1), f - float2(0, 1));
	float  n11 = grad2(ip + float2(1, 1), f - float2(1, 1));
	float  nx0 = lerp(n00, n10, u.x);
	float  nx1 = lerp(n01, n11, u.x);
	return lerp(nx0, nx1, u.y);
}

inline float sampleBaseNoise(float2 p, int algo) { return (algo == 0) ? perlin2d(p) : sin(p.x) * cos(p.y); }

inline float fbm2d(float2 p, int octaves, float lacunarity, float gain, int algo)
{
	float amp = 0.5;
	float freq = 1.0;
	float sum = 0.0;
	[loop] for(int i = 0; i < octaves; i++)
	{
		sum += sampleBaseNoise(p * freq, algo) * amp;
		freq *= max(lacunarity, 1.0);
		amp *= saturate(gain);
	}
	return sum;
}

inline float EvaluateNoise(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
{
	float2 uv = ComputeMaskUV(progress, signedDistance, roadWidth, p);

	// 综合整体缩放、细节缩放与旋转
	float2 m = uv * max(p.OverallScale, 1e-6);
	m = m * float2(max(p.NoiseScale.x, 1e-6), max(p.NoiseScale.y, 1e-6));
	m = rotate2(m, p.RotationRad);
	m += float2(p.Seed * 17.0, p.Seed * 29.0);

	int   oct = max(p.Octaves, 1);
	float n = fbm2d(m, oct, max(p.Lacunarity, 1.0), p.Gain, p.AlgorithmId); // [-1,1]
	float n01 = n * 0.5 + 0.5;

	// 平滑
	float s = saturate(p.Smooth);
	n01 = lerp(n01, smoothstep(0.0, 1.0, n01), s);

	return saturate(n01 * p.Strength);
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
	return saturate(softened * p.Strength);
}

// 主评估函数（统一坐标）：使用 progress / signedDistance / roadWidth
inline float EvaluateMask(float progress, float signedDistance, float roadWidth, GpuMaskParams mask)
{
	if(mask.Type == MASK_TYPE_NONE) return 1.0;

	float m = 1.0;
	if(mask.Type == MASK_TYPE_NOISE)
	{
		m = EvaluateNoise(progress, signedDistance, roadWidth, mask.Noise);
	}
	else if(mask.Type == MASK_TYPE_SHOULDER)
	{
		m = EvaluateShoulder(signedDistance, roadWidth, mask.Shoulder);
	}

	return saturate(m * mask.Strength);
}

// 兼容旧签名：保持 compute 侧不改动
inline float EvaluateMask(GpuMaskParams maskParams, float2 worldPos, float progress, float distanceFromPath, float roadWidth)
{
	return EvaluateMask(progress, distanceFromPath, roadWidth, maskParams);
}

#endif // BLEND_MASK_LIBRARY_INCLUDED
