using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using MrPathV2.Extensions;

namespace MrPathV2.Examples
{
    /// <summary>
    /// 改进的Job实现示例，展示如何使用新的内存管理工具
    /// </summary>
    public class ImprovedJobExample : MonoBehaviour
    {
        [Header("测试参数")]
        [SerializeField] private int dataSize = 10000;
        [SerializeField] private bool enableMemoryTracking = true;
        [SerializeField] private bool useResourceManager = true;

        private JobResourceManager resourceManager;
        private SafeJobExecutor jobExecutor;

        private void Start()
        {
            // 初始化资源管理器
            resourceManager = new JobResourceManager();
            jobExecutor = new SafeJobExecutor();

            // 开始内存监控
            if (enableMemoryTracking)
            {
                StartMemoryMonitoring();
            }
        }

        private void OnDestroy()
        {
            // 清理资源
            resourceManager?.Dispose();
            jobExecutor?.Dispose();
        }

        /// <summary>
        /// 示例1：使用JobResourceManager的安全Job执行
        /// </summary>
        [ContextMenu("运行安全Job示例")]
        public async void RunSafeJobExample()
        {
            Debug.Log("开始执行安全Job示例...");

            try
            {
                if (useResourceManager)
                {
                    await RunJobWithResourceManager();
                }
                else
                {
                    await RunJobWithManualManagement();
                }

                Debug.Log("安全Job示例执行完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Job执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用JobResourceManager的Job执行
        /// </summary>
        private async Task RunJobWithResourceManager()
        {
            // 创建输入数据
            var inputData = resourceManager.CreateNativeArray<float>(dataSize, Allocator.Persistent);
            var outputData = resourceManager.CreateNativeArray<float>(dataSize, Allocator.Persistent);

            // 初始化输入数据
            for (int i = 0; i < dataSize; i++)
            {
                inputData[i] = UnityEngine.Random.Range(0f, 100f);
            }

            // 创建并执行Job
            var job = new ProcessDataJob
            {
                inputData = inputData,
                outputData = outputData,
                multiplier = 2.0f
            };

            // 使用SafeJobExecutor执行Job
            await jobExecutor.ExecuteAsync(job, dataSize, 64);
            
            Debug.Log($"Job执行成功，处理了 {dataSize} 个元素");
                
                // 验证结果
                float sum = 0;
                for (int i = 0; i < math.min(10, dataSize); i++)
                {
                    sum += outputData[i];
                }
                Debug.Log($"前10个结果的平均值: {sum / math.min(10, dataSize):F2}");
            
            // 资源会在resourceManager.Dispose()时自动清理
        }

        /// <summary>
        /// 手动内存管理的Job执行
        /// </summary>
        private async Task RunJobWithManualManagement()
        {
            NativeArray<float> inputData = default;
            NativeArray<float> outputData = default;
            JobHandle jobHandle = default;

            try
            {
                // 使用扩展方法创建带跟踪的NativeArray
                inputData = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<float>(dataSize, Allocator.Persistent);
                outputData = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<float>(dataSize, Allocator.Persistent);

                // 初始化输入数据
                for (int i = 0; i < dataSize; i++)
                {
                    inputData[i] = UnityEngine.Random.Range(0f, 100f);
                }

                // 创建并调度Job
                var job = new ProcessDataJob
                {
                    inputData = inputData,
                    outputData = outputData,
                    multiplier = 2.0f
                };

                jobHandle = job.Schedule(dataSize, 64);

                // 等待Job完成
                while (!jobHandle.IsCompleted)
                {
                    await Task.Yield();
                }
                jobHandle.Complete();

                Debug.Log($"手动管理Job执行完成，处理了 {dataSize} 个元素");
            }
            finally
            {
                // 确保资源被正确释放
                if (jobHandle.IsCompleted)
                {
                    jobHandle.Complete();
                }

                inputData.SafeDispose();
                outputData.SafeDispose();
            }
        }

        /// <summary>
        /// 示例2：批量Job执行
        /// </summary>
        [ContextMenu("运行批量Job示例")]
        public async void RunBatchJobExample()
        {
            Debug.Log("开始执行批量Job示例...");

            var batchSize = 5;
            var jobs = new ProcessDataJob[batchSize];
            var inputArrays = new NativeArray<float>[batchSize];
            var outputArrays = new NativeArray<float>[batchSize];

            try
            {
                // 创建多个Job
                for (int i = 0; i < batchSize; i++)
                {
                    inputArrays[i] = resourceManager.CreateNativeArray<float>(dataSize / batchSize, Allocator.Persistent);
                    outputArrays[i] = resourceManager.CreateNativeArray<float>(dataSize / batchSize, Allocator.Persistent);

                    // 初始化数据
                    for (int j = 0; j < dataSize / batchSize; j++)
                    {
                        inputArrays[i][j] = UnityEngine.Random.Range(0f, 100f);
                    }

                    jobs[i] = new ProcessDataJob
                    {
                        inputData = inputArrays[i],
                        outputData = outputArrays[i],
                        multiplier = 1.5f + i * 0.5f
                    };
                }

                // 批量执行Job
                await jobExecutor.ExecuteBatchAsync(
                    jobs.Select(job => new Func<JobHandle>(() => job.Schedule(dataSize / batchSize, 32))).ToArray()
                );

                Debug.Log($"批量Job执行成功，处理了 {batchSize} 个Job");
            }
            catch (Exception ex)
            {
                Debug.LogError($"批量Job执行异常: {ex.Message}");
            }

            // 资源会自动清理
        }

        /// <summary>
        /// 开始内存监控
        /// </summary>
        private async void StartMemoryMonitoring()
        {
            var cancellationToken = this.GetCancellationTokenOnDestroy();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(5000, cancellationToken); // 每5秒检查一次

                    var stats = MemoryTracker.GetMemoryStats();
                    if (stats.ActiveAllocations > 0)
                    {
                        Debug.Log($"内存监控 - 活跃分配: {stats.ActiveAllocations}, " +
                                 $"当前使用: {FormatBytes(stats.CurrentMemoryUsage)}, " +
                                 $"峰值: {FormatBytes(stats.PeakMemoryUsage)}");

                        // 检查潜在泄漏
                        var leaks = MemoryTracker.GetPotentialLeaks(2.0); // 2分钟阈值
                        if (leaks.Count > 0)
                        {
                            Debug.LogWarning($"检测到 {leaks.Count} 个潜在内存泄漏");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需要处理
            }
        }

        /// <summary>
        /// 生成内存报告
        /// </summary>
        [ContextMenu("生成内存报告")]
        public void GenerateMemoryReport()
        {
            var report = MemoryTracker.GenerateMemoryReport();
            Debug.Log(report);

            // 也可以保存到文件
            var filePath = Application.persistentDataPath + "/memory_report.txt";
            System.IO.File.WriteAllText(filePath, report);
            Debug.Log($"内存报告已保存到: {filePath}");
        }

        /// <summary>
        /// 重置内存统计
        /// </summary>
        [ContextMenu("重置内存统计")]
        public void ResetMemoryStats()
        {
            MemoryTracker.ResetStats();
            Debug.Log("内存统计已重置");
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    /// <summary>
    /// 示例Job：处理数据
    /// </summary>
    public struct ProcessDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> inputData;
        [WriteOnly] public NativeArray<float> outputData;
        [ReadOnly] public float multiplier;

        public void Execute(int index)
        {
            // 模拟一些计算
            float value = inputData[index];
            value = math.sin(value) * multiplier;
            value = math.sqrt(math.abs(value));
            outputData[index] = value;
        }
    }
}

/// <summary>
/// 扩展方法：为MonoBehaviour添加取消令牌支持
/// </summary>
public static class MonoBehaviourExtensions
{
    public static CancellationToken GetCancellationTokenOnDestroy(this MonoBehaviour monoBehaviour)
    {
        var source = new CancellationTokenSource();
        
        // 当GameObject被销毁时取消令牌
        if (monoBehaviour != null)
        {
            void OnDestroy()
            {
                source?.Cancel();
                source?.Dispose();
            }

            // 注册销毁事件（这里简化处理，实际项目中可能需要更复杂的生命周期管理）
            monoBehaviour.StartCoroutine(WaitForDestroy(monoBehaviour.gameObject, OnDestroy));
        }

        return source.Token;
    }

    private static System.Collections.IEnumerator WaitForDestroy(GameObject gameObject, System.Action onDestroy)
    {
        while (gameObject != null)
        {
            yield return null;
        }
        onDestroy?.Invoke();
    }
}