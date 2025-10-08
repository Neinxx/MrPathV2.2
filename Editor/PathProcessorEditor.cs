// PathProcessorEditor.cs
using UnityEditor;
using UnityEngine;
using MrPathV2.Commands;
using System.Threading.Tasks;

/// <summary>
/// 【最终适配版】路径处理器编辑器。
/// 
/// (大师赞许：此脚本的异步处理、UI状态管理和错误处理已是业界顶尖水准。
/// 我们只需将其验证逻辑与我们新的 PathData 架构对齐即可。)
/// </summary>
[CustomEditor(typeof(PathProcessor))]
public class PathProcessorEditor : Editor
{
    private bool _isApplying;
    private TerrainHeightProvider _heightProvider;

    private void OnEnable()
    {
        // 懒加载模式很好，予以保留
        _heightProvider = new TerrainHeightProvider();
    }

    private void OnDisable()
    {
        _isApplying = false;
        if (_heightProvider != null)
        {

            _heightProvider.Dispose();
            _heightProvider = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        var processor = (PathProcessor)target;
        // PathCreator 的获取方式保持不变，依然是正确的
        var creator = processor.GetComponent<PathCreator>();

        // --- 核心修改 I：更新验证逻辑 ---
        // 使用新的验证方法来检查 PathCreator 的状态
        string warningMessage = GetValidationMessage(creator);
        if (!string.IsNullOrEmpty(warningMessage))
        {
            EditorGUILayout.HelpBox(warningMessage, MessageType.Warning);
            // 按钮应当被禁用，所以我们在这里返回
            GUI.enabled = false;
        }

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            GUI.backgroundColor = _isApplying ? Color.yellow : Color.green;
            string buttonText = _isApplying ? "正在应用..." : "将路径应用到地形";

            // 如果上面验证失败，GUI.enabled已经是false
            if (GUILayout.Button(buttonText, GUILayout.Height(40)))
            {
                // 安全执行异步操作的模式非常出色，予以保留
                _ = ExecuteApplyCommandAsync(creator);
            }
        }

        // 恢复GUI的默认状态
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
    }

    // 异步执行方法的核心逻辑不变，它已经是最佳实践
    private async Task ExecuteApplyCommandAsync(PathCreator creator)
    {
        if (_isApplying) return;

        _isApplying = true;
        Repaint();

        try
        {
            EditorUtility.DisplayProgressBar("应用路径到地形", "正在处理...", 0.3f);

            // --- 【【【 重要联动修改提示 】】】 ---
            // 这里的 ApplyPathToTerrainCommand 也需要进行适配！(详见下方心法详解)
            ICommand command = new ApplyPathToTerrainCommand(creator, _heightProvider);
            await command.ExecuteAsync();

            EditorUtility.DisplayProgressBar("应用路径到地形", "完成!", 1.0f);
            Debug.Log("路径已成功应用到地形");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"应用路径失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("错误", $"应用失败: {ex.Message}", "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _isApplying = false;
            Repaint();
        }
    }

    // --- 核心修改 II：全新的验证方法 ---
    /// <summary>
    /// 提供具体的验证信息，方便用户排查问题。
    /// </summary>
    private string GetValidationMessage(PathCreator creator)
    {
        if (creator == null)
            return "此物体上找不到 PathCreator 组件。";

        if (creator.profile == null)
            return "PathCreator 需要一个有效的 Profile 资产。";

        // 旧的验证: creator.Path == null
        // 新的验证: pathData 是否存在（虽然它总是在），以及是否有足够的点
        if (creator.pathData == null) // 理论上这不会发生，但作为安全检查是好的
            return "PathCreator 内部的 pathData 未初始化。";

        // 旧的验证: creator.NumPoints < 2
        // 新的验证: creator.pathData.KnotCount < 2
        if (creator.pathData.KnotCount < 2)
            return "路径点数量不足（至少需要2个点）。";

        // 检查注册中心是否能找到对应的法则
        if (PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType) == null)
            return $"无法在注册中心找到与类型 '{creator.profile.curveType}' 对应的策略资产。";

        return null; // 返回null表示验证通过
    }
}