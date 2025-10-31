# Recipe ScriptableObject 使用指南

## 概述

MrPath V2 使用 `StylizedRoadRecipe` ScriptableObject 来定义道路的外观和材质设置。每个 `PathCreator` 实例可以通过其 `PathProfile` 直接引用不同的 Recipe SO 文件，实现多样化的道路样式。

## 核心概念

### PathProfile.roadRecipe
- **类型**: `StylizedRoadRecipe` (ScriptableObject)
- **用途**: 直接引用 Recipe SO 文件
- **特点**: 保持 Unity ScriptableObject 的原生设计，支持资源共享和序列化

## 使用方法

### 1. Inspector 配置

在 PathCreator 的 Inspector 面板中：

1. 展开 **PathProfile** 设置
2. 在 **Preview Section** 中找到 **StylizedRoadRecipe** 字段
3. 直接拖拽或选择不同的 Recipe SO 文件

### 2. 代码配置

```csharp
// 获取 PathCreator 实例
PathCreator pathCreator = GetComponent<PathCreator>();

// 直接设置 Recipe SO
pathCreator.profile.roadRecipe = yourRecipeAsset;

// 运行时切换 Recipe
pathCreator.profile.roadRecipe = anotherRecipeAsset;
```

### 3. 多实例使用

不同的 PathCreator 实例可以使用不同的 Recipe SO：

```csharp
// PathCreator 1 使用城市道路样式
pathCreator1.profile.roadRecipe = cityRoadRecipe;

// PathCreator 2 使用乡村道路样式  
pathCreator2.profile.roadRecipe = ruralRoadRecipe;

// PathCreator 3 使用高速公路样式
pathCreator3.profile.roadRecipe = highwayRecipe;
```

## Recipe SO 文件管理

### 创建新的 Recipe SO

1. 在 Project 窗口中右键
2. 选择 **Create > MrPath > Stylized Road Recipe**
3. 配置道路层级、材质和混合设置
4. 保存为 `.asset` 文件

### 组织 Recipe 文件

建议的文件夹结构：
```
Assets/
├── MrPath/
│   ├── Recipes/
│   │   ├── Urban/
│   │   │   ├── CityStreet.asset
│   │   │   └── Sidewalk.asset
│   │   ├── Rural/
│   │   │   ├── DirtRoad.asset
│   │   │   └── GrassPath.asset
│   │   └── Highway/
│   │       ├── Asphalt.asset
│   │       └── Concrete.asset
```

## 最佳实践

### 1. Recipe 共享
- 多个 PathCreator 可以共享同一个 Recipe SO
- 修改 Recipe SO 会影响所有使用它的 PathCreator
- 适合统一管理相同类型的道路样式

### 2. 性能考虑
- Recipe SO 是轻量级资源，可以安全地在运行时切换
- 避免频繁创建新的 Recipe 实例
- 使用预制的 Recipe SO 库提高性能

### 3. 版本控制
- Recipe SO 文件可以正常进行版本控制
- 团队成员可以共享和同步 Recipe 设置
- 支持 Unity 的资源依赖管理

## 示例场景

项目中包含了一个测试场景 `SimpleRecipeTestScene.unity`，演示了：

- 两个 PathCreator 使用不同的 Recipe SO
- 运行时切换 Recipe 的方法
- Recipe 引用的验证和调试

## 故障排除

### 常见问题

1. **Recipe 为空**
   - 检查 `pathCreator.profile.roadRecipe` 是否已分配
   - 确保 Recipe SO 文件存在且未损坏

2. **样式不生效**
   - 验证 Recipe SO 的层级配置是否正确
   - 检查材质和纹理引用是否有效

3. **运行时切换无效果**
   - 确保在切换后触发了预览更新
   - 检查是否有缓存需要清理

### 调试方法

使用测试脚本 `SimpleRecipeTest.cs` 进行调试：

```csharp
// 验证 Recipe 分配
Debug.Log($"Current Recipe: {pathCreator.profile.roadRecipe?.name}");

// 检查 Recipe 类型
Debug.Log($"Is ScriptableObject: {pathCreator.profile.roadRecipe is ScriptableObject}");
```

## 技术实现

### 简化设计
- 移除了复杂的实例化逻辑
- 直接使用 ScriptableObject 引用
- 保持 Unity 原生的资源管理方式

### 向后兼容
- 现有的 Recipe SO 文件无需修改
- API 保持简洁和直观
- 支持所有 Unity 版本的 ScriptableObject 特性

---

*本文档描述了 MrPath V2 中 Recipe ScriptableObject 的使用方法。如有问题，请参考示例场景或联系开发团队。*