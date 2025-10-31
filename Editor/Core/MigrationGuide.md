# 统一架构迁移指南

## 概述

本指南帮助您从旧的GPU/CPU分离架构迁移到新的统一架构。新架构以CPU数据源为标准，提供更优雅、简洁、高效的解决方案。

## 架构对比

### 旧架构 (Before)
```
CPU数据源 ←→ GPU数据源 (重复定义)
    ↓           ↓
CpuTerrainPainter  GpuTerrainPainterV2
    ↓           ↓
PaintTerrainCommand (复杂的后端选择逻辑)
```

### 新架构 (After)
```
CPU数据源 (统一标准)
    ↓
UnifiedDataAdapter (转换层)
    ↓
IUnifiedTerrainPainter
    ↓
UnifiedCpuTerrainPainter | UnifiedGpuTerrainPainter
    ↓
UnifiedPaintTerrainCommand (简洁的统一接口)
```

## 核心变更

### 1. 数据源统一

**旧方式:**
```csharp
// GPU特有的PathData
var gpuPathData = new PathData(spinePoints, width, length, bounds);
var pathRecipe = new PathRecipe(layers, falloff, curve);

// CPU特有的数据结构
var cpuPathData = pathCreator.pathData; // 不同的PathData类
var pathProfile = new PathProfile(roadRecipe, width, falloff);
```

**新方式:**
```csharp
// 统一使用CPU数据源
var pathData = pathCreator.pathData;  // Runtime.Core.PathData
var pathProfile = new PathProfile(roadRecipe, width, falloff);

// 适配器自动处理GPU转换
var gpuData = UnifiedDataAdapter.CreateGpuRenderData(pathData, pathProfile, Allocator.TempJob);
```

### 2. 绘制器接口统一

**旧方式:**
```csharp
// 不同的接口和调用方式
ITerrainPainter cpuPainter = new CpuTerrainPainter();
GpuTerrainPainterV2 gpuPainter = GpuTerrainPainterV2.Instance;

// 复杂的参数传递
await cpuPainter.ExecuteAsync(terrain, spineData, profileData, recipeData, roadContour, bounds, coverageMin, coverageMax, token);
await gpuPainter.PaintPathAsync(terrain, spinePoints, width, layers, isPreview);
```

**新方式:**
```csharp
// 统一接口
IUnifiedTerrainPainter painter = UnifiedPainterFactory.CreatePainter(PainterType.Auto, terrain);

// 简洁的调用方式
var result = await painter.PaintAsync(terrain, pathData, pathProfile, isPreview, cancellationToken);
```

### 3. 命令模式简化

**旧方式:**
```csharp
// 复杂的命令类
var command = new PaintTerrainCommand(pathCreator, heightProvider);
await command.ProcessTerrainsAsync(terrains, spine, cancellationToken);
```

**新方式:**
```csharp
// 简洁的命令类
using var command = UnifiedPaintTerrainCommandV2.Create(pathCreator, heightProvider);
var result = await command.ExecuteAsync(terrains, pathProfile, PainterType.Auto, isPreview, cancellationToken);

// 或者使用构建器模式
var result = await new UnifiedPaintCommandBuilder()
    .ForTerrain(terrain)
    .WithPath(pathData)
    .WithProfile(pathProfile)
    .AsPreview(true)
    .PreferPainter(PainterType.GPU)
    .ExecuteAsync(cancellationToken);
```

## 迁移步骤

### 步骤1: 更新引用

**替换命名空间:**
```csharp
// 移除旧的引用
// using __temp.MrPathV2.Editor.GPU;
// using __temp.MrPathV2.Editor.Terrain;

// 添加新的引用
using __temp.MrPathV2.Editor.Core;
using __temp.MrPathV2.Runtime.Core;
```

### 步骤2: 替换绘制器创建

**旧代码:**
```csharp
ITerrainPainter painter;
if (useGpu)
{
    painter = GpuTerrainPainterV2.Instance;
}
else
{
    painter = new CpuTerrainPainter();
}
```

**新代码:**
```csharp
var painter = UnifiedPainterFactory.CreatePainter(
    preferredType: PainterType.Auto,  // 或 GPU/CPU
    terrain: terrain,
    pathBounds: pathBounds
);
```

### 步骤3: 更新绘制调用

**旧代码:**
```csharp
// GPU调用
await gpuPainter.PaintPathAsync(terrain, spinePoints, width, layers, isPreview);

// CPU调用
await cpuPainter.ExecuteAsync(terrain, spineData, profileData, recipeData, roadContour, bounds, coverageMin, coverageMax, token);
```

**新代码:**
```csharp
// 统一调用
var result = await painter.PaintAsync(terrain, pathData, pathProfile, isPreview, cancellationToken);

// 检查结果
if (result.IsSuccess)
{
    Debug.Log($"绘制成功，使用 {result.PainterType} 绘制器，耗时 {result.ExecutionTimeMs}ms");
}
else
{
    Debug.LogError($"绘制失败: {result.ErrorMessage}");
}
```

### 步骤4: 替换命令类

**旧代码:**
```csharp
var command = new PaintTerrainCommand(pathCreator, heightProvider);
try
{
    await command.ProcessTerrainsAsync(terrains, spine, cancellationToken);
}
finally
{
    // 手动清理
}
```

**新代码:**
```csharp
using var command = UnifiedPaintTerrainCommandV2.Create(pathCreator, heightProvider);
var result = await command.ExecuteAsync(terrains, pathProfile, PainterType.Auto, isPreview, cancellationToken);

// 自动清理，无需手动管理资源
if (result.IsSuccess)
{
    Debug.Log($"处理了 {result.ProcessedTerrainCount} 个地形，成功 {result.SuccessfulTerrainCount} 个");
}
```

## 性能优化建议

### 1. 绘制器选择策略

```csharp
// 根据场景选择最佳绘制器
PainterType GetOptimalPainterType(Terrain terrain, PathData pathData)
{
    var terrainSize = terrain.terrainData.alphamapWidth * terrain.terrainData.alphamapHeight;
    var pathComplexity = pathData.positions.Count;
    
    // 大地形或复杂路径使用GPU
    if (terrainSize > 1024 * 1024 || pathComplexity > 100)
    {
        return PainterType.GPU;
    }
    
    // 小地形使用CPU
    return PainterType.CPU;
}
```

### 2. 批处理优化

```csharp
// 批量处理多个地形
var tasks = terrains.Select(terrain => 
    ProcessTerrainAsync(terrain, pathData, pathProfile, cancellationToken)
).ToArray();

var results = await Task.WhenAll(tasks);
```

### 3. 内存管理

```csharp
// 使用using语句确保资源清理
using var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);
using var command = UnifiedPaintTerrainCommandV2.Create(pathCreator);

// 或者手动管理
try
{
    var result = await painter.PaintAsync(terrain, pathData, pathProfile);
}
finally
{
    painter?.Dispose();
}
```

## 常见问题

### Q: 如何确保向后兼容？

A: 新架构保持了核心数据结构的兼容性。`PathData` 和 `PathProfile` 仍然使用相同的定义，只是统一了使用方式。

### Q: 性能是否有影响？

A: 新架构通过以下方式提升性能：
- 统一的数据适配器减少了重复转换
- 智能的绘制器选择算法
- 更好的内存管理和资源清理
- 异步执行和并行处理

### Q: 如何调试绘制问题？

A: 新架构提供了更详细的结果信息：
```csharp
var result = await painter.PaintAsync(terrain, pathData, pathProfile);
if (!result.IsSuccess)
{
    Debug.LogError($"绘制失败: {result.ErrorMessage}");
    Debug.Log($"使用的绘制器: {result.PainterType}");
    Debug.Log($"执行时间: {result.ExecutionTimeMs}ms");
}
```

## 最佳实践

1. **优先使用 `PainterType.Auto`** - 让系统自动选择最佳绘制器
2. **使用 using 语句** - 确保资源正确清理
3. **异步优先** - 使用异步方法避免阻塞主线程
4. **批处理** - 对多个地形使用并行处理
5. **错误处理** - 检查 `TerrainPaintResult.IsSuccess` 和错误信息

## 示例代码

### 完整的迁移示例

```csharp
// 旧代码
public async Task PaintTerrainsOld(List<Terrain> terrains, PathCreator pathCreator)
{
    var command = new PaintTerrainCommand(pathCreator, null);
    try
    {
        var spine = new PathSpine(pathCreator.pathData);
        await command.ProcessTerrainsAsync(terrains, spine, CancellationToken.None);
    }
    catch (Exception ex)
    {
        Debug.LogError($"绘制失败: {ex.Message}");
    }
}

// 新代码
public async Task PaintTerrainsNew(List<Terrain> terrains, PathCreator pathCreator, PathProfile pathProfile)
{
    using var command = UnifiedPaintTerrainCommandV2.Create(pathCreator);
    
    var result = await command.ExecuteAsync(
        terrains, 
        pathProfile, 
        PainterType.Auto, 
        isPreview: false, 
        CancellationToken.None
    );
    
    if (result.IsSuccess)
    {
        Debug.Log($"成功绘制 {result.SuccessfulTerrainCount}/{result.ProcessedTerrainCount} 个地形");
    }
    else
    {
        Debug.LogError($"绘制失败: {result.ErrorMessage}");
    }
}
```

这个新架构提供了更优雅、简洁、高效的解决方案，同时保持了良好的向后兼容性和扩展性。