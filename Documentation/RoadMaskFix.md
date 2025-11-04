# 道路遮罩修复文档

## 问题描述

用户报告了两个问题：
1. **计算着色器错误**: "Compute shader (PaintSplatmapCompute): Property (road_mask) at kernel index (0) is not set"
2. **绘制范围问题**: GPU绘制应该只影响预览道路网格覆盖的地形区域

## 根本原因分析

### 问题1: road_mask属性未设置
- `PaintSplatmapCompute.compute` 着色器中定义了 `road_mask` 纹理参数
- 但在 `GpuComputeDispatcher.cs` 的 `BindComputeParameters` 方法中没有设置这个参数
- `GpuDataPacket` 结构中也缺少 `RoadMask` 字段

### 问题2: 缺少区域限制机制
- 虽然计算着色器有遮罩门控逻辑，但没有生成实际的道路遮罩纹理
- 需要根据道路轮廓生成遮罩纹理来限制绘制区域

## 解决方案

### 1. 修复 GpuDataPacket 结构
**文件**: `GpuDataStreamer.cs`
```csharp
public struct GpuDataPacket
{
    public ComputeBuffer SpineBuffer;
    public ComputeBuffer ContourBuffer;
    public ComputeBuffer LayerParamsBuffer;
    public RenderTexture AlphaMapTexture;
    public Texture2D RoadMask;  // 新增：道路遮罩纹理
    public GpuComputeParams ComputeParams;
    public bool IsValid;
}
```

### 2. 添加道路遮罩生成功能
**文件**: `GpuDataStreamer.cs`
- 新增 `GenerateRoadMask` 方法，根据道路轮廓生成遮罩纹理
- 使用射线投射算法判断像素是否在道路区域内
- 在 `PrepareGpuData` 方法中调用遮罩生成

```csharp
private Texture2D GenerateRoadMask(UnityEngine.Terrain terrain, PathData pathData)
{
    // 创建与地形AlphaMap相同分辨率的遮罩纹理
    // 使用射线投射算法检查每个像素是否在道路轮廓内
    // 道路内为255（白色），道路外为0（黑色）
}
```

### 3. 修复参数绑定
**文件**: `GpuComputeDispatcher.cs`
- 在 `ShaderProperties` 类中添加 `MaskThreshold` 属性
- 在 `BindComputeParameters` 方法中设置 `road_mask` 纹理和 `mask_threshold` 参数

```csharp
// 绑定道路遮罩纹理
if (dataPacket.RoadMask != null)
{
    shader.SetTexture(kernel, ShaderProperties.RoadMask, dataPacket.RoadMask);
}
shader.SetFloat(ShaderProperties.MaskThreshold, 0.5f);
```

## 计算着色器工作原理

**文件**: `PaintSplatmapCompute.compute`
```hlsl
// 从道路遮罩纹理采样
float mask_sample = road_mask.Load(int3(pixel_coord, 0));
float maskGate = (mask_sample > mask_threshold) ? 1.0 : 0.0;
if (maskGate <= 0.0) return;  // 如果不在道路区域，直接返回
```

这确保了只有在道路遮罩区域内的像素才会被绘制。

## 测试验证

创建了 `RoadMaskTest.cs` 测试脚本，包含：
- GPU组件初始化测试
- 着色器属性验证
- 可通过Unity菜单 "MrPath/Tests/" 运行

## 技术细节

### 遮罩纹理格式
- 格式: `TextureFormat.R8` (单通道8位)
- 分辨率: 与地形AlphaMap相同
- 值范围: 0-255 (0=道路外, 255=道路内)

### 坐标转换
- 纹理坐标 → 世界坐标
- 使用地形位置和尺寸进行转换
- 支持任意地形尺寸和位置

### 性能考虑
- 遮罩纹理在GPU数据准备阶段生成一次
- 计算着色器中使用高效的纹理采样
- 门控检查在着色器早期进行，避免不必要的计算

## 结果

修复后：
1. ✅ 消除了 "road_mask property not set" 错误
2. ✅ GPU绘制只影响道路覆盖的地形区域
3. ✅ 保持了原有的性能特性
4. ✅ 代码结构清晰，易于维护

## 相关文件

- `GpuDataStreamer.cs` - 数据流传输和遮罩生成
- `GpuComputeDispatcher.cs` - 参数绑定和着色器调度
- `PaintSplatmapCompute.compute` - 计算着色器实现
- `RoadMaskTest.cs` - 测试验证脚本

## 新增：遮罩缺失时的防守逻辑（2025-11）

- 新增计算着色器 Uniform `int use_road_mask`：为 `1` 时启用 `road_mask` 的阈值门控；为 `0` 时不采样遮罩，仅依赖 ROI 与距离逻辑绘制，避免未绑定遮罩导致“全图禁绘”。
- CPU 侧在 `UnifiedGpuTerrainPainter.ExecuteComputeShader` 中绑定 `use_road_mask`（`roadMask ? 1 : 0`）。
- 统一参数名称：计算着色器使用 `road_width`；CPU 侧改为设置 `road_width`（替换原先误用的 `path_width`）。

### 验证要点
- 日志或断点确认：`use_road_mask` 在两种路径下正确设置为 `0/1`；`road_width` 与路径配置一致。
- 可视验证：去掉遮罩绑定时仍可在 ROI 内看到道路涂抹；绑定遮罩后，非道路区域不再被绘制。
