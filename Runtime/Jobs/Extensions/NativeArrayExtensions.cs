using System;
using Unity.Collections;
using UnityEngine;
using MrPathV2.Memory;

namespace MrPathV2.Extensions
{
    /// <summary>
    /// NativeArray扩展方法，提供内存跟踪和安全释放功能
    /// </summary>
    public static class NativeArrayExtensions
    {
        /// <summary>
        /// 创建带内存跟踪的NativeArray
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="length">数组长度</param>
        /// <param name="allocator">分配器类型</param>
        /// <param name="options">初始化选项</param>
        /// <returns>创建的NativeArray</returns>
        public static NativeArray<T> CreateTracked<T>(int length, Allocator allocator,
            NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : struct
        {
            NativeArray<T> array;
            if (allocator == Allocator.Persistent)
            {
                // 使用统一内存管理器，以便后续集中回收
                var owner = UnifiedMemory.Instance.RentNativeArray<T>(length, allocator, options == NativeArrayOptions.ClearMemory);
                array = owner.Collection;
            }
            else
            {
                array = new NativeArray<T>(length, allocator, options);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MemoryTracker.TrackAllocation(array, allocator);
#endif

            return array;
        }

        /// <summary>
        /// 安全释放NativeArray，包含内存跟踪
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">要释放的数组</param>
        public static void SafeDispose<T>(this NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return;

            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MemoryTracker.TrackDeallocation(array);
#endif

                try
                {
                    array.Dispose();
                }

                catch (Exception disposeEx) when (disposeEx is InvalidOperationException || disposeEx is ObjectDisposedException)
                {
                    // 已被外部释放或已处于无效状态，忽略重复释放错误
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"释放NativeArray时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全释放NativeList，包含内存跟踪
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">要释放的列表</param>
        public static void SafeDispose<T>(this NativeList<T> list) where T : unmanaged
        {
            if (!list.IsCreated) return;

            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MemoryTracker.TrackDeallocation(list);
#endif

                list.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"释放NativeList时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查NativeArray是否有效且已创建
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">要检查的数组</param>
        /// <returns>是否有效</returns>
        public static bool IsValid<T>(this NativeArray<T> array) where T : struct
        {
            return array.IsCreated && array.Length > 0;
        }

        /// <summary>
        /// 检查NativeList是否有效且已创建
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">要检查的列表</param>
        /// <returns>是否有效</returns>
        public static bool IsValid<T>(this NativeList<T> list) where T : unmanaged
        {
            return list.IsCreated && list.Capacity > 0;
        }

        /// <summary>
        /// 安全复制NativeArray内容
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="source">源数组</param>
        /// <param name="destination">目标数组</param>
        /// <param name="sourceIndex">源起始索引</param>
        /// <param name="destIndex">目标起始索引</param>
        /// <param name="length">复制长度</param>
        public static void SafeCopyTo<T>(this NativeArray<T> source, NativeArray<T> destination,
            int sourceIndex = 0, int destIndex = 0, int length = -1) where T : struct
        {
            if (!source.IsValid() || !destination.IsValid())
            {
                Debug.LogError("源数组或目标数组无效");
                return;
            }

            if (length == -1)
                length = Mathf.Min(source.Length - sourceIndex, destination.Length - destIndex);

            if (sourceIndex + length > source.Length || destIndex + length > destination.Length)
            {
                Debug.LogError("复制范围超出数组边界");
                return;
            }

            try
            {
                NativeArray<T>.Copy(source, sourceIndex, destination, destIndex, length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"复制NativeArray时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建NativeArray的安全副本
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="source">源数组</param>
        /// <param name="allocator">分配器类型</param>
        /// <returns>副本数组</returns>
        public static NativeArray<T> CreateCopy<T>(this NativeArray<T> source, Allocator allocator) where T : struct
        {
            if (!source.IsValid())
            {
                Debug.LogError("源数组无效，无法创建副本");
                return default;
            }

            var copy = CreateTracked<T>(source.Length, allocator);
            source.SafeCopyTo(copy);
            return copy;
        }

        /// <summary>
        /// 重新调整NativeArray大小（创建新数组并复制数据）
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">原数组</param>
        /// <param name="newLength">新长度</param>
        /// <param name="allocator">分配器类型</param>
        /// <param name="preserveData">是否保留原数据</param>
        /// <returns>新的调整大小后的数组</returns>
        public static NativeArray<T> Resize<T>(this NativeArray<T> array, int newLength,
            Allocator allocator, bool preserveData = true) where T : struct
        {
            var newArray = CreateTracked<T>(newLength, allocator);

            if (preserveData && array.IsValid())
            {
                int copyLength = Mathf.Min(array.Length, newLength);
                array.SafeCopyTo(newArray, 0, 0, copyLength);
            }

            return newArray;
        }

        /// <summary>
        /// 获取NativeArray的内存使用量（字节）
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">数组</param>
        /// <returns>内存使用量</returns>
        public static long GetMemoryUsage<T>(this NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated) return 0;
            return array.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
        }

        /// <summary>
        /// 获取NativeList的内存使用量（字节）
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="list">列表</param>
        /// <returns>内存使用量</returns>
        public static long GetMemoryUsage<T>(this NativeList<T> list) where T : unmanaged
        {
            if (!list.IsCreated) return 0;
            return list.Capacity * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
        }

        /// <summary>
        /// 填充NativeArray的所有元素
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">数组</param>
        /// <param name="value">填充值</param>
        public static void Fill<T>(this NativeArray<T> array, T value) where T : struct
        {
            if (!array.IsValid()) return;

            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }

        /// <summary>
        /// 安全获取NativeArray元素，带边界检查
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">数组</param>
        /// <param name="index">索引</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>元素值或默认值</returns>
        public static T SafeGet<T>(this NativeArray<T> array, int index, T defaultValue = default) where T : struct
        {
            if (!array.IsValid() || index < 0 || index >= array.Length)
                return defaultValue;

            return array[index];
        }

        /// <summary>
        /// 安全设置NativeArray元素，带边界检查
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        /// <param name="array">数组</param>
        /// <param name="index">索引</param>
        /// <param name="value">值</param>
        /// <returns>是否设置成功</returns>
        public static bool SafeSet<T>(this NativeArray<T> array, int index, T value) where T : struct
        {
            if (!array.IsValid() || index < 0 || index >= array.Length)
                return false;

            array[index] = value;
            return true;
        }
    }
}