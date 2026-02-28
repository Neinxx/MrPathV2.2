using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 内存跟踪器：监控NativeArray分配和释放，检测内存泄漏
    /// 提供详细的内存使用统计和调试信息
    /// </summary>
    public static class MemoryTracker
    {
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> s_ActiveAllocations = 
            new ConcurrentDictionary<IntPtr, AllocationInfo>();
        
        private static long s_TotalAllocations = 0;
        private static long s_TotalDeallocations = 0;
        private static long s_TotalBytesAllocated = 0;
        private static long s_TotalBytesFreed = 0;
        private static long s_PeakActiveAllocations = 0;
        private static long s_PeakMemoryUsage = 0;

        /// <summary>
        /// 分配信息结构
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
        /// 跟踪NativeArray分配
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
                ElementSize = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>(),
                AllocatorType = allocator,
                AllocationTime = DateTime.Now,
                StackTrace = Application.isEditor ? Environment.StackTrace : "N/A"
            };

            var ptr = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(array);
            s_ActiveAllocations.TryAdd(new System.IntPtr(ptr), info);

            // 更新统计信息
            Interlocked.Increment(ref s_TotalAllocations);
            Interlocked.Add(ref s_TotalBytesAllocated, info.TotalBytes);

            // 更新峰值统计
            var currentActive = s_ActiveAllocations.Count;
            var currentMemory = GetCurrentMemoryUsage();
            
            if (currentActive > s_PeakActiveAllocations)
                Interlocked.Exchange(ref s_PeakActiveAllocations, currentActive);
                
            if (currentMemory > s_PeakMemoryUsage)
                Interlocked.Exchange(ref s_PeakMemoryUsage, currentMemory);
        }

        /// <summary>
        /// 跟踪NativeArray释放
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">要释放的数组</param>
        public static unsafe void TrackDeallocation<T>(NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return;

            var ptr = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(array);
            if (s_ActiveAllocations.TryRemove(new IntPtr(ptr), out var info))
            {
                Interlocked.Increment(ref s_TotalDeallocations);
                Interlocked.Add(ref s_TotalBytesFreed, info.TotalBytes);
            }
        }

        /// <summary>
        /// 跟踪NativeList分配
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">分配的列表</param>
        /// <param name="allocator">分配器类型</param>
        public static unsafe void  TrackAllocation<T>(NativeList<T> list, Allocator allocator) where T : unmanaged
        {
            if (!list.IsCreated) return;

            var info = new AllocationInfo
            {
                TypeName = $"NativeList<{typeof(T).Name}>",
                ElementCount = list.Capacity,
                ElementSize = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>(),
                AllocatorType = allocator,
                AllocationTime = DateTime.Now,
                StackTrace = Application.isEditor ? Environment.StackTrace : "N/A"
            };

            var ptr = Unity.Collections.LowLevel.Unsafe.NativeListUnsafeUtility.GetUnsafePtr(list);
            s_ActiveAllocations.TryAdd((IntPtr)ptr, info);

            Interlocked.Increment(ref s_TotalAllocations);
            Interlocked.Add(ref s_TotalBytesAllocated, info.TotalBytes);
        }

        /// <summary>
        /// 跟踪NativeList释放
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">要释放的列表</param>
        public static unsafe void TrackDeallocation<T>(NativeList<T> list) where T : unmanaged
        {
            if (!list.IsCreated) return;

            var ptr = Unity.Collections.LowLevel.Unsafe.NativeListUnsafeUtility.GetUnsafePtr(list);
            if (s_ActiveAllocations.TryRemove((IntPtr)ptr, out var info))
            {
                Interlocked.Increment(ref s_TotalDeallocations);
                Interlocked.Add(ref s_TotalBytesFreed, info.TotalBytes);
            }
        }

        /// <summary>
        /// 获取当前活跃分配数量
        /// </summary>
        public static int ActiveAllocationCount => s_ActiveAllocations.Count;

        /// <summary>
        /// 获取当前内存使用量（字节）
        /// </summary>
        public static long GetCurrentMemoryUsage()
        {
            long totalBytes = 0;
            foreach (var kvp in s_ActiveAllocations)
            {
                totalBytes += kvp.Value.TotalBytes;
            }
            return totalBytes;
        }

        /// <summary>
        /// 获取内存使用统计信息
        /// </summary>
        public static MemoryStats GetMemoryStats()
        {
            return new MemoryStats
            {
                ActiveAllocations = s_ActiveAllocations.Count,
                TotalAllocations = s_TotalAllocations,
                TotalDeallocations = s_TotalDeallocations,
                CurrentMemoryUsage = GetCurrentMemoryUsage(),
                TotalBytesAllocated = s_TotalBytesAllocated,
                TotalBytesFreed = s_TotalBytesFreed,
                PeakActiveAllocations = s_PeakActiveAllocations,
                PeakMemoryUsage = s_PeakMemoryUsage,
                PotentialLeaks = s_TotalAllocations - s_TotalDeallocations
            };
        }

        /// <summary>
        /// 获取按分配器类型分组的统计信息
        /// </summary>
        public static Dictionary<Allocator, AllocatorStats> GetAllocatorStats()
        {
            var stats = new Dictionary<Allocator, AllocatorStats>();
            
            foreach (var kvp in s_ActiveAllocations)
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
        /// 获取长时间未释放的分配（可能的内存泄漏）
        /// </summary>
        /// <param name="thresholdMinutes">阈值时间（分钟）</param>
        /// <returns>可能泄漏的分配信息</returns>
        public static List<LeakInfo> GetPotentialLeaks(double thresholdMinutes = 5.0)
        {
            var leaks = new List<LeakInfo>();
            var threshold = DateTime.Now.AddMinutes(-thresholdMinutes);
            
            foreach (var kvp in s_ActiveAllocations)
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
        /// 生成内存使用报告
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
        /// 重置所有统计信息
        /// </summary>
        public static void ResetStats()
        {
            s_ActiveAllocations.Clear();
            s_TotalAllocations = 0;
            s_TotalDeallocations = 0;
            s_TotalBytesAllocated = 0;
            s_TotalBytesFreed = 0;
            s_PeakActiveAllocations = 0;
            s_PeakMemoryUsage = 0;
        }

        /// <summary>
        /// 格式化字节数为可读字符串
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        /// <summary>
        /// 内存统计信息结构
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
        /// 分配器统计信息结构
        /// </summary>
        public struct AllocatorStats
        {
            public int Count;
            public long TotalBytes;
        }

        /// <summary>
        /// 内存泄漏信息结构
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