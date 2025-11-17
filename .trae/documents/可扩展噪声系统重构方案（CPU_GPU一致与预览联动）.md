## 目标
- 统一、可扩展的噪声架构：新增噪声只需实现一个评估器与参数打包器即可生效。
- CPU/GPU视觉一致：同一噪声变体在两侧计算路径得到相同效果。
- 参数改动即时预览：任何噪声/遮罩参数变动都会触发预览刷新（编辑器与运行时）。
- 性能最佳实践：零分配评估、预计算、Job/Burst 并行构建、Compute 优先。

## 总体架构
- 抽象接口
  - `INoiseVariant`：描述一种噪声（ID、名称、默认参数、参数校验）。
  - `INoiseCpuEvaluator`：CPU侧评估接口（`Evaluate(u, v, paramsDto) -> 0..1`）。
  - `INoiseGpuParamWriter`：将参数写入统一的 `GpuNoiseParams`（包含 `Variant` 与该变体特有字段）。
  - `IRegionSelector`：区域权重（路肩/边缘等），与噪声解耦。
  - `IMaskEvaluator`：组合区域权重与噪声输出（含整体缩放与平滑）。
- 数据与DTO
  - `NoiseParamsDto`：统一字段（`scale/cos/sin/seedOffsets` + fBm基础），编辑器侧在 `OnValidate`/工厂预计算。
  - `VariantParamsDto`：每个噪声变体自定义的参数快照（如 Worley 的 `period/jitter/invert`、Stripe 的 `period/jitter`）。
- 变体注册表
  - `NoiseVariantRegistry`（静态）：注册与发现所有 `INoiseVariant`；暴露 `TryGet(id)` 给遮罩与生成管线。
  - 新增噪声：添加一个 `INoiseVariant` 实现（携带 CPU evaluator + GPU writer + 参数校验），不动其他代码。

## CPU路径
- 评估器组合
  - 区域选择器（路肩/边缘）：纯函数权重；提前返回权重为0时跳过噪声评估。
  - 噪声评估：`INoiseCpuEvaluator.Evaluate(u, v, paramsDto)`（零分配、标量流水线）。
  - 组合输出：`mask = regionWeight * noise`，再整体缩放与（对称/非对称）平滑。
- 预计算缓存
  - 在资产 `OnValidate`/构造工厂内计算 `cos/sin/seedOffset/oct/lac/gain` 等常量，评估期零分配。
- 图集构建
  - 优先 Compute；CPU回退：`IJobParallelFor + Burst` 并行每层×行，输出 `NativeArray<byte>`，用 `Texture2D.SetPixelData<byte>` 写回。

## GPU路径
- 统一参数结构
  - `GpuNoiseParams` 扩展通用字段：`Variant/Period/Jitter/Invert` 等变体参数；保持已有 fBm 字段兼容。
- HLSL评估分支
  - `EvaluateNoise(...)` 根据 `Variant` 选择：`FBM/STRIPE/WORLEY`；复用 `_NoiseLUT` 实现可平铺抖动与哈希。
- 组合遮罩
  - 现有 `EvaluateShoulder/EvaluateShoulderNoise` 保留；边缘窄带复用“路肩参数，PositionRatio=0”作为窄带选择器。
- 资产打包
  - 每个遮罩资产在 `FillGpuParams` 中设置 `Variant` 与对应参数；缺省 `NoiseMask` 设置 `FBM`。

## 预览联动（参数改动→刷新）
- 事件与观察者
  - 遮罩资产实现 `IMaskChangeNotifier`：在 `OnValidate`、`OnEnable`、`OnDisable` 中触发 `Changed` 事件（`Action<BlendMaskBase>`）。
  - `PreviewMaterialManager`/`PathPreviewManager` 订阅 `Changed`，调用 `BuildMaskAtlas(...)` 并刷新材质/目标纹理。
- 自定义Inspector最佳实践
  - `EditorGUI.BeginChangeCheck()/EndChangeCheck()` 包裹参数绘制，变化后直接调用 `PreviewPipelineUtility.BuildMaskAtlas` 并 `Repaint` 目标窗口。
  - 避免频繁重建：合并多字段变更为一次刷新（`delayCall` 或 change aggregation）。
- 运行时联动
  - 提供 `MaskRuntimeObserver` 组件，绑定资产并在参数变化时刷新运行时预览材质（可选）。

## 性能与质量保障
- 提前返回：权重为零、强度为零时立即返回。
- 零分配：移除每样本 `new Vector2` 与逐样本三角函数；统一标量流水线。
- Job/Burst：CPU图集构建并行化；在大型图层/分辨率下保持交互流畅。
- 一致性校验：CPU/GPU两侧使用同一 `_NoiseLUT` 采样，视觉对齐；落差允许 < 1 LSB。

## 测试
- 冒烟与边界：各遮罩 `Evaluate` 输出范围、路肩中心为0、边缘两端非0、非对称阈值正确。
- 视觉一致性：采样若干参数集合，比较 CPU/GPU atlas 差异（字节级容差）。
- 性能基准：在典型场景记录 `BuildAtlasCpu/Gpu` 耗时与GC分配。

## 迁移策略
- 第一步：引入 `NoiseVariantRegistry/INoiseVariant` 与 DTO，不破坏现有资产；默认所有旧遮罩映射到 `FBM`。
- 第二步：将现有 `NoiseMask/ShoulderPerlinNoiseMask/EdgePerlinNoiseMask` 切换到新评估工具（CPU）与新 GPU 打包字段。
- 第三步：添加 `Stripe/Worley` 变体（CPU/GPU）、自定义Inspector联动刷新。
- 第四步：并行化 CPU 图集构建，完善测试与文档。

## 示例（接口与用法简稿）
- `INoiseVariant`
  - `Id`、`Name`
  - `CreateDefaultDto(asset)` → `VariantParamsDto`
  - `CpuEvaluator.Evaluate(u, v, baseDto, variantDto)`
  - `GpuWriter.Write(baseDto, variantDto, ref GpuNoiseParams)`
- 遮罩资产 `FillGpuParams`
  - 设置 `Variant` 与变体字段；区域选择参数写入 `ShoulderParams`（或 `EdgeBand`）。
- 编辑器刷新
  - `MaskAssetEditor`：参数变更后调用 `PreviewMaterialManager.Update()`；合并频繁事件。

## 交付计划
- Phase 1（架构/注册/DTO）：搭建接口与注册表、移植现有FBM到新框架。
- Phase 2（CPU评估）：迁移路肩/边缘/通用噪声到统一评估；零分配与预计算完成。
- Phase 3（GPU评估）：扩展 `GpuNoiseParams` 与 HLSL分支；资产打包对齐。
- Phase 4（预览联动）：实现资产事件与Inspector联动刷新；整合预览流水线。
- Phase 5（性能与测试）：并行化CPU构建、完成一致性与基准测试。

## 风险与回避
- HLSL结构体变更需与运行时结构体严谨对齐；提供静态校验辅助。
- 事件风暴：通过合并策略/节流与延迟刷新避免过度重建。
- 兼容旧资产：默认映射到 `FBM`，提供升级脚本将旧字段填入新 DTO。