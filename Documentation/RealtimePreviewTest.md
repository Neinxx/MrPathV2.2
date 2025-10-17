# 实时预览测试指南

## 概述
本文档描述如何测试新实现的实时预览功能，该功能允许用户在Scene视图中直接看到StylizedRoadRecipe参数调整的效果。

## 测试步骤

### 1. 准备测试环境
1. 在Scene中创建一个PathCreator对象
2. 为PathCreator分配一个PathProfile
3. 为PathProfile分配一个StylizedRoadRecipe
4. 确保PathProfile的`showPreviewMesh`选项已启用

### 2. 基础实时预览测试
1. 在Inspector中选择StylizedRoadRecipe资产
2. 调整以下参数并观察Scene视图中的变化：
   - `masterOpacity`: 调整整体透明度
   - 各BlendLayer的`opacity`: 调整单层透明度
   - 各BlendLayer的`blendMode`: 切换混合模式
   - 各BlendLayer的`enabled`: 启用/禁用层

### 3. 遮罩系统测试
1. 测试ShoulderMask参数调整：
   - `shoulderWidth`: 调整肩部宽度
   - `shoulderStrength`: 调整肩部强度
   - `edgeFalloff`: 调整边缘衰减
   - `shoulderProfile`: 调整肩部轮廓曲线

2. 测试RoadSurfaceMask参数调整：
   - `surfaceWidthRatio`: 调整表面宽度比例
   - `surfaceStrength`: 调整表面强度
   - `edgeTransition`: 调整边缘过渡
   - `centerOffset`: 调整中心偏移
   - `surfaceProfile`: 调整表面轮廓曲线

### 4. 性能测试
1. 创建包含多个BlendLayer的复杂Recipe
2. 快速调整参数，观察响应速度
3. 检查是否有明显的延迟或卡顿

## 预期结果

### 正常行为
- 调整Recipe参数时，Scene视图中的道路预览应立即更新
- 参数变化应准确反映在预览效果中
- 不应出现明显的性能问题或延迟

### 错误排查
如果预览没有实时更新，检查以下项目：
1. PathCreator是否正确分配了Profile和Recipe
2. PathProfile的`showPreviewMesh`是否启用
3. Console中是否有相关错误信息
4. Scene视图是否处于活动状态

## 技术实现要点

### 事件流程
1. StylizedRoadRecipeEditor检测参数变化
2. 触发OnRecipeModified事件
3. PathCreatorEditor接收事件并调用MarkMaterialsDirty()
4. PathPreviewManager更新材质并重新渲染

### 关键组件
- `StylizedRoadRecipeEditor`: 参数变化检测
- `PathCreatorEditor`: 事件处理和预览更新
- `PathPreviewManager`: 材质管理和渲染
- `PreviewMaterialManager`: 材质属性应用

## 注意事项
- Inspector预览面板已被移除，所有预览效果现在在Scene视图中显示
- 确保Scene视图可见且未被其他窗口遮挡
- 复杂的Recipe可能需要更多时间来更新预览