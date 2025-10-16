using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 异步操作管理器：提供统一的异步操作管理、取消令牌支持和资源清理
    /// </summary>
    public class AsyncOperationManager : IDisposable
    {
        private readonly Dictionary<string, CancellationTokenSource> _activeTasks;
        private readonly Dictionary<string, TaskCompletionSource<bool>> _taskCompletions;
        private readonly object _lock = new object();
        private bool _disposed = false;

        // 全局取消令牌源
        private CancellationTokenSource _globalCancellationSource;
        
        // 操作超时设置
        private readonly Dictionary<string, TimeSpan> _operationTimeouts;
        private const int DEFAULT_TIMEOUT_SECONDS = 30;

        public AsyncOperationManager()
        {
            _activeTasks = new Dictionary<string, CancellationTokenSource>();
            _taskCompletions = new Dictionary<string, TaskCompletionSource<bool>>();
            _operationTimeouts = new Dictionary<string, TimeSpan>();
            _globalCancellationSource = new CancellationTokenSource();
        }

        /// <summary>
        /// 执行异步操作，支持取消和超时
        /// </summary>
        /// <param name="operationId">操作唯一标识符</param>
        /// <param name="operation">要执行的异步操作</param>
        /// <param name="timeout">操作超时时间（可选）</param>
        /// <returns>操作是否成功完成</returns>
        public async Task<bool> ExecuteAsync(string operationId, Func<CancellationToken, Task> operation, TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(operationId))
            {
                throw new ArgumentException("操作ID不能为空", nameof(operationId));
            }

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            // 检查是否已有同名操作在执行
            if (IsOperationActive(operationId))
            {
                Debug.LogWarning($"[AsyncOperationManager] 操作 '{operationId}' 已在执行中");
                return false;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCancellationSource.Token);
            var tcs = new TaskCompletionSource<bool>();

            lock (_lock)
            {
                _activeTasks[operationId] = cts;
                _taskCompletions[operationId] = tcs;
            }

            try
            {
                // 设置超时
                var operationTimeout = timeout ?? TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS);
                if (_operationTimeouts.ContainsKey(operationId))
                {
                    operationTimeout = _operationTimeouts[operationId];
                }

                cts.CancelAfter(operationTimeout);

                // 执行操作
                await operation(cts.Token);
                
                // 操作成功完成
                tcs.SetResult(true);
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[AsyncOperationManager] 操作 '{operationId}' 被取消");
                tcs.SetResult(false);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AsyncOperationManager] 操作 '{operationId}' 执行失败: {ex.Message}");
                tcs.SetException(ex);
                return false;
            }
            finally
            {
                // 清理资源
                CleanupOperation(operationId);
            }
        }

        /// <summary>
        /// 取消指定操作
        /// </summary>
        /// <param name="operationId">操作ID</param>
        /// <returns>是否成功取消</returns>
        public bool CancelOperation(string operationId)
        {
            lock (_lock)
            {
                if (_activeTasks.TryGetValue(operationId, out var cts))
                {
                    cts.Cancel();
                    Debug.Log($"[AsyncOperationManager] 已请求取消操作 '{operationId}'");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取消所有活动操作
        /// </summary>
        public void CancelAllOperations()
        {
            lock (_lock)
            {
                foreach (var kvp in _activeTasks)
                {
                    kvp.Value.Cancel();
                }
                
       //         Debug.Log($"[AsyncOperationManager] 已请求取消所有活动操作 ({_activeTasks.Count} 个)");
            }
        }

        /// <summary>
        /// 检查操作是否正在执行
        /// </summary>
        /// <param name="operationId">操作ID</param>
        /// <returns>是否正在执行</returns>
        public bool IsOperationActive(string operationId)
        {
            lock (_lock)
            {
                return _activeTasks.ContainsKey(operationId);
            }
        }

        /// <summary>
        /// 获取所有活动操作的ID列表
        /// </summary>
        /// <returns>活动操作ID列表</returns>
        public string[] GetActiveOperationIds()
        {
            lock (_lock)
            {
                var ids = new string[_activeTasks.Count];
                _activeTasks.Keys.CopyTo(ids, 0);
                return ids;
            }
        }

        /// <summary>
        /// 等待指定操作完成
        /// </summary>
        /// <param name="operationId">操作ID</param>
        /// <param name="timeout">等待超时时间</param>
        /// <returns>操作是否成功完成</returns>
        public async Task<bool> WaitForOperationAsync(string operationId, TimeSpan? timeout = null)
        {
            TaskCompletionSource<bool> tcs;
            
            lock (_lock)
            {
                if (!_taskCompletions.TryGetValue(operationId, out tcs))
                {
                    return false; // 操作不存在
                }
            }

            try
            {
                if (timeout.HasValue)
                {
                    using (var timeoutCts = new CancellationTokenSource(timeout.Value))
                    {
                        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout.Value, timeoutCts.Token));
                        if (completedTask == tcs.Task)
                        {
                            return await tcs.Task;
                        }
                        else
                        {
                            Debug.LogWarning($"[AsyncOperationManager] 等待操作 '{operationId}' 超时");
                            return false;
                        }
                    }
                }
                else
                {
                    return await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AsyncOperationManager] 等待操作 '{operationId}' 时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置操作的默认超时时间
        /// </summary>
        /// <param name="operationId">操作ID</param>
        /// <param name="timeout">超时时间</param>
        public void SetOperationTimeout(string operationId, TimeSpan timeout)
        {
            lock (_lock)
            {
                _operationTimeouts[operationId] = timeout;
            }
        }

        /// <summary>
        /// 获取操作统计信息
        /// </summary>
        /// <returns>操作统计信息</returns>
        public OperationStats GetOperationStats()
        {
            lock (_lock)
            {
                return new OperationStats
                {
                    ActiveOperationCount = _activeTasks.Count,
                    TotalOperationCount = _taskCompletions.Count,
                    ActiveOperationIds = GetActiveOperationIds()
                };
            }
        }

        /// <summary>
        /// 清理指定操作的资源
        /// </summary>
        /// <param name="operationId">操作ID</param>
        private void CleanupOperation(string operationId)
        {
            lock (_lock)
            {
                if (_activeTasks.TryGetValue(operationId, out var cts))
                {
                    cts.Dispose();
                    _activeTasks.Remove(operationId);
                }

                // 保留TaskCompletionSource以便外部可以等待结果
                // 它们会在一定时间后自动清理或在Dispose时清理
            }
        }

        /// <summary>
        /// 清理已完成的操作
        /// </summary>
        public void CleanupCompletedOperations()
        {
            lock (_lock)
            {
                var completedOperations = new List<string>();
                
                foreach (var kvp in _taskCompletions)
                {
                    if (kvp.Value.Task.IsCompleted)
                    {
                        completedOperations.Add(kvp.Key);
                    }
                }

                foreach (var operationId in completedOperations)
                {
                    _taskCompletions.Remove(operationId);
                }

                if (completedOperations.Count > 0)
                {
                    Debug.Log($"[AsyncOperationManager] 清理了 {completedOperations.Count} 个已完成的操作");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            // 取消所有活动操作
            CancelAllOperations();

            // 等待短时间让操作有机会清理
            Task.Delay(100).Wait();

            lock (_lock)
            {
                // 清理所有资源
                foreach (var cts in _activeTasks.Values)
                {
                    cts?.Dispose();
                }
                _activeTasks.Clear();

                foreach (var tcs in _taskCompletions.Values)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetCanceled();
                    }
                }
                _taskCompletions.Clear();

                _operationTimeouts.Clear();
            }

            _globalCancellationSource?.Dispose();
            _disposed = true;
        }

        /// <summary>
        /// 操作统计信息
        /// </summary>
        public struct OperationStats
        {
            public int ActiveOperationCount;
            public int TotalOperationCount;
            public string[] ActiveOperationIds;
        }
    }
}