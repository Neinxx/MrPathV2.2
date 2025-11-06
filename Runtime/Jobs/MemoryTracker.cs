using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     内存跟踪器：监控NativeArray分配和释放，检测内存泄漏
    ///     提供详细的内存使用统计和调试信息
    /// </summary>
    public static class MemoryTracker
    {
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> SActiveAllocations = new ConcurrentDictionary<IntPtr, AllocationInfo>();

        private static long m_STotalAllocations;
        private static long m_STotalDeallocations;
        private static long m_STotalBytesAllocated;
        private static long m_STotalBytesFreed;
        private static long m_SPeakActiveAllocations;
        private static long m_SPeakMemoryUsage;

        /// <summary>
        ///     跟踪NativeArray分配
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">分配的数组</param>
        /// <param name="allocator">分配器类型</param>
        public static unsafe void TrackAllocation<T>(NativeArray<T> array, Allocator allocator) where T : struct
        {
            if (!array.IsCreated) return;

            var info = new AllocationInfo
            {
                TypeName = typeof(T).Name,
                ElementCount = array.Length,
                ElementSize = UnsafeUtility.SizeOf<T>(),
                AllocatorType = allocator,
                AllocationTime = DateTime.Now,
                StackTrace = Application.isEditor ? Environment.StackTrace : "N/A"
            };

            var ptr = array.GetUnsafePtr();
            SActiveAllocations.TryAdd(new IntPtr(ptr), info);

            // 更新统计信息
            Interlocked.Increment(ref m_STotalAllocations);
            Interlocked.Add(ref m_STotalBytesAllocated, info.TotalBytes);

            // 更新峰值统计
            var currentActive = SActiveAllocations.Count;
            var currentMemory = GetCurrentMemoryUsage();

            if (currentActive > m_SPeakActiveAllocations)
                Interlocked.Exchange(ref m_SPeakActiveAllocations, currentActive);

            if (currentMemory > m_SPeakMemoryUsage)
                Interlocked.Exchange(ref m_SPeakMemoryUsage, currentMemory);
        }

        /// <summary>
        ///     跟踪NativeArray释放
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">要释放的数组</param>
        public static unsafe void TrackDeallocation<T>(NativeArray<T> array) where T : struct
        {
            try
            {
                if (!array.IsCreated) return;
                var ptr = array.GetUnsafePtr();
                if (SActiveAllocations.TryRemove(new IntPtr(ptr), out var info))
                {
                    Interlocked.Increment(ref m_STotalDeallocations);
                    Interlocked.Add(ref m_STotalBytesFreed, info.TotalBytes);
                }
            }
            catch
            {
                // 已被 Unity 标记为释放的数组在获取指针时会抛出异常，忽略即可
            }
        }

        /// <summary>
        ///     跟踪NativeList释放
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">要释放的列表</param>
        public static unsafe void TrackDeallocation<T>(NativeList<T> list) where T : unmanaged
        {
            try
            {
                if (!list.IsCreated) return;
                var ptr = list.GetUnsafePtr();
                if (SActiveAllocations.TryRemove((IntPtr)ptr, out var info))
                {
                    Interlocked.Increment(ref m_STotalDeallocations);
                    Interlocked.Add(ref m_STotalBytesFreed, info.TotalBytes);
                }
            }
            catch
            {
                // 已被 Unity 标记为释放的列表在获取指针时会抛出异常，忽略即可
            }
        }

        /// <summary>
        ///     获取当前内存使用量（字节）
        /// </summary>
        public static long GetCurrentMemoryUsage()
        {
            long totalBytes = 0;
            foreach (var kvp in SActiveAllocations)
            {
                totalBytes += kvp.Value.TotalBytes;
            }
            return totalBytes;
        }

        /// <summary>
        ///     获取内存使用统计信息
        /// </summary>
        public static MemoryStats GetMemoryStats() => new MemoryStats
        {
            ActiveAllocations = SActiveAllocations.Count,
            TotalAllocations = m_STotalAllocations,
            TotalDeallocations = m_STotalDeallocations,
            CurrentMemoryUsage = GetCurrentMemoryUsage(),
            TotalBytesAllocated = m_STotalBytesAllocated,
            TotalBytesFreed = m_STotalBytesFreed,
            PeakActiveAllocations = m_SPeakActiveAllocations,
            PeakMemoryUsage = m_SPeakMemoryUsage,
            PotentialLeaks = m_STotalAllocations - m_STotalDeallocations
        };

        /// <summary>
        ///     获取按分配器类型分组的统计信息
        /// </summary>
        public static Dictionary<Allocator, AllocatorStats> GetAllocatorStats()
        {
            var stats = new Dictionary<Allocator, AllocatorStats>();

            foreach (var kvp in SActiveAllocations)
            {
                var allocator = kvp.Value.AllocatorType;
                if (!stats.ContainsKey(allocator))
                {
                    stats[allocator] = new AllocatorStats();
                }

                var currentStats = stats[allocator];
                currentStats.Count++;
                currentStats.TotalBytes += kvp.Value.TotalBytes;
                stats[allocator] = currentStats;
            }

            return stats;
        }

        /// <summary>
        ///     获取长时间未释放的分配（可能的内存泄漏）
        /// </summary>
        /// <param name="thresholdMinutes">阈值时间（分钟）</param>
        /// <returns>可能泄漏的分配信息</returns>
        public static List<LeakInfo> GetPotentialLeaks(double thresholdMinutes = 5.0)
        {
            var leaks = new List<LeakInfo>();
            var threshold = DateTime.Now.AddMinutes(-thresholdMinutes);

            foreach (var kvp in SActiveAllocations)
            {
                var info = kvp.Value;
                if (info.AllocationTime < threshold)
                {
                    leaks.Add(new LeakInfo
                    {
                        TypeName = info.TypeName,
                        ElementCount = info.ElementCount,
                        TotalBytes = info.TotalBytes,
                        AllocatorType = info.AllocatorType,
                        AllocationTime = info.AllocationTime,
                        AgeMinutes = (DateTime.Now - info.AllocationTime).TotalMinutes,
                        StackTrace = info.StackTrace
                    });
                }
            }

            return leaks;
        }

        /// <summary>
        ///     生成内存使用报告
        /// </summary>
        public static string GenerateMemoryReport()
        {
            var stats = GetMemoryStats();
            var allocatorStats = GetAllocatorStats();
            var leaks = GetPotentialLeaks();

            var report = $@"
=== MrPath V2.2 内存使用报告 ===
生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

总体统计:
- 当前活跃分配: {stats.ActiveAllocations}
- 总分配次数: {stats.TotalAllocations}
- 总释放次数: {stats.TotalDeallocations}
- 当前内存使用: {FormatBytes(stats.CurrentMemoryUsage)}
- 峰值活跃分配: {stats.PeakActiveAllocations}
- 峰值内存使用: {FormatBytes(stats.PeakMemoryUsage)}
- 潜在泄漏: {stats.PotentialLeaks}

分配器统计:";

            foreach (var kvp in allocatorStats)
            {
                report += $@"
- {kvp.Key}: {kvp.Value.Count} 个分配, {FormatBytes(kvp.Value.TotalBytes)}";
            }

            if (leaks.Count > 0)
            {
                report += $@"

潜在内存泄漏 ({leaks.Count} 个):";

                foreach (var leak in leaks)
                {
                    report += $@"
- {leak.TypeName}: {leak.ElementCount} 元素, {FormatBytes(leak.TotalBytes)}, 存活 {leak.AgeMinutes:F1} 分钟";
                }
            }

            return report;
        }

        /// <summary>
        ///     重置所有统计信息
        /// </summary>
        public static void ResetStats()
        {
            SActiveAllocations.Clear();
            m_STotalAllocations = 0;
            m_STotalDeallocations = 0;
            m_STotalBytesAllocated = 0;
            m_STotalBytesFreed = 0;
            m_SPeakActiveAllocations = 0;
            m_SPeakMemoryUsage = 0;
        }

        /// <summary>
        ///     格式化字节数为可读字符串
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        /// <summary>
        ///     分配信息结构
        /// </summary>
        private struct AllocationInfo
        {
            public string TypeName;
            public int ElementCount;
            public int ElementSize;
            public Allocator AllocatorType;
            public DateTime AllocationTime;
            public string StackTrace;

            public long TotalBytes => ElementCount * ElementSize;
        }

        /// <summary>
        ///     内存统计信息结构
        /// </summary>
        public struct MemoryStats
        {
            public int ActiveAllocations;
            public long TotalAllocations;
            public long TotalDeallocations;
            public long CurrentMemoryUsage;
            public long TotalBytesAllocated;
            public long TotalBytesFreed;
            public long PeakActiveAllocations;
            public long PeakMemoryUsage;
            public long PotentialLeaks;
        }

        /// <summary>
        ///     分配器统计信息结构
        /// </summary>
        public struct AllocatorStats
        {
            public int Count;
            public long TotalBytes;
        }

        /// <summary>
        ///     内存泄漏信息结构
        /// </summary>
        public struct LeakInfo
        {
            public string TypeName;
            public int ElementCount;
            public long TotalBytes;
            public Allocator AllocatorType;
            public DateTime AllocationTime;
            public double AgeMinutes;
            public string StackTrace;
        }
    }
}
