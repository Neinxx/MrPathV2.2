## 现状与目标
- 现状：已完成噪声变体统一（FBM/Stripe/Worley）、GPU评估分支、事件联动与Inspector节流；CPU串行图集写回已优化为 `SetPixelData(byte)`。
- 目标：在Compute不可用或需CPU回退时，并行化图集构建以提升预览帧率，同时保证CPU/GPU一致性和零分配评估。

## 设计要点
- 保持Compute优先：`Runtime/Core/MaskAtlasGenerator.cs:94-112` 与 `128-207` 保持GPU路径为首选。
- 新增并行Job：对“层×行”维度并行，内部仅做 across 列循环；沿用遮罩的提前返回策略。
- LUT只读快照：从 `_NoiseLUT`（或CPU生成LUT）构建只读 `NativeArray<float>`，Job内自实现repeat + bilinear，避免 `Texture2D` 线程不安全访问。

## 修改内容
- 新增 `NoiseLutSnapshotProvider`
  - 位置：`Runtime/Core/Noise/` 或 `Runtime/Core/NoiseRuntime/`
  - 功能：将 `NoiseLutProvider.GetOrCreateLut()` 的内容读取到 `float[]`/`NativeArray<float>`（R8→float），并提供尺寸与双线性采样静态方法（Job可调用）。
- 新增并行Job
  - `BuildMaskAtlasJob : IJobParallelFor`（BurstCompile）
  - 输入：层参数快照（避免Job里访问 `ScriptableObject`）、`atlasWidth/pathSamples/worldWidth/pathLength/maskThreshold`、`NativeArray<float> lut`、`lutSize`
  - 输出：`NativeArray<byte> outR`
  - 逻辑：索引映射到 `layerIndex` 与 `py`；内部 `px` 循环计算 `across`、调用层 `Evaluate(...)`，写入 `outR[px + idx*atlasWidth]`。
- 层参数快照结构
  - 位置：`Runtime/Core/MaskAtlasGenerator.cs`（私有 struct）
  - 内容：对每种遮罩的必需参数（如 `NoiseParamsDto`/路肩参数/边缘参数等），与当前已存在的 `GpuMaskParams` 对齐，便于CPU与GPU一致。
- 替换CPU回退入口
  - 在 `BuildAtlasCpu(...)` 判断 `SystemInfo.supportsComputeShaders` 与工作量（分辨率×层数）
  - 大工作量时启用并行Job：创建 `NativeArray<byte>`、`NativeArray<float>`（lut）；`job.Schedule(layerCount*pathSamples, 1).Complete()`；写回 `SetPixelData<byte>(data, 0)`。
  - 小工作量保持现有串行路径。

## 一致性与验证
- CPU/GPU一致性测试
  - 在 `Editor/Tests/` 新增用例：对若干参数集合（FBM/Stripe/Worley、Shoulder/Edge组合）构建CPU/GPU atlas，逐像素比较差异（允许 < 1 LSB）。
- 性能基准
  - 记录Compute、CPU串行、CPU并行三路径在不同分辨率与层数下的耗时与GC；确保Job路径零GC（除初始化）。

## 风险与规避
- 线程安全：Job内不得访问 `Texture2D`；全部使用 `NativeArray<float>` 的lut快照与纯数学运算。
- 结构体对齐：CPU快照与GPU结构一致，减少分支差异；在变体评估上复用已统一的DTO与数学流程。
- 回退健壮性：lut不存在时使用常量0.5；与当前GPU/HLSL处理一致（`MaskAtlasGenerator.cs:266-268`）。

## 实施步骤
1) 添加 `NoiseLutSnapshotProvider`（只读快照与bilinear工具）
2) 在 `MaskAtlasGenerator.cs` 定义层参数快照结构与 `BuildMaskAtlasJob`（BurstCompile）
3) 修改 `BuildAtlasCpu(...)` 入口，按工作量启用并行Job，写回 `SetPixelData<byte>`
4) 新增一致性与性能测试（`Editor/Tests/`），覆盖FBM/Stripe/Worley及组合遮罩

## 参考入口
- GPU优先入口：`Runtime/Core/MaskAtlasGenerator.cs:94-112, 128-207`
- CPU回退串行：`Runtime/Core/MaskAtlasGenerator.cs:292-331`
- 噪声LUT生成与绑定：`Runtime/Core/Noise/NoiseLutProvider.cs:26-101, 118-127`; GPU绑定处：`Runtime/Core/MaskAtlasGenerator.cs:257-263`