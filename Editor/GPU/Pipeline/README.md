# 地形纹理绘制管线（分阶段架构）

## 设计目标
- 清晰阶段划分：数据准备（顶点处理）、栅格化（Compute）、解析（片段应用）
- 明确输入输出：每阶段仅依赖必需数据，返回标准化结果
- 单一职责与提前返回：失败即时返回并记录错误，不做隐式副作用

## 组件
- `IPipelineStage<TIn,TOut>`：阶段接口，统一调用约束
- `DataPrepStage`：将 `Terrain + PathData + PathRecipe` 打包为 `GpuDataPacket`
- `RasterizationStage`：执行计算着色器并输出 `RenderTexture`
- `ResolveStage`：将 `GpuRenderResult` 写回 Terrain（非预览）
- `TerrainPaintPipeline`：编排三个阶段，提供 `Execute/Resolve` 入口

## 使用
在 `GpuTerrainRenderer` 中注入管线：
- 调用 `Execute(terrain, path, recipe, isPreview)` 获取纹理与计算参数
- 由渲染器创建 `GpuRenderResult`，缓存并在非预览时调用 `ApplyToTerrain`

## 最佳实践
- 资源管理统一由 `GpuResourceManager` 负责，阶段只读/写自身数据
- 栅格化使用 ROI（覆盖区域）减少无关像素计算
- 所有阶段严格防御式校验，失败提前返回，记录详细错误

## 迁移
- 原有直接调用 `PrepareGpuData + ExecuteCompute` 已由管线封装
- 外部 API（`GpuTerrainPainterV2`、`GpuTerrainRenderer`）保持不变

