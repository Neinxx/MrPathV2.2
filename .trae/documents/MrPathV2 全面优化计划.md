## 总览
- 目标：提升数据一致性、依赖注入解耦、预览管线职责清晰、运行时性能与健壮性，保持 Unity 2021.3 兼容。
- 范围：BlendMasks/Noise/Jobs/Memory/Settings/Preview/Editor 流程与资源访问。

## 现状与问题
- 资源加载：已有资源提供者抽象；建议扩展为可配置后端（Addressables/AssetDatabase）。
- 预览材质管理：类内职责多（数组构建/参数推送/MaskAtlas绑定），维护成本高。
- MaskAtlas 并行阈值：固定阈值可能低估/高估并行收益。
- 噪声LUT：每次查询 `HasKernel/FindKernel` 存在重复开销。
- 单例与DI：部分使用静态入口；调用方仍直接依赖具体类。
- 健康检查：已有 PathStrategyRegistry 运行时提示；建议扩展更全面的自检项。

## 优化建议
### 资源与依赖注入
- 提供 `AddressablesResourceProvider` 与 `AssetDatabaseResourceProvider(Editor)` 实现，通过 `ResourceProvider.Instance` 注入。
- 在测试环境/平台切换脚本中统一注入后端，避免运行时散点替换。

### 预览管线职责拆分
- 拆分 `PreviewMaterialManager`：
  - 纹理数组构建器（TextureArrayBuilder）
  - 材质参数推送器（MaterialParamPusher）
  - Atlas绑定器（MaskAtlasBinder）
- 主协调器仅编排与生命周期管理，保留提前返回路径。

### MaskAtlas 并行与资源复用
- 并行阈值动态估计：根据 `atlasWidth*atlasHeight` 与 `SystemInfo.processorCount` 计算最优批量。
- 复用 `ComputeBuffer/RenderTexture/Texture2D`：引入轻量池化，减少频繁分配释放。
- GPU→CPU拷贝在大图时使用 `AsyncGPUReadback`（2021.3可用）降低主线程阻塞。

### 噪声LUT与Compute细节
- 缓存 `HasKernel` 与 `FindKernel` 结果，避免重复查询。
- 在 `BindToCompute` 时统一写入一次 `_NoiseLutSize` 与纹理，避免多次设置。

### 单例与接口化
- 为 `PathStrategyRegistry` 增加接口 `IPathStrategyRegistry`，调用方依赖接口，便于测试与替换实现。
- 使用构造或方法注入传递注册表，而非直接访问静态 `Instance`（在核心运行时代码中逐步迁移）。

### 健康检查与开发体验
- 扩展健康检查：
  - 检查 `Resources` 中必要 Compute/Shader/LUT 是否存在；
  - 检查策略映射完整性（所有 `CurveType` 都有策略）；
  - 提供一键修复入口（Editor 下创建缺失资产/生成占位资源）。
- 在 `Project Settings` 中增加 MrPath 诊断面板，集中展示与修复。

### 代码风格与提前返回
- 审查关键热点方法，进一步前移空集合/空资源的提前返回，减少分支与日志输出。
- 保持方法短小与单一职责，拆分过长方法为可测试的私有子方法。

## 实施步骤
1. 资源后端扩展与注入示例（Addressables/Editor AssetDatabase）。
2. 预览材质管理模块拆分与单元测试覆盖关键功能。
3. MaskAtlas 并行阈值动态化与资源复用；引入可选 AsyncGPUReadback 路径。
4. 噪声LUT内核结果缓存与绑定优化。
5. 引入 `IPathStrategyRegistry`，逐步在核心运行时代码中改为接口依赖。
6. 健康检查面板与运行时自检扩展，完善一键修复流程。
7. 核查提前返回与方法拆分，统一风格与命名，补充接口注释。

## 验证与回归保护
- 添加 PlayMode/EditMode 测试：
  - MaskAtlas 在多尺寸下 CPU/GPU 一致性（误差阈值）。
  - NoiseLUT 一致采样（CPU/GPU 对齐）。
  - 资源提供者切换（Resources/Addressables）无行为差异。
- 性能基线：对比并行阈值调整前后生成耗时与GC分配（Profiler）。

请确认以上计划，我将按步骤逐项落地，并在每一步提供具体改动和验证结果。