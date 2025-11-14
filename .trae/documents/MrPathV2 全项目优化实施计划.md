## 总览
- 目标：在不改变对外行为的前提下，以“提前返回”和“单一职责”为核心，提升架构可维护性、运行时性能与内存安全，统一依赖注入风格，补齐验证测试。
- 范围：Runtime 与 Editor 全目录，重点模块包括内存管理与Jobs、异步任务管理、预览渲染器、策略注册与错误处理。

## 架构与依赖注入
- 为核心单例引入接口并通过构造/方法注入使用，减少对具体实现的直接依赖：
  - 将 `UnifiedMemoryManager` 抽象为 `IUnifiedMemoryManager` 并在使用处注入（参考 Runtime/Memory/UnifiedMemoryManager.cs:109-113；Runtime/Jobs/JobResourceManager.cs:116-131 的静态访问）。
  - 为 `SafeJobExecutor`、`AsyncOperationManager` 增加接口（如 `IJobExecutor`、`IAsyncOps`），在 `PathPreviewManager` 等组合类中以依赖注入持有。
  - 为 `PathStrategyRegistry` 提供查询接口并允许替换数据源（Runtime/Settings/PathStrategyRegistry.cs:43-56）。

## 异步与任务管理
- 改进 `AsyncOperationManager` 的任务完成与取消语义（Runtime/Core/AsyncOperationManager.cs）：
  - 使用 `TaskCompletionSource<bool>(RunContinuationsAsynchronously)` 创建 `tcs`（96-103），避免同步继续导致潜在死锁。
  - 注册取消回调而非仅 `CancelAfter`，区分“超时取消”和“外部取消”，并在 `WaitForOperationAsync` 中统一返回（210-243）。
  - 提供 `TryGetOperation(string id, out ...)` 与 `GetSnapshot()`，避免在热路径频繁分配数组（194-201）。

## Jobs 与内存安全
- 减少热点分支与重复扫描，统一原语与分配策略：
  - 在 `PaintSplatmapJob` 预先构建“是否为配方图层”的查表数组，替代 `IsRecipeSplatIndex` 的逐层线性扫描（Runtime/Jobs/PaintSplatmapJob.cs:255-263）。
  - 统一 `Allocator` 使用场景：Temp/TempJob/Persistent 的选择遵循“生命周期最短原则”，减少不必要的 Persistent 分配。
  - 为 `RecipeData` 增加只读语义与更小的默认结构体分辨率参数开关，降低初始化成本（Runtime/Jobs/RecipeJobsUtility.cs:76-91, 160-193, 198-235）。
  - 在 `UnifiedMemoryManager` 中增加统计项的只读快照与池化策略钩子，避免 Editor 下频繁GC（Runtime/Memory/UnifiedMemoryManager.cs:31-47, 71-79）。

## 预览渲染器性能
- 降低 Editor 每帧GC与绘制开销（Runtime/Preview/PreviewLineRenderer.cs）：
  - 对 `RenderBatch` 中的 `points`、`poly` 等集合采用对象池复用，避免每帧新建大数组（504-521, 525-566）。
  - 将分组字典改为可复用缓存并在帧结束清空，减少哈希与List分配（463-502）。
  - 保留当前禁用GPU路径，但抽象 `IGpuPreviewBackend` 接口，未来可按需启用（595-641, 880-971）。

## 策略注册与健壮性
- 为 `PathStrategyRegistry` 增强缓存初始化的幂等性与降噪：
  - 缓存字典只在数据变动时重建并提供只读视图，减少查找成本（146-168, 243-250）。
  - 对 Resources 加载失败的路径提供可注入数据源，便于测试与无Resources场景（101-141）。

## 错误处理与日志
- 在 `ErrorHandler` 中：
  - 将 `ErrorInfo` 字段声明为只读并提供轻量级快照API，减少复制成本（24-53）。
  - 增加“等级限流”选项，避免高频Warning刷屏影响性能（80-88, 291-307）。

## 测试与验证
- 扩充现有测试（Editor/Tests 与 Runtime/Tests）：
  - 内存与泄漏：`UnifiedMemoryManager` 生命周期与统计正确性。
  - Jobs 正确性：`PaintSplatmapJob` 在多配方层的边界权重与归一化稳定性。
  - 预览性能：`PreviewLineRenderer` 在大批量线段下的GC占用与帧时间基线。

## 交付阶段
- 阶段1：接口抽象与依赖注入落地（内存管理/策略注册/预览组合类）。
- 阶段2：`AsyncOperationManager` 取消与完成改进，统一API。
- 阶段3：Jobs 微优化与分配策略梳理，引入查表。
- 阶段4：预览渲染器对象池与分组缓存，绘制路径优化。
- 阶段5：测试用例补齐与性能基线采集。

## 风险与兼容
- 保持对外API不变；引入接口仅在内部依赖层面调整。
- Editor下的行为与现有UI一致；GPU预览仍默认关闭。
- Unity 2021.3.18f1 兼容，Burst与Jobs设置沿用当前配置。
