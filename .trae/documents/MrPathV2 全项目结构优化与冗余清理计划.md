## 概览
- 目标：按单一职责与提前返回原则，理顺 Editor/Runtime 边界，去除冗余，引入轻量依赖注入，并提升预览与内存管理的稳定性与性能（Unity 2021.3.18f1）。
- 影响范围：目录结构、asmdef、若干脚本拆分（策略、句柄、属性绘制器、示例）、少量 API 接口化与条件编译调整、测试与验证。

## 现状要点
- 目录与 asmdef 不一致：Runtime/Editor 代码大多位于项目根（`Editor/`, `Runtime/`），而 asmdef 位于 `Assets/MrPathV2/Editor|Runtime/`。且 `MrPathV2.Runtime.asmdef` 仅包含 `Editor` 平台，导致运行时层含 `UnityEditor` 用法的“异常正确”。
- 运行时脚本引用了 `UnityEditor`：如 `Runtime/Core/PathEditorHandles.cs`、`Runtime/Components/RequiredFieldDrawer.cs`、`Runtime/Core/PathLengthUsageExample.cs`、`Runtime/Strategies/*`、`Runtime/Preview/PreviewLineRenderer.cs` 等，违反 Runtime 无编辑器依赖的边界。
- 预览与策略实现存在 Editor 逻辑混入 Runtime：策略类既含数学法则又含编辑器绘制；句柄绘制器位于 Runtime；预览渲染器大量 `Handles` 逻辑。
- 冗余与样例：`Editor/UI/Testss.cs` 引用 `Assets\__temp\...` 资源路径；Settings/Masks 下的占位资产与实验性文档较多。

## 优化方案
### 1. 目录与程序集定义（asmdef）修复
- 将根层 `Editor/` 与 `Runtime/` 代码迁移至 `Assets/MrPathV2/Editor/` 与 `Assets/MrPathV2/Runtime/`，与 asmdef 对齐。
- 修正 `Assets/MrPathV2/Runtime/MrPathV2.Runtime.asmdef`：`includePlatforms` 设为空（Any Platform），移除 Editor-only 限制；保留 `MrPathV2.Editor.asmdef` 引用 Runtime。
- 结果：打包/运行/测试边界清晰，Runtime 不再允许编译 `UnityEditor`。

### 2. Editor/Runtime 职责拆分
- 将 Editor 相关脚本移至 Editor 目录：
  - `Runtime/Core/PathEditorHandles.cs` → Editor（仅编辑器绘制与交互）。
  - `Runtime/Components/RequiredFieldDrawer.cs` → Editor（属性绘制器）。
  - `Runtime/Core/PathLengthUsageExample.cs` → Editor 示例或以 `#if UNITY_EDITOR` 包裹 `Handles.Label` 并移除顶级 `using UnityEditor`。
  - `Runtime/Preview/PreviewLineRenderer.cs`：保留为 Runtime 类，但把 `Handles` 绘制路径拆至 Editor 层，Runtime 仅提供采样与数据管线；或以条件编译完全隔离 Editor API。
- 策略类（如 `Runtime/Strategies/BezierStrategy.cs`、`CatmullRomStrategy.cs`）改为 partial：
  - Runtime partial：曲线数学与数据操作（不含任何 Editor API）。
  - Editor partial：`DrawHandles/UpdatePointHover`，置于 Editor 目录。

### 3. 依赖注入与接口化
- 为单例提供接口与可选注入（保持向后兼容）：
  - `IPathStrategyRegistry`（默认实现 PathStrategyRegistry，`Resources.Load`/Editor 查找仍可用）。
  - `IMemoryManager`（默认实现 UnifiedMemoryManager，经 `UnifiedMemory.Instance` 访问）。
- 在 `PathCreator`、`JobResourceManager` 等处增加可注入字段（`[SerializeField]` 或 `public set`），若未注入则回退至单例。避免强耦合，便于测试与替换。

### 4. 提前返回与单一职责微调
- 运行时 API 强化“无效即退”：
  - 路径采样与长度：缓存世界缩放系数，细采样前验证 `NumPoints>=2` 与策略存在（`Runtime/Core/PathCreator.cs:258`、`271`）。
  - 策略 `ClearSegments` 与 `MovePoint/InsertSegment` 内边界检查统一为前置返回，移除嵌套分支。
- Editor 层合并刷新策略：事件只广播一次，具体刷新由 Editor 管理器分流（已有 `PathCreatorEditor.cs:147-159`，保持并简化）。

### 5. 预览性能与可维护性
- 预览采样与绘制路径清晰分层：
  - Runtime：采样（Bezier/Catmull）与分辨率估算（屏幕像素步长）纯算法；避免 `Handles` 与 `SceneView` 直接访问。
  - Editor：摄像机、视锥剔除、AA/虚线批绘集合，集中于一个 Editor-only 渲染器。
- 统一条件编译与 API：删除顶层 `using UnityEditor`（改为 `#if UNITY_EDITOR` 区块内引用），减少意外构建错误。

### 6. 冗余清理与示例归档
- 移除或归档 `Editor/UI/Testss.cs` 与 `Assets\__temp\...` 依赖；若保留则移动到 `Samples~/` 或 `Tests/Editor`。
- Settings/Masks 下的占位资产（如 `filler text*.asset`）归档或删除，以免污染包体。
- 文档类 *.md 保留关键的“架构/最佳实践”和“性能优化”文档，其他合并到一份索引或移至外部文档仓库。

### 7. 验证与测试
- 修复后执行：
  - PlayMode：`PathCreator.GetPathLength()` 结果稳定性（场景缩放、路径点变化）。
  - Editor：策略绘制与句柄交互（悬停、拖拽）在不同分辨率与采样策略下的流畅度。
- 静态分析：确保 `MrPathV2.Runtime` 不再引用 `UnityEditor`；`MrPathV2.Editor` 引用 Runtime；无循环依赖。

## 关键变更一览（示例引用）
- `Runtime/Core/PathCreator.cs:41-61` 通过注册中心获取策略，提前返回保护；`:271-294` 细采样长度，建议缓存缩放。
- `Runtime/Settings/PathStrategyRegistry.cs:103` 使用 `Resources.Load`；建议将 `FindInEditor()`（`:109-123`）与 `using UnityEditor` 迁移至 Editor 层。
- `Runtime/Core/PathEditorHandles.cs:28-47`、`:58-109` 纯编辑器绘制逻辑，应移至 Editor。
- `Runtime/Strategies/BezierStrategy.cs:105-377` 含大量 Handles 绘制，应拆为 Editor partial。
- `Runtime/Components/RequiredFieldDrawer.cs:50-174` 属性绘制器，应在 Editor 目录。
- `Runtime/Core/PathLengthUsageExample.cs:41-44` 顶层 `using UnityEditor` 与 `Handles.Label`，转为 Editor-only。
- `Editor/UI/Testss.cs:12-15` 与 `:41-61` 引用临时资源，建议删除或迁移。

## 里程碑
- 阶段1：目录迁移与 asmdef 修复；去除 Runtime 的 `UnityEditor` 直接引用。
- 阶段2：策略与预览拆分（partial 与 Editor 渲染器）；句柄/属性绘制器迁移。
- 阶段3：接口化与可注入；提前返回与微优化；示例与占位资产清理。
- 阶段4：测试验证与静态分析；文档整理（仅索引与关键准则保留）。

## 交付与回归
- 提供变更清单与代码差异；全量编译通过（Editor/Standalone），示例场景交互流畅；确保包体清洁与依赖边界正确。