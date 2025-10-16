using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// Native Collection内存管理器，提供统一的内存分配、跟踪和释放机制
    /// </summary>
    public class NativeCollectionManager : IDisposable
    {
        private readonly List<object> _trackedCollections = new List<object>();
        private readonly Dictionary<string, int> _allocationStats = new Dictionary<string, int>();
        private bool _disposed = false;

        /// <summary>
        /// 创建并跟踪一个NativeArray
        /// </summary>
        /// <summary>
        /// 创建并跟踪一个NativeArray（旧版兼容）。
        /// 新实现代理到 UnifiedMemoryManager，内部仍记录原生数组引用，便于旧代码无缝迁移。
        /// </summary>
        public NativeArray<T> CreateNativeArray<T>(int length, Allocator allocator, string tag = null) where T : struct
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeCollectionManager));

            var owner = MrPathV2.Memory.UnifiedMemory.Instance.RentNativeArray<T>(length, allocator, false, tag);
            var array = owner.Collection;

            _trackedCollections.Add(owner); // 跟踪包装器以便统一释放
            
            string key = tag ?? typeof(T).Name;
            _allocationStats[key] = _allocationStats.GetValueOrDefault(key, 0) + 1;
            
            return array;
        }

        /// <summary>
        /// 创建并跟踪一个NativeList
        /// </summary>
        /// 创建并跟踪一个NativeList（旧版兼容）。
        /// 新实现代理到 UnifiedMemoryManager。
        public NativeList<T> CreateNativeList<T>(int initialCapacity, Allocator allocator, string tag = null) where T : unmanaged
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeCollectionManager));

            var owner = MrPathV2.Memory.UnifiedMemory.Instance.RentNativeList<T>(initialCapacity, allocator, tag);
            var list = owner.Collection;

            _trackedCollections.Add(owner);

            string key = tag ?? typeof(T).Name;
            _allocationStats[key] = _allocationStats.GetValueOrDefault(key, 0) + 1;

            return list;
        }

        /// <summary>
        /// 手动释放指定的Native Collection
        /// </summary>
        // 手动释放指定的Native Collection
        // 仅处理实现 IDisposable 的对象；否则记录错误。
        public void DisposeCollection(object collection)
        {
            if (collection == null) return;
        
            if (collection is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to dispose native collection: {ex.Message}");
                }
                finally
                {
                    _trackedCollections.Remove(collection);
                }
            }
            else
            {
                Debug.LogError($"Collection of type {collection.GetType().Name} does not implement IDisposable. Please ensure all tracked collections implement IDisposable and are managed by UnifiedMemory.");
            }
        }

        /// <summary>
        /// 获取当前分配统计信息
        /// </summary>
        public Dictionary<string, int> GetAllocationStats()
        {
            return new Dictionary<string, int>(_allocationStats);
        }

        /// <summary>
        /// 获取当前活跃的Native Collection数量
        /// </summary>
        public int GetActiveCollectionCount()
        {
            int count = 0;
            for (int i = _trackedCollections.Count - 1; i >= 0; i--)
            {
                var collection = _trackedCollections[i];
                if (collection == null || !IsCollectionCreated(collection))
                {
                    _trackedCollections.RemoveAt(i);
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 检查Native Collection是否已创建
        /// </summary>
        private bool IsCollectionCreated(object collection)
        {
            if (collection == null) return false;

            var isCreatedProperty = collection.GetType().GetProperty("IsCreated");
            if (isCreatedProperty != null)
            {
                return (bool)isCreatedProperty.GetValue(collection);
            }
            
            return false;
        }

        /// <summary>
        /// 检查是否有潜在的内存泄漏
        /// </summary>
        public bool HasPotentialLeaks()
        {
            return GetActiveCollectionCount() > 0;
        }

        /// <summary>
        /// 强制清理所有未释放的Native Collections
        /// </summary>
        public void ForceCleanup()
        {
            if (_trackedCollections == null || _trackedCollections.Count == 0)
            {
                return;
            }

            // 创建一个副本来避免在迭代过程中修改集合
            var collectionsToDispose = new List<object>(_trackedCollections);
            
            for (int i = collectionsToDispose.Count - 1; i >= 0; i--)
            {
                var collection = collectionsToDispose[i];
                
                // 跳过null引用
                if (collection == null)
                {
                    continue;
                }

                // 检查集合是否仍然有效
                if (!IsCollectionCreated(collection))
                {
                    // 如果集合已经无效，直接从跟踪列表中移除
                    _trackedCollections.Remove(collection);
                    continue;
                }

                try
                {
                    DisposeCollection(collection);
                }
                catch (Exception ex)
                {
                    // 记录详细的异常信息，但继续处理其他集合
                    Debug.LogError($"Failed to dispose native collection at index {i}: {ex.Message}\nCollection Type: {collection?.GetType()?.Name ?? "Unknown"}\nStackTrace: {ex.StackTrace}");
                    
                    // 即使disposal失败，也要从跟踪列表中移除，避免重复尝试
                    _trackedCollections.Remove(collection);
                }
            }
            
            // 最后清理跟踪列表
            _trackedCollections.Clear();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                ForceCleanup();
                _allocationStats.Clear();
                _disposed = true;
            }
        }

        ~NativeCollectionManager()
        {
            if (!_disposed)
            {
                Debug.LogWarning("NativeCollectionManager was not properly disposed!");
                Dispose();
            }
        }
    }

    /// <summary>
    /// Native Collection管理器的静态访问点
    /// </summary>
    public static class NativeCollections
    {
        private static NativeCollectionManager _instance;
        
        public static NativeCollectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new NativeCollectionManager();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 在应用程序退出时清理资源
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Application.quitting += () =>
            {
                _instance?.Dispose();
                _instance = null;
            };
        }
    }
}