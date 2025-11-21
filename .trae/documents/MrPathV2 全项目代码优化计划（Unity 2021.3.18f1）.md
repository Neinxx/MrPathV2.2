您好，我是资深Unity开发代码优化专家。接下来我将按照既定工作流（审查→拆分职责→性能优化→现代化风格→API最佳实践）为整个项目给出系统化优化方案与落地计划。

## 项目概览
- 核心模块：`Runtime/Core`（路径、遮罩、噪声、预览、Jobs、内存）、`Runtime/Settings`、`Runtime/Providers`、`Editor`（预览、地形、UI、工具）、`Tests`（Editor/集成）。
- 优点：错误与日志统一（`ErrorHandler`）、资源加载支持依赖注入（`ResourceProvider`）、CPU/GPU 双路径遮罩图集（`MaskAtlasGenerator`）、Job/Burst 已在关键路径使用（如网格生成、图集构建）。
- 改进空间：少量热路径仍存在重复计算与未缓存、CPU噪声采样 `GetPixelBilinear` 调用开销大、预览 GPU 路径被强制禁用、若干平滑/噪声逻辑重复且未抽象复用、地形吸附为单线程 C# 循环可并行化。

## 关键问题与证据
- 噪声遮罩重复计算与未缓存：`PerlinNoiseMask.Evaluate` 每次调用都计算 `cos/sin` 与 `seedOffset`（`Runtime/Core/BlendMasks/PerlinNoiseMask.cs:61-69`），相比路肩噪声已做预计算（`Runtime/Core/BlendMasks/ShoulderPerlinNoiseMask.cs:77-92`）。
- CPU 噪声采样的高成本：`NoiseEvalUtils` 多次调用 `NoiseLutProvider.Sample01`，其内部用 `Texture2D.GetPixelBilinear` 逐次采样（`Runtime/Core/Noise/NoiseLutProvider.cs:119-128`），在密集循环中会成为热点。
- 预览 GPU 路径被硬禁用：`PreviewLineRenderer.SetUseGpu` 强制 `_useGpu = false`（`Runtime/Preview/PreviewLineRenderer.cs:205-209`），即使已有 GPU 批渲染脚手架。
- 平滑逻辑重复：`PerlinNoiseMask.ApplyNoiseSmoothing` 与 `BlendMaskBase.ApplySmoothing`/肩部非对称平滑实现相似，缺少统一复用（`Runtime/Core/BlendMasks/PerlinNoiseMask.cs:89-109`、`Runtime/Core/BlendMasks/ShoulderPerlinNoiseMask.cs:182-202`、`Runtime/Core/BlendMasks/BlendMaskBase.cs:91-109`）。
- 地形吸附串行：`PathSampler.DrapeSpineOnTerrain` 在单线程循环里采样地形与迭代平滑（`Runtime/Core/PathSampler.cs:69-139`），可 Job 并行化提升交互帧率。
- GPU 参数布尔布局风险：CPU DTO 使用 `bool`（`GpuShoulderMaskParamsData`，`Runtime/Core/BlendMasks/BlendMaskBase.cs:135-147`），运行时打包为 `int`（`MaskAtlasGenerator.PackMaskParams`，`Runtime/Core/MaskAtlasGenerator.cs:389-425`）。建议统一为 `int/flags`，避免未来扩展时的错位风险。

## 优化方案（分模块）

### 1) 噪声与遮罩
- 为 `PerlinNoiseMask` 引入预计算缓存，与 `ShoulderPerlinNoiseMask` 保持一致：
```csharp
// PerlinNoiseMask：新增预计算与早退
private float _scaleX,_scaleY,_cos,_sin,_seedX,_seedY; private int _oct; private float _lac,_gain; private bool _ready;
private void OnValidate(){ if (uniformScale) noiseScale.y=noiseScale.x; edgeLow=Mathf.Clamp01(edgeLow); edgeHigh=Mathf.Clamp01(edgeHigh); if(edgeHigh<edgeLow){var t=edgeLow;edgeLow=edgeHigh;edgeHigh=t;} _ready=false; }
private void EnsurePrecomputed(){ _scaleX=noiseScale.x; _scaleY=noiseScale.y; var rad=rotationDeg*Mathf.Deg2Rad; _cos=Mathf.Cos(rad); _sin=Mathf.Sin(rad); _oct=Mathf.Max(1,octaves); _lac=Mathf.Max(1f,lacunarity); _gain=Mathf.Clamp01(gain); var sx=Mathf.Abs(Mathf.Sin(seed*12.9898f)*43758.5453f); var sy=Mathf.Abs(Mathf.Sin(seed*78.233f)*12345.678f); _seedX=sx-Mathf.Floor(sx); _seedY=sy-Mathf.Floor(sy); _ready=true; }
public override float Evaluate(float x,float t,float w,float L){ if (strength<=0f) return 0f; if(!_ready) EnsurePrecomputed(); var u=TransformPosition(x,w,L); var v=TransformPathPosition(t,L); var p=new NoiseParamsDto{ ScaleX=uniformScale?_scaleX:noiseScale.x, ScaleY=uniformScale?_scaleX:_scaleY, Cos=_cos, Sin=_sin, Octaves=_oct, Lacunarity=_lac, Gain=_gain, SeedX=_seedX, SeedY=_seedY}; var n01=NoiseEvalUtils.EvaluateFbm01(p,u,v); var raw=Mathf.Clamp01(n01*Mathf.Max(0f,strength)); return ApplyNoiseSmoothing(raw); }
```
- 抽象并复用非对称平滑：将 `ApplyNoiseSmoothing` 与肩部的非对称平滑合并为 `BlendMaskBase.ApplyAsymmetricSmoothing(value, low, high)`，统一逻辑与边界处理，减少重复代码与维护成本。
- 统一噪声评估入口：优先使用 `NoiseVariantRegistry`（`Runtime/Core/NoiseRuntime/Variants/NoiseVariantRegistry.cs`）以 DI 风格选择 CPU/GPU 变体，避免各遮罩内直接调用 LUT 采样，便于扩展新变体（Stripe/Worley 已内置）。

### 2) CPU 噪声采样缓存
- 目标：避免在热路径中频繁 `GetPixelBilinear` 调用。
- 方案：为 `NoiseLutProvider` 增加只读快取（一次性 `GetPixels` → `NativeArray<float>`），并提供 `INoiseSampler` 接口，以 DI 将快取注入 `NoiseEvalUtils`：
```csharp
public interface INoiseSampler{ float Sample01(float u,float v); }
public sealed class CpuLutSampler:INoiseSampler{ readonly float[] _lut; readonly int _size; public CpuLutSampler(Texture2D tex){ _size=tex.width; var cs=tex.GetPixels(); _lut=new float[cs.Length]; for(int i=0;i<cs.Length;i++) _lut[i]=cs[i].r; } public float Sample01(float u,float v){ var st=new Vector2(u-Mathf.Floor(u), v-Mathf.Floor(v))*_size-Vector2.one*0.5f; /* 双线性采样同 MaskAtlasGenerator.SampleLut */ /* ... */ return value; } }
// NoiseEvalUtils 支持可选注入：静态设置 Sampler，默认回退到 NoiseLutProvider.Sample01
```
- 效果：批量采样时将耗时从 GPU 纹理接口调用降至纯内存访问，在 Editor 预览与 CPU 图集构建显著降时。

### 3) 路径采样与地形吸附并行化
- 目标：提升 `PathSampler.DrapeSpineOnTerrain` 交互帧率与稳定性。
- 方案：拆分为两个 Job：高度采样与迭代平滑，使用 `NativeArray<float3>` 与 `NativeArray<float>`，并用 `SafeJobExecutor` 调度。
```csharp
[BurstCompile]
struct SampleTerrainHeightsJob:IJobParallelFor{ [ReadOnly] public NativeArray<float3> Points; [WriteOnly] public NativeArray<float> TerrainHeights; public void Execute(int i){ TerrainHeights[i]=/* heightProvider.GetHeight(worldPoints[i]) 通过接口适配器 */; } }
[BurstCompile]
struct RelaxHeightsJob:IJobParallelFor{ public NativeArray<float> Heights; [ReadOnly] public NativeArray<float> TerrainHeights; public int Iterations; public void Execute(int i){ /* 邻域平均并 Max 到 TerrainHeights[i]，端点处理保留 */ } }
// Pipe：Local→World 转换(C#) → SampleTerrainHeightsJob → Elevation Align(C#) → RelaxHeightsJob(迭代) → 回填到 worldPoints → 计算切线/法线
```
- 效果：交互预览下，每次脊线采样的 CPU 时间显著下降；平滑次数增大时收益更明显。

### 4) 预览渲染 GPU 路径开关
- 放开 GPU 开关（保留提前返回策略）：
```csharp
public void SetUseGpu(bool enabled){ _useGpu = enabled && SystemInfo.supportsComputeShaders; }
```
- 引入项目级配置（`PreviewLineConfig` 已存在，`Runtime/Preview/PreviewLineRenderer.cs:935-951`），通过 Editor 面板切换 CPU/ GPU 预览模式；CPU 为默认，GPU 可选，失败自动回退。
- 渲染批次路径已完备（`RenderBatchGpu`），按需扩容 `ComputeBuffer` 并使用 `Graphics.DrawMeshInstancedProcedural`。

### 5) GPU 参数与结构体对齐
- 建议将 `GpuShoulderMaskParamsData` 的 `bool` 统一替换为 `int`，并在 `BlendMaskBase` 的 DTO 层面与 HLSL 完全对齐，避免未来扩展错位（当前运行时打包已做 `bool→int` 转换，`Runtime/Core/MaskAtlasGenerator.cs:389-425`）。

### 6) 代码风格与复用
- 提前返回统一化：在所有 `Evaluate`/`Update` 热路径顶部添加早退条件（强度为0、资源缺失、集合为空等），与当前风格保持一致（如 `ShoulderPerlinNoiseMask.Evaluate` 现已采用）。
- 抽象公共平滑与边缘权重计算（如 `ShoulderStripeWeight`）为静态工具，供 CPU 与 GPU 侧共享参数语义，降低差异风险（参考 `MaskAtlasGenerator.BuildMaskAtlasJob` 中实现）。
- 依赖注入强化：现有 `ResourceProvider`/`UnifiedMemory` 已具备注入能力，增加 `INoiseSampler`、`IHeightProviderAdapter` 等接口以减少对具体实现的硬依赖。

### 7) 内存与GC
- 将预览折线缓存（`_pointsBuffer/_polyBuffer/_polyExact`）的扩容策略统一为幂次扩容，减少数组重建（代码已部分采用，继续贯彻）。
- 在 Editor 高频路径避免 `List.Clear()` 后遗留大量容量导致的瞬时大 GC（可设置最大容量上限，超过时重建为合理大小）。

## 验证与测试
- CPU/GPU 一致性测试：对 `Noise/Stripe/Worley/ShoulderNoise` 在采样网格上做误差度量，阈值 < 1e-3。
- 性能基准：
  - `PerlinNoiseMask.Evaluate` 在 1M 次采样中的耗时对比（缓存前/后）。
  - `PathSampler.DrapeSpineOnTerrain` 在不同平滑迭代数下的帧时间对比（串行 vs Job）。
  - 预览渲染（CPU vs GPU）的线段数量阈值对比（如 10k、50k、100k）。
- 资源健壮性：Compute 不可用/内核缺失时的自动回退（已有 `HasKernel` 与 try/catch，继续覆盖单元测试）。

## 优先级与里程碑
1. 快速收益（1～2天）：
- `PerlinNoiseMask` 预计算缓存与早退；统一非对称平滑；开放预览 GPU 开关。
2. 中期优化（3～5天）：
- `NoiseLut` CPU 快取与 `INoiseSampler` 注入；`PathSampler` Job 并行化；抽象公共平滑工具。
3. 后续增强（>1周，按需）：
- DTO/HLSL 结构体完全对齐；Editor 面板配置项与 Addressables 资源提供器。

## 交付说明
- 所有改动遵循提前返回与单一职责，接口以依赖注入提供；
- 保持现有 API 不破坏、以内部实现替换为主；
- 变更点将附带基准数据与单元/集成测试报告，确保优化真实有效并稳定可维护。

请确认是否按上述计划执行，我将开始逐步提交优化改动与验证结果。