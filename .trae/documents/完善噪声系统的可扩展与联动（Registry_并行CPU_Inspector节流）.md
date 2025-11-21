## 目标
- 新增噪声变体注册中心与接口，新增风格无需改动现有管线
- 并行化 CPU 图集构建，提升预览帧率
- 统一参数变更事件与 Inspector 节流，保证“改动→即时刷新”且稳定

## 接口与注册中心
- 新增 `INoiseVariant/INoiseCpuEvaluator/INoiseGpuParamWriter/IRegionSelector/IMaskEvaluator`
- 新建 `NoiseVariantRegistry`（静态）：注册 FBM/Stripe/Worley 变体；提供 `TryGet(id)` 获取 evaluator 与 GPU writer
- 在现有遮罩资产的 `FillGpuParams` 与 CPU评估处，改为通过 `NoiseVariantRegistry` 获取相应 evaluator 与 writer

## CPU图集并行化
- 在 `Runtime/Core/MaskAtlasGenerator.cs` 引入 `IJobParallelFor + Burst` 路径：
  - 新建 `BuildMaskAtlasJob`，输入层列表、`worldWidth/pathLength/atlasWidth/pathSamples`，输出 `NativeArray<byte>`
  - 每个索引对应“层×行”，内部循环 `px` 写入到 `outR`；最终 `SetPixelData<byte>(0)` 写回 R8（参考现有 R8 写回：Runtime/Core/MaskAtlasGenerator.cs:304-331）
  - 在 Editor/Runtime 入口以开关选择 CPU并行或现有串行（保留 compute 优先）

## 参数事件与 Inspector节流
- 已有事件中心：`Runtime/Core/BlendMasks/MaskChangeEvents.cs:1`
- 在各遮罩 `OnValidate` 已触发 `RaiseChanged`（如 NoiseMask.cs:43-67 后）
- 新增 Inspector 层面节流：
  - 在自定义 Inspector 中使用 `EditorGUI.BeginChangeCheck/EndChangeCheck`；合并多字段改动后，通过 `EditorApplication.delayCall` 一次性触发刷新
  - `PreviewMaterialManager` 已订阅事件（Editor/Preview/PreviewMaterialManager.cs:231-254），保留 `m_ForceRefresh` 机制；增加“节流窗口”避免过度重建

## CPU/GPU一致性增强
- 将 `NoiseVariantRegistry` 的 evaluator 与 GPU writer 权责固定：
  - CPU evaluator：仅接受基础 `NoiseParamsDto` 与变体 `VariantParamsDto`
  - GPU writer：统一写入 `GpuNoiseParams` 扩展字段（Variant/Period/Jitter/Invert），保持 HLSL 分支：Editor/Resources/BlendMaskLibrary.hlsl:111-146
- 在 Edge/Shoulder 组合类中，区域选择作为独立模块，噪声变体仅更换 evaluator，不影响区域逻辑

## 测试与基准
- 一致性测试：对 FBM/Stripe/Worley 参数集生成 CPU/GPU atlas，逐像素比较差异（允许 < 1 LSB）
- 性能基准：记录 compute/CPU串行/CPU并行耗时与GC；对比不同分辨率与层数
- 事件与刷新：更改若干参数，验证 PreviewMaterialManager 收到事件后当帧刷新（Editor/Preview/PreviewMaterialManager.cs:241-254）

## 迁移
- 维持现有资产不破坏：默认 FBM 变体；Edge/Shoulder 保留原映射
- 提供脚本批量为旧资产打上 `Variant` 默认值，并按需要填充扩展参数（如 Stripe/Worley）

## 实施阶段
- Phase 1：接口与注册中心、将现有遮罩接入注册中心
- Phase 2：CPU并行化 Job 与开关、保持 compute 优先
- Phase 3：Inspector 节流与事件合并、完善材质管理器的刷新策略
- Phase 4：一致性与性能测试、示例资产与预设更新

## 代码定位
- 材质预览与刷新逻辑：Editor/Preview/PreviewMaterialManager.cs:231-254, 339-501
- GPU评估分支：Editor/Resources/BlendMaskLibrary.hlsl:5-9, 27-43, 111-146
- R8写回与Atlas生成：Runtime/Core/MaskAtlasGenerator.cs:304-331
- 事件中心与触发：Runtime/Core/BlendMasks/MaskChangeEvents.cs:1；各遮罩 OnValidate 中触发

请确认后我将开始按上述阶段落地，实现并联通预览链路。