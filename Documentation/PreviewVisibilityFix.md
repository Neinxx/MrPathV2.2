# 预览可见性修复说明

## 问题描述
当用户选中 `StylizedRoadRecipe` 时，场景中的道路预览会消失。

## 问题原因
在 `PathCreatorEditor.OnSceneGUI()` 方法中，预览激活逻辑与工具激活条件耦合过紧：

```csharp
// 原始问题代码
if (ToolManager.activeToolType != typeof(PathCreatorTool) && Tools.current != Tool.Move)
{
    return; // 这里会导致预览被禁用
}
_ctx.PreviewManager.SetActive(true);
```

当选中 `StylizedRoadRecipe` 时，`PathCreatorTool` 不是激活工具，导致预览被禁用。

## 修复方案
将预览激活逻辑与工具激活条件分离：

1. **预览始终激活**：只要 PathCreator 有效，预览就保持激活状态
2. **工具条件独立**：工具激活条件只影响句柄绘制，不影响预览显示

## 修复代码
```csharp
private void OnSceneGUI()
{
    _targetCreator = target as PathCreator;
    if (_targetCreator == null) return;
    
    // 确保预览始终激活（即使选中了Recipe等其他对象）
    if (_ctx != null && _ctx.IsPathValid())
    {
        _ctx.PreviewManager.SetActive(true);
    }
    else
    {
        _ctx?.PreviewManager?.SetActive(false);
        return;
    }

    // 工具激活条件只影响句柄绘制，不影响预览
    if (ToolManager.activeToolType != typeof(PathCreatorTool) && Tools.current != Tool.Move)
    {
        return; // 只返回，不禁用预览
    }
    
    // 继续句柄绘制逻辑...
}
```

## 修复效果
- ✅ 选中 `StylizedRoadRecipe` 时，道路预览保持可见
- ✅ 实时参数调整功能正常工作
- ✅ 工具激活逻辑不受影响
- ✅ 句柄绘制逻辑保持独立

## 测试步骤
1. 在场景中创建一个 PathCreator
2. 分配 PathProfile 和 StylizedRoadRecipe
3. 选中 StylizedRoadRecipe 对象
4. 验证道路预览仍然可见
5. 调整 Recipe 参数，确认实时更新正常

## 技术要点
- 预览可见性与工具激活状态解耦
- 保持向后兼容性
- 不影响现有的句柄交互逻辑