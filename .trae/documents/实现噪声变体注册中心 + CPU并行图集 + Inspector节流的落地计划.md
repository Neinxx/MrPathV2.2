## 概览
- 目标：新增噪声变体注册中心、并行化CPU图集构建，并在Inspector层做参数节流；保持CPU/GPU一致与即改即刷。
- 范围：不破坏现有资产与预览链路（`PreviewMaterialManager.cs:231-254`），仅增强扩展性与性能。

## 变体注册中心
- 位置：`Runtime/Core/NoiseRuntime/Variants/`
- 新接口
  - `INoiseVariant`：Id/Name/默认参数DTO构造/参数校验
  - `INoiseCpuEvaluator`：`Evaluate(u, v, NoiseParamsDto baseDto, VariantParamsDto variantDto)`
  - `INoiseGpuParamWriter`：`Write(NoiseParamsDto baseDto, VariantParamsDto variantDto, ref GpuNoiseMaskParamsData dst)`
- 注册类
  - `NoiseVariantRegistry`（静态）：`Register`、`TryGet(id, out evaluator, out writer)`；预注册 FBM/Stripe/Worley，对齐 HLSL 变体常量（`Editor/Resources/BlendMaskLibrary.hlsl:5-9`）。
- 资产接入
  - `NoiseMask.cs:119-135`、`StripeNoiseMask.cs:71-93`、`WorleyNoiseMask.cs:79-101` 中的 `FillGpuParams` 改为从 Registry 获取 writer 写入；`Evaluate` 改为从 Registry 获取 evaluator，传入已预计算的 `NoiseParamsDto` 与变体DTO。

## CPU图集并行化
- 位置：`Runtime/Core/MaskAtlasGenerator.cs`
- 新Job：`BuildMaskAtlasJob : IJobParallelFor`（BurstCompile）
  - 输入：每层已打包的评估参数（避免在Job里访问`ScriptableObject`），`worldWidth/pathLength/atlasWidth/pathSamples/maskThreshold`
  - 输出：`NativeArray<byte>`（R通道0..255）
  - 执行：索引映射 `layerIndex * pathSamples + py`；内部 `px` 循环调用对应层的 `Evaluate(across, progress, worldWidth, pathLength)`；沿用提前返回。
- 入口开关
  - 在 `BuildAtlasCpu(...)` 判断分辨率/层数与 `Burst` 可用性，走并行Job，否则走已有串行路径（参考当前串行：`Runtime/Core/MaskAtlasGenerator.cs:292-331`）。
  - 写回：统一使用 `Texture2D.SetPixelData<byte>(data, 0)`（已在`Runtime/Core/MaskAtlasGenerator.cs:304-331`）。

## Inspector参数节流
- 位置：`Editor/Inspectors/`
- 基类：`MaskAssetInspector<TMask>`
  - 用 `EditorGUI.BeginChangeCheck/EndChangeCheck` 包裹多个字段
  - 在 `EndChangeCheck` 合并变更，用 `EditorApplication.delayCall` 做一次性刷新；同时触发 `MaskChangeEvents.RaiseChanged(mask)`（事件中心在 `Runtime/Core/BlendMasks/MaskChangeEvents.cs:1`）。
- 说明：材质管理器已订阅事件并当帧强制刷新（`Editor/Preview/PreviewMaterialManager.cs:231-254`）。

## CPU/GPU一致性
- 变体参数对齐
  - 运行时结构扩展已完成：`BlendMaskBase.cs:152-166` 与映射 `MaskAtlasGenerator.cs:366-383,407-424`
  - HLSL分支选择与实现：`Editor/Resources/BlendMaskLibrary.hlsl:111-146`
- 区域选择器不变：路肩与边缘逻辑维持（`ShoulderPerlinNoiseMask.cs:82-107`、`EdgePerlinNoiseMask.cs:59-74`）。

## 测试与基准
- 一致性测试：构建 CPU 与 GPU atlas 对比差异 < 1 LSB；覆盖 FBM/Stripe/Worley 参数集。
- 性能基准：对比 Compute/CPU串行/CPU并行在不同分辨率与层数下的耗时与GC。
- 冒烟测试已存在：`Editor/Tests/NoiseMasksSmokeTest.cs`；补充CPU/GPU一致性用例。

## 迁移
- 现有资产默认映射到FBM变体；新增条纹/沃雷资产沿用Registry。
- 提供批处理脚本为旧资产补写变体标识与默认参数（无需改动字段结构）。

## 实施顺序
1) 添加接口与 `NoiseVariantRegistry`，预注册三变体
2) 接入遮罩资产的CPU/GPU路径（Evaluate/FillGpuParams 改为通过Registry）
3) 实现并行Job与入口开关，写回沿用R8 `SetPixelData`
4) 增加Inspector节流基类与派生，接入事件中心
5) 补充一致性与性能测试，验收后清理与微调