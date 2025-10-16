// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Terrain/TerrainOperationHandler.cs (最终统一版)
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace MrPathV2
{
    /// <summary>
    /// 地形操作处理器：统一管理地形命令的执行、进度显示、错误处理和取消操作。
    /// 优化版本：集成AsyncOperationManager，提供更好的异步操作管理
    /// </summary>
    public class TerrainOperationHandler : IDisposable
    {
        private readonly IHeightProvider _heightProvider;
        private readonly AsyncOperationManager _asyncManager;
        
        // 保持向后兼容的字段
        private CancellationTokenSource _cts;
        private TerrainCommandBase _currentCommand;

        public TerrainOperationHandler(IHeightProvider heightProvider)
        {
            _heightProvider = heightProvider;
            _asyncManager = new AsyncOperationManager();
            
            // 设置地形操作的默认超时时间
            _asyncManager.SetOperationTimeout("FlattenTerrain", TimeSpan.FromMinutes(5));
            _asyncManager.SetOperationTimeout("PaintTerrain", TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// 执行地形命令，显示进度并捕获错误。支持取消令牌。
        /// 优化版本：使用AsyncOperationManager进行统一管理
        /// </summary>
        public async Task ExecuteAsync(TerrainCommandBase command, Action<bool> setIsApplying)
        {
            if (command == null)
            {
                Debug.LogError("[TerrainOperationHandler] 命令不能为空");
                return;
            }

            var operationId = command.GetCommandName();
            
            // 检查是否已有同类操作在执行
            if (_asyncManager.IsOperationActive(operationId))
            {
                Debug.LogWarning($"[TerrainOperationHandler] 操作 '{operationId}' 已在执行中");
                return;
            }

            setIsApplying?.Invoke(true);
            _heightProvider?.MarkAsDirty();
            
            // 保持向后兼容
            _currentCommand = command;

            try
            {
                EditorUtility.DisplayProgressBar("应用路径到地形", $"正在执行: {operationId}...", 0.3f);
                
                // 使用AsyncOperationManager执行操作
                bool success = await _asyncManager.ExecuteAsync(operationId, async (token) =>
                {
                    // 创建兼容的CancellationTokenSource
                    _cts?.Dispose();
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    
                    await command.ExecuteAsync(_cts.Token);
                });

                if (!success)
                {
                    Debug.LogWarning($"[TerrainOperationHandler] 操作 '{operationId}' 未能成功完成");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TerrainOperationHandler] 执行 {operationId} 失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("执行失败", $"操作 {operationId} 失败，详情请查看控制台日志。", "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                setIsApplying?.Invoke(false);
                _currentCommand = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 取消当前操作
        /// </summary>
        public void Cancel()
        {
            if (_currentCommand != null)
            {
                var operationId = _currentCommand.GetCommandName();
                bool cancelled = _asyncManager.CancelOperation(operationId);
                
                if (cancelled)
                {
                    EditorUtility.DisplayDialog("取消操作", $"已请求取消操作: {operationId}", "确定");
                }
                else
                {
                    Debug.LogWarning($"[TerrainOperationHandler] 无法取消操作: {operationId}");
                }
            }
            
            // 保持向后兼容
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }

        /// <summary>
        /// 取消所有活动操作
        /// </summary>
        public void CancelAllOperations()
        {
            _asyncManager.CancelAllOperations();
            EditorUtility.DisplayDialog("取消操作", "已请求取消所有活动的地形操作。", "确定");
        }

        /// <summary>
        /// 检查是否有操作正在执行
        /// </summary>
        /// <returns>是否有活动操作</returns>
        public bool HasActiveOperations()
        {
            var stats = _asyncManager.GetOperationStats();
            return stats.ActiveOperationCount > 0;
        }

        /// <summary>
        /// 获取当前活动操作的信息
        /// </summary>
        /// <returns>活动操作信息</returns>
        public string GetActiveOperationsInfo()
        {
            var stats = _asyncManager.GetOperationStats();
            if (stats.ActiveOperationCount == 0)
            {
                return "无活动操作";
            }

            return $"活动操作 ({stats.ActiveOperationCount}): {string.Join(", ", stats.ActiveOperationIds)}";
        }

        /// <summary>
        /// 等待所有操作完成
        /// </summary>
        /// <param name="timeout">等待超时时间</param>
        /// <returns>是否所有操作都已完成</returns>
        public async Task<bool> WaitForAllOperationsAsync(TimeSpan? timeout = null)
        {
            var activeIds = _asyncManager.GetActiveOperationIds();
            var tasks = new Task<bool>[activeIds.Length];
            
            for (int i = 0; i < activeIds.Length; i++)
            {
                tasks[i] = _asyncManager.WaitForOperationAsync(activeIds[i], timeout);
            }

            if (tasks.Length == 0) return true;

            try
            {
                var results = await Task.WhenAll(tasks);
                return Array.TrueForAll(results, r => r);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TerrainOperationHandler] 等待操作完成时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清理已完成的操作
        /// </summary>
        public void CleanupCompletedOperations()
        {
            _asyncManager.CleanupCompletedOperations();
        }

        public void Dispose()
        {
            _asyncManager?.Dispose();
            _cts?.Dispose();
        }
    }
}