using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace MrPathV2
{
    /// <summary>
    /// 安全的Job执行器：提供异常安全的Job调度和内存管理
    /// 集成性能监控和取消令牌支持
    /// </summary>
    public class SafeJobExecutor : IDisposable
    {
        // 性能监控标记
        private static readonly ProfilerMarker s_JobScheduleMarker = new ProfilerMarker("SafeJobExecutor.Schedule");
        private static readonly ProfilerMarker s_JobCompleteMarker = new ProfilerMarker("SafeJobExecutor.Complete");
        private static readonly ProfilerMarker s_JobWaitMarker = new ProfilerMarker("SafeJobExecutor.Wait");

        private bool _disposed = false;

        /// <summary>
        /// 安全执行单个IJobParallelFor
        /// </summary>
        /// <typeparam name="T">Job类型</typeparam>
        /// <param name="job">要执行的Job</param>
        /// <param name="arrayLength">数组长度</param>
        /// <param name="batchSize">批处理大小</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task ExecuteAsync<T>(T job, int arrayLength, int batchSize = 64, CancellationToken cancellationToken = default)
            where T : struct, IJobParallelFor
        {
            JobHandle handle = default;
            
            try
            {
                using (s_JobScheduleMarker.Auto())
                {
                    handle = job.Schedule(arrayLength, batchSize);
                }

                await WaitForJobCompletionAsync(handle, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消操作时确保Job完成
                handle.Complete();
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Job执行失败: {ex.Message}");
                
                // 异常时确保Job完成
                handle.Complete();
                throw;
            }
        }

        /// <summary>
        /// 安全执行单个IJob
        /// </summary>
        /// <typeparam name="T">Job类型</typeparam>
        /// <param name="job">要执行的Job</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task ExecuteAsync<T>(T job, CancellationToken cancellationToken = default)
            where T : struct, IJob
        {
            JobHandle handle = default;
            
            try
            {
                using (s_JobScheduleMarker.Auto())
                {
                    handle = job.Schedule();
                }

                await WaitForJobCompletionAsync(handle, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                handle.Complete();
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Job执行失败: {ex.Message}");
                
                handle.Complete();
                throw;
            }
        }

        /// <summary>
        /// 批量执行多个Job并等待全部完成
        /// </summary>
        /// <param name="jobSchedulers">Job调度器委托列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task ExecuteBatchAsync(Func<JobHandle>[] jobSchedulers, CancellationToken cancellationToken = default)
        {
            if (jobSchedulers == null || jobSchedulers.Length == 0)
                return;

            var handles = new NativeArray<JobHandle>(jobSchedulers.Length, Allocator.TempJob);
            
            try
            {
                // 调度所有Job
                using (s_JobScheduleMarker.Auto())
                {
                    for (int i = 0; i < jobSchedulers.Length; i++)
                    {
                        handles[i] = jobSchedulers[i]();
                    }
                }

                // 合并所有JobHandle
                var combinedHandle = JobHandle.CombineDependencies(handles);
                
                await WaitForJobCompletionAsync(combinedHandle, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消时完成所有Job
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i].Complete();
                }
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"批量Job执行失败: {ex.Message}");
                
                // 异常时完成所有Job
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i].Complete();
                }
                throw;
            }
            finally
            {
                if (handles.Length > 0)
                {
                    handles.Dispose();
                }
            }
        }

        /// <summary>
        /// 使用资源管理器安全执行Job
        /// </summary>
        /// <typeparam name="T">Job类型</typeparam>
        /// <param name="resourceManager">资源管理器</param>
        /// <param name="jobFactory">Job创建工厂</param>
        /// <param name="arrayLength">数组长度</param>
        /// <param name="batchSize">批处理大小</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        public async Task ExecuteWithResourceManagerAsync<T>(
            JobResourceManager resourceManager,
            Func<JobResourceManager, T> jobFactory,
            int arrayLength,
            int batchSize = 64,
            CancellationToken cancellationToken = default)
            where T : struct, IJobParallelFor
        {
            if (resourceManager == null)
                throw new ArgumentNullException(nameof(resourceManager));

            var job = jobFactory(resourceManager);
            await ExecuteAsync(job, arrayLength, batchSize, cancellationToken);
        }

        /// <summary>
        /// 异步等待Job完成，支持取消令牌
        /// </summary>
        /// <param name="jobHandle">Job句柄</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        private static async Task WaitForJobCompletionAsync(JobHandle jobHandle, CancellationToken cancellationToken)
        {
            using (s_JobWaitMarker.Auto())
            {
                while (!jobHandle.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                using (s_JobCompleteMarker.Auto())
                {
                    jobHandle.Complete();
                }
            }
        }

        /// <summary>
        /// 创建带有超时的取消令牌
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="parentToken">父级取消令牌</param>
        /// <returns>组合的取消令牌</returns>
        public static CancellationToken CreateTimeoutToken(int timeoutMs, CancellationToken parentToken = default)
        {
            var timeoutSource = new CancellationTokenSource(timeoutMs);
            
            if (parentToken != default)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(parentToken, timeoutSource.Token).Token;
            }
            
            return timeoutSource.Token;
        }

        /// <summary>
        /// Job执行统计信息
        /// </summary>
        public struct JobExecutionStats
        {
            public int TotalJobsExecuted;
            public int FailedJobs;
            public int CancelledJobs;
            public float AverageExecutionTimeMs;
            public float TotalExecutionTimeMs;
        }

        private static JobExecutionStats s_Stats = new JobExecutionStats();

        /// <summary>
        /// 获取Job执行统计信息
        /// </summary>
        public static JobExecutionStats GetExecutionStats() => s_Stats;

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public static void ResetStats()
        {
            s_Stats = new JobExecutionStats();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // 当前实现中没有需要释放的资源
                // 但保留此方法以便将来扩展
                _disposed = true;
            }
        }
    }
}