# 新Mask系统测试指南

## 概述
本文档描述了新实现的路肩遮罩(ShoulderMask)和路面遮罩(RoadSurfaceMask)系统的功能和测试方法。

## 新功能

### 1. 路肩遮罩 (ShoulderMask)
- **功能**: 专门用于道路两侧路肩区域的遮罩
- **主要属性**:
  - `shoulderWidthRatio`: 路肩宽度比例 (0-1)
  - `shoulderStrength`: 路肩强度 (0-1)
  - `edgeFalloff`: 边缘衰减 (0-1)
  - `shoulderProfile`: 路肩轮廓曲线
  - `enableLeftShoulder`: 启用左路肩
  - `enableRightShoulder`: 启用右路肩

### 2. 路面遮罩 (RoadSurfaceMask)
- **功能**: 专门用于道路中央路面区域的遮罩
- **主要属性**:
  - `surfaceWidthRatio`: 路面宽度比例 (0-1)
  - `surfaceStrength`: 路面强度 (0-1)
  - `edgeTransition`: 边缘过渡 (0-1)
  - `centerOffset`: 中心偏移 (-1到1)
  - `surfaceProfile`: 路面轮廓曲线
  - `falloffType`: 衰减类型 (Linear/Smooth/Sharp)

### 3. BlendLayer增强
- **新增属性**:
  - `maskType`: 遮罩类型选择 (General/Shoulder/RoadSurface)
  - `shoulderMask`: 路肩遮罩引用
  - `roadSurfaceMask`: 路面遮罩引用
- **新增方法**:
  - `GetActiveMask()`: 获取当前活动的遮罩
  - `OnMaskTypeChanged()`: 遮罩类型变更回调
  - `HasValidMask()`: 检查是否有有效遮罩
  - `GetMaskTypeInfo()`: 获取遮罩类型信息

## 测试文件

### 创建的测试资产
1. `TestShoulderMask.asset` - 路肩遮罩测试资产
2. `TestRoadSurfaceMask.asset` - 路面遮罩测试资产
3. `TestMaskSystemRecipe.asset` - 包含两种遮罩类型的测试配方

### 编辑器UI改进
- 快速创建按钮: "创建路肩遮罩" 和 "创建路面遮罩"
- 中文显示名称支持
- 改进的遮罩类型选择界面

## 测试步骤

### 1. 基本功能测试
1. 在Unity编辑器中打开项目
2. 导航到 `Settings/TestMaskSystemRecipe.asset`
3. 检查Inspector中是否正确显示新的遮罩类型选项
4. 验证路肩层和路面层的遮罩类型设置

### 2. 遮罩创建测试
1. 选择任意StylizedRoadRecipe资产
2. 在Inspector中找到"快速创建遮罩"部分
3. 点击"创建路肩遮罩"按钮，验证是否成功创建
4. 点击"创建路面遮罩"按钮，验证是否成功创建

### 3. 预览测试
1. 创建或选择一个路径对象
2. 应用TestMaskSystemRecipe配方
3. 观察预览效果，验证:
   - 路肩遮罩是否正确显示在道路两侧
   - 路面遮罩是否正确显示在道路中央
   - 遮罩强度和衰减效果是否符合预期

### 4. 参数调整测试
1. 选择路肩遮罩资产，调整以下参数:
   - 修改`shoulderWidthRatio`，观察路肩宽度变化
   - 修改`shoulderStrength`，观察强度变化
   - 修改`edgeFalloff`，观察边缘衰减效果
2. 选择路面遮罩资产，调整以下参数:
   - 修改`surfaceWidthRatio`，观察路面宽度变化
   - 修改`centerOffset`，观察中心偏移效果
   - 修改`falloffType`，观察不同衰减类型效果

## 预期结果
- 所有新的遮罩类型应该正确显示和工作
- 编辑器UI应该提供直观的遮罩创建和配置界面
- 预览效果应该实时反映遮罩参数的变化
- 系统应该向后兼容现有的渐变遮罩和噪声遮罩

## 故障排除
如果遇到问题，请检查:
1. 所有新的脚本文件是否正确编译
2. 遮罩资产的GUID引用是否正确
3. Unity控制台是否有相关错误信息
4. 预览材质和着色器是否正确更新