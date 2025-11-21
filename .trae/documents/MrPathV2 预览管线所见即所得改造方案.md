## 结论与原因

* 产生“方块 clip”是因为 MaskAtlas 纵向采样数 `_PathSamples` 固定为 64，导致沿路径方向权重按行插值，在长路径和高频噪声下可见分块。

* 证据：材质推参中固定 `_PathSamples=64`（Editor/Preview/PreviewMaterialParameterSetter.cs:52,313），Atlas 构建也使用常量 64（Runtime/Core/MaskAtlasGenerator.cs:63,110）。采样函数按行插值（Editor/Resources/BlendLayer.hlsl:53-62）。

* Atlas 纹理已设置 `Bilinear+Clamp`（Runtime/Core/MaskAtlasGenerator.cs:74-79），因此块状不是采样器问题，而是采样分辨率不足。

* 白色绘制线来自 Editor Handles（Runtime/Preview/PreviewLineRenderer.cs:354-378），与遮罩混合路径解耦，非问题根源。

## 改进目标

1. 自适应 `_PathSamples`，与路径长度统一：避免沿途分块。
2. 统一 Atlas 与 Shader 的 `_PathSamples` 值：消除不一致。
3. 保留 `Bilinear+Clamp`，确保软混合和清晰边缘。
4. 验证绘制线与遮罩叠加无伪影。

## 实施步骤

1. 在 `PreviewMaterialManager` 计算并缓存路径长度（已存在 SetPathLength）；新增根据长度计算 `pathSamples = clamp(ceil(pathLength / targetMeters), 64, 2048)`，默认 `targetMeters=0.5~1.0`。
2. 将该 `pathSamples` 同时传入：

   * Atlas 构建：`MaskAtlasGenerator.BuildMaskAtlas(...)` 的 `pathSamples` 参数；

   * 材质属性：通过 `PreviewMaterialParameterSetter` 写 `_PathSamples`。
3. 保持 Atlas 宽度 `baseResolution=256` 或按道路宽度自适应（可选），并确认 `filterMode=Bilinear`、`wrap=Clamp`。
4. 在预览 Shader `PathPreviewSplatMulti.shader` / `StylizedRoadBlend.shader` 保持现有 `SampleMaskAtlas2D` 逻辑，无需改动；只需确保 `_PathSamples` 与 Atlas 高度一致。
5. 验证：

   * 场景中走查不同路径长度，观察方块伪影是否消失；

   * 将 `targetMeters` 调为更小值（如 0.25m）确认高频噪声仍平滑。

## 额外最佳实践

* 阈值塑形应在 Atlas 构建阶段完成（已实现，Editor/Resources/MaskAtlas.compute:299-301），片元侧不重复塑形。

* 保持上一帧结果纹理透明，避免整块黑底混合（PreviewMaterialManager.cs:265-271,300-305）。

* 绘线使用单次 `Render()` 批处理与 AA 折线（PreviewLineRenderer.cs:539-595），无需改动；若线路密集，可考虑启用 GPU 模式但当前项目设为禁用。

## 我将进行的改动

* 增加 `pathSamples` 自适应计算与双端同步（Atlas+材质）。

* 增加参数注入通道，遵循依赖注入与提前返回原则，不引入额外状态耦合。

* 提供一个可配置的 `targetMeters`（走配置或 Profile 字段）。

## 验证与交付

* 我会在场景中重建预览网格，保证线与遮罩叠加平滑无分块；给出前后对比截图与关键参数值列表。

