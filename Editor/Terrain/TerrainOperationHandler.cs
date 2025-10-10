using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 地形操作处理器：负责UI触发到命令执行的完整链路，支持取消、进度与错误提示。
    /// 单一职责：命令执行与用户反馈。
    /// </summary>
    public class TerrainOperationHandler : IDisposable
    {
        private readonly IHeightProvider _heightProvider;
        private CancellationTokenSource _cts;
        private TerrainCommandBase _currentCommand;

        public TerrainOperationHandler(IHeightProvider heightProvider)
        {
            _heightProvider = heightProvider;
        }

        /// <summary>
        /// 执行地形命令，显示进度并捕获错误。支持取消令牌。
        /// </summary>
        public async Task ExecuteAsync(TerrainCommandBase command, Action<bool> setIsApplying)
        {
            setIsApplying?.Invoke(true);
            _heightProvider?.MarkAsDirty();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _currentCommand = command;
            try
            {
                EditorUtility.DisplayProgressBar("应用路径到地形", $"正在执行: {command?.GetCommandName()}...", 0.3f);
                await command.ExecuteAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogError($"执行 {command?.GetCommandName()} 失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("执行失败", $"操作 {command?.GetCommandName()} 失败，详情请查看控制台日志。", "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                setIsApplying?.Invoke(false);
                _currentCommand = null;
            }
        }

        public void Cancel()
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            _cts.Cancel();
            EditorUtility.DisplayDialog("取消操作", "已请求取消当前地形操作。", "确定");
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}