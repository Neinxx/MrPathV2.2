using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using UnityEngine;

namespace MrPathV2.Runtime.Memory
{
    /// <summary>
    ///     表示一个拥有并负责释放底层 NativeCollection 资源的对象。
    /// </summary>
    /// <typeparam name="TCollection">NativeCollection 类型 (NativeArray / NativeList)</typeparam>
    public interface IMemoryOwner<TCollection> : IDisposable
    {
        /// <summary>
        ///     获取底层 NativeCollection。
        /// </summary>
        TCollection Collection { get; }
    }

    /// <summary>
    ///     统一的内存管理器，整合了分配、释放、统计与泄漏检测。
    ///     设计目标：
    ///     1. 编译时泛型约束，无需反射即可安全释放。
    ///     2. 通过池化减少频繁分配；Editor/Development 构建下启用详细统计。
    ///     3. 可扩展：后续支持 NativeQueue / NativeHashMap 等类型。
    /// </summary>
    public sealed class UnifiedMemoryManager : IDisposable
    {

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Dictionary<string, int> _allocStats = new Dictionary<string, int>();
#endif

        private readonly List<IDisposable> _tracked = new List<IDisposable>(256);
        private bool _disposed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public IReadOnlyDictionary<string, int> AllocationStats => _allocStats;
#endif

        public void Dispose()
        {
            if (_disposed) return;
            ForceCleanup();
            _disposed = true;
        }

        /// <summary>
        ///     创建 NativeArray 并返回包装器，调用方持有 IMemoryOwner 接口即可。
        /// </summary>
        public MemoryOwner<NativeArray<T>> RentNativeArray<T>(int length, Allocator allocator, bool clear = false, string tag = null) where T : struct
        {
            var array = new NativeArray<T>(length, allocator, clear ? NativeArrayOptions.ClearMemory : NativeArrayOptions.UninitializedMemory);
            var owner = new MemoryOwner<NativeArray<T>>(array, ReleaseNativeArray);
            Register(owner, tag ?? $"NativeArray<{typeof(T).Name}>");
            return owner;
        }

        /// <summary>
        ///     创建 NativeList 并返回包装器。
        /// </summary>
        public MemoryOwner<NativeList<T>> RentNativeList<T>(int capacity, Allocator allocator, string tag = null) where T : unmanaged
        {
            var list = new NativeList<T>(capacity, allocator);
            var owner = new MemoryOwner<NativeList<T>>(list, ReleaseNativeList);
            Register(owner, tag ?? $"NativeList<{typeof(T).Name}>");
            return owner;
        }

        private void Register(IDisposable owner, string key)
        {
            _tracked.Add(owner);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _allocStats[key] = _allocStats.GetValueOrDefault(key, 0) + 1;
            // try { Debug.Log($"[UnifiedMemoryManager] Tracked allocation: {key} (count={_allocStats[key]})"); } catch { }
#endif
        }

        /// <summary>
        ///     主动释放所有仍被跟踪的资源。
        /// </summary>
        public void ForceCleanup()
        {
            for (var i = _tracked.Count - 1; i >= 0; i--)
            {
                try
                {
                    _tracked[i]?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"UnifiedMemoryManager cleanup error: {ex.Message}");
                }
            }
            _tracked.Clear();
        }

        ~UnifiedMemoryManager()
        {
            if (!_disposed)
            {
                Debug.LogWarning("UnifiedMemoryManager finalizer detected missing Dispose call.");
                Dispose();
            }
        }
        #region Singleton

        private static UnifiedMemoryManager _instance;
        public static UnifiedMemoryManager Instance => _instance ??= new UnifiedMemoryManager();
        private UnifiedMemoryManager() { }

        #endregion

        #region Release helpers

        private static void ReleaseNativeArray<T>(ref NativeArray<T> array) where T : struct
        {
            try
            {
                var canDispose = false;
                try
                {
                    canDispose = array.IsCreated;
                }
                catch
                { /* already deallocated; treat as disposed */
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (canDispose)
                {
                    try
                    {
                        MemoryTracker.TrackDeallocation(array);
                    }
                    catch
                    { /* tracking best-effort only */
                    }
                }
#endif
                if (canDispose)
                {
                    try
                    {
                        array.Dispose();
                    }
                    catch
                    { /* already disposed */
                    }
                }
            }
            finally
            {
                array = default;
            }
        }

        private static void ReleaseNativeList<T>(ref NativeList<T> list) where T : unmanaged
        {
            try
            {
                var canDispose = false;
                try
                {
                    canDispose = list.IsCreated;
                }
                catch
                { /* already deallocated */
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (canDispose)
                {
                    try
                    {
                        MemoryTracker.TrackDeallocation(list);
                    }
                    catch
                    { /* tracking best-effort only */
                    }
                }
#endif
                if (canDispose)
                {
                    try
                    {
                        list.Dispose();
                    }
                    catch
                    { /* already disposed */
                    }
                }
            }
            finally
            {
                list = default;
            }
        }

        #endregion
    }

    /// <summary>
    ///     泛型包装器，确保释放逻辑在 IMemoryOwner.Dispose 中执行。
    /// </summary>
    public sealed class MemoryOwner<TCollection> : IMemoryOwner<TCollection>
    {
        private readonly ActionRef<TCollection> _release;
        private TCollection _collection;
        private bool _disposed;

        public MemoryOwner(TCollection collection, ActionRef<TCollection> release)
        {
            _collection = collection;
            _release = release;
        }

        public TCollection Collection => _collection;

        public void Dispose()
        {
            if (_disposed) return;
            _release(ref _collection);
            _disposed = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[UnifiedMemoryManager] MemoryOwner<{typeof(TCollection).Name}> disposed."); } catch { }
#endif
        }
    }

    /// <summary>
    ///     允许传递 ref 参数的委托，用于泛型释放回调。
    /// </summary>
    public delegate void ActionRef<T>(ref T value);
}
