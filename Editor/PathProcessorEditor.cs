using UnityEditor;
using UnityEngine;
using MrPathV2.Commands;
using System.Threading.Tasks;

[CustomEditor(typeof(PathProcessor))]
public class PathProcessorEditor : Editor
{
    private bool _isApplying;
    private TerrainHeightProvider _heightProvider;
    // private SerializedProperty _someImportantProperty; // 示例：如果有需要序列化的属性可添加
    private PathCreator _lastValidCreator; // 缓存有效的PathCreator引用

    // 初始化资源，只在编辑器创建时执行一次
    private void OnEnable()
    {
        // 延迟初始化高度提供者，避免不必要的资源加载
        _heightProvider = new TerrainHeightProvider();

        // 缓存序列化属性（如果需要）
        // _someImportantProperty = serializedObject.FindProperty("someField");
    }

    // 清理资源，防止内存泄漏
    private void OnDisable()
    {
        _isApplying = false;
        _lastValidCreator = null;
        // 如果高度提供者需要释放资源
        if (_heightProvider != null)
        {
            _heightProvider.Dispose(); // 假设实现了IDisposable接口
            _heightProvider = null;
        }
    }

    public override void OnInspectorGUI()
    {
        // 使用序列化对象绘制默认Inspector，更安全地处理属性
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var processor = (PathProcessor)target;

        // 性能优化：只在必要时获取组件
        PathCreator creator = null;
        if (processor != null)
        {
            creator = processor.GetComponent<PathCreator>();

            // 缓存有效引用，减少GetComponent调用
            if (creator != null && creator.profile != null && creator.Path != null && creator.NumPoints >= 2)
            {
                _lastValidCreator = creator;
            }
            else if (_lastValidCreator != null && !IsCreatorValid(_lastValidCreator))
            {
                _lastValidCreator = null; // 缓存失效
            }
        }

        // 前置检查，清晰显示错误原因
        string warningMessage = GetValidationMessage(creator);
        if (!string.IsNullOrEmpty(warningMessage))
        {
            EditorGUILayout.HelpBox(warningMessage, MessageType.Warning);
            return;
        }

        // 按钮样式优化
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            GUI.backgroundColor = _isApplying ? Color.yellow : Color.green;
            string buttonText = _isApplying ? "正在应用..." : "将路径应用到地形";

            using (new EditorGUI.DisabledGroupScope(_isApplying))
            {
                if (GUILayout.Button(buttonText, GUILayout.Height(40)))
                {
                    // 安全执行异步操作
                    _ = ExecuteApplyCommandAsync(creator);
                }
            }
        }
        GUI.backgroundColor = Color.white;
    }

    // 独立的异步执行方法，避免直接在OnInspectorGUI中使用async void
    private async Task ExecuteApplyCommandAsync(PathCreator creator)
    {
        if (_isApplying || creator == null || !IsCreatorValid(creator))
            return;

        _isApplying = true;
        Repaint(); // 立即更新UI状态

        try
        {
            // 编辑器环境下使用EditorUtility.DisplayProgressBar提供反馈
            EditorUtility.DisplayProgressBar("应用路径到地形", "正在处理...", 0.3f);

            ICommand command = new ApplyPathToTerrainCommand(creator, _heightProvider);
            await command.ExecuteAsync();

            EditorUtility.DisplayProgressBar("应用路径到地形", "完成!", 1.0f);
            Debug.Log("路径已成功应用到地形");
        }
        catch (System.Exception ex)
        {
            // 详细错误日志，方便调试
            Debug.LogError($"应用路径失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("错误", $"应用失败: {ex.Message}", "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _isApplying = false;
            Repaint(); // 恢复UI状态
        }
    }

    // 独立的验证方法，提高可读性和可维护性
    private bool IsCreatorValid(PathCreator creator)
    {
        return creator != null
               && creator.profile != null
               && creator.Path != null
               && creator.NumPoints >= 2;
    }

    // 提供具体的验证信息，方便用户排查问题
    private string GetValidationMessage(PathCreator creator)
    {
        if (creator == null)
            return "找不到PathCreator组件";

        if (creator.profile == null)
            return "PathCreator的profile未设置";

        if (creator.Path == null)
            return "PathCreator未生成路径数据";

        if (creator.NumPoints < 2)
            return "路径点数量不足（至少需要2个点）";

        return null;
    }

    // 防止在后台执行时对象被销毁导致的错误
    private void OnDestroy()
    {
        _isApplying = false;
    }
}