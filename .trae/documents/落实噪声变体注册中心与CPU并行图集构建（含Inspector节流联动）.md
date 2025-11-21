## 目标
- 引入噪声变体注册中心，统一管理与注入 CPU/GPU 评估与打包
- 将 CPU 图集构建并行化（IJobParallelFor + Burst），保持 Compute 优先
- 在 Inspector 层做参数节流与合并，确保“改动→当帧刷新”且稳定

## 变体注册中心
- 新增接口
  - `INoiseVariant`：变体元信息（Id/Name/默认参数构造/参数校验）
  - `INoiseCpuEvaluator`：`Evaluate(u, v, baseDto, variantDto) → 0..1`
  - `INoiseGpuParamWriter`：`Write(baseDto, variantDto, ref GpuNoiseParamsData)`
- 新增 `NoiseVariantRegistry`（静态）
  - 提供 `Register(INoiseVariant)`、`TryGet(id, out evaluator, out writer)`
  - 预注册 FBM/STRIPE/WORLEY 三种变体，对齐现有 HLSL `Variant` 常量
- 遮罩改造
  - 在 `FillGpuParams` 中通过 Registry 获取变体 writer 进行统一打包（避免分散逻辑）
  - CPU 评估逻辑通过 Registry 获取 evaluator，保持零分配与预计算 DTO

## CPU 并行图集构建
- 新增 `BuildMaskAtlasJob : IJobParallelFor`
  - 输入：层列表（只读），`worldWidth/pathLength/atlasWidth/pathSamples/maskThreshold`
  - 输出：`NativeArray<byte>`（R通道权重 0..255）
  - 执行：索引映射为 `layerIndex * pathSamples + py`；内部 `px` 循环调用层的 `Evaluate`（保持提前返回）
- 入口切换
  - `MaskAtlasGenerator.BuildAtlasCpu(...)` 增加分支：若 `Burst` 可用且层数与分辨率较大，走并行 Job；否则沿用串行路径
  - 写回：统一使用 `Texture2D.SetPixelData<byte>(outR, 0)`，与现有 R8 写回一致

## Inspector 参数节流
- 自定义 Inspector 模板（遮罩基类与噪声变体通用）
  - 使用 `EditorGUI.BeginChangeCheck/EndChangeCheck` 包裹多个字段
  - 变化后将刷新操作聚合，使用 `EditorApplication.delayCall` 推送一次性刷新
  - 仍触发已有 `MaskChangeEvents.RaiseChanged`，材质管理器当帧强制刷新（已接入）
- 可选：在 `PreviewMaterialManager` 增加简单节流窗口（如 50~100ms），若频繁变更则合并为一次 `RefreshMaterial`

## CPU/GPU 一致性
- 继续使用共享 `_NoiseLUT` 与相同变体语义；FBM/Stripe/Worley 的参数映射由 `INoiseGpuParamWriter` 保证与 HLSL 对齐
- 区域选择器（路肩/边缘）独立：噪声变体替换不影响区域权重与提前返回逻辑

## 测试与基准
- 一致性：对 FBM/Stripe/Worley 参数集生成 CPU/GPU atlas，逐像素比对（容差 < 1 LSB）
- 性能：对比 Compute、CPU串行、CPU并行三种路径在不同分辨率/层数下的耗时与 GC
- 事件联动：连续修改多个参数，确认 Inspector 节流与事件当帧刷新行为稳定

## 迁移策略
- 现有资产默认映射到 `FBM` 变体；新增条纹/沃雷资产沿用注册中心
- 提供批量脚本为旧资产填充变体标识与默认参数（无需改动现有字段）

## 实施阶段
- Phase 1：接口与 Registry、注册 FBM/Stripe/Worley
- Phase 2：遮罩接入 Registry（CPU/GPU）、移除分散打包代码
- Phase 3：CPU并行 Job 与入口开关、统一 R8 写回
- Phase 4：Inspector 节流、事件合并
- Phase 5：一致性与性能测试、基准报告与示例预设更新