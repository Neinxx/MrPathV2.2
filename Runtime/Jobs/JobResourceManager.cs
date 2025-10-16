using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// Job资源管理器：统一管理Job相关的NativeArray和其他IDisposable资源
    /// 确保异常安全的资源释放和内存管理
    /// </summary>
    public class JobResourceManager : IDisposable
    {
        private readonly List<IDisposable> _resources = new List<IDisposable>();
        private readonly List<JobHandle> _jobHandles = new List<JobHandle>();
        private bool _disposed = false;

        /// <summary>
        /// 创建并注册一个资源，确保在Dispose时自动释放
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="factory">资源创建工厂方法</param>
        /// <returns>创建的资源</returns>
        public T CreateResource<T>(Func<T> factory) where T : IDisposable
        {
            ThrowIfDisposed();
            
            var resource = factory();
            _resources.Add(resource);
            return resource;
        }

        /// <summary>
        /// 创建SpineData并自动注册管理
        /// </summary>
        public PathJobsUtility.SpineData CreateSpineData(PathSpine spine, Allocator allocator = Allocator.Persistent)
        {
            return CreateResource(() => new PathJobsUtility.SpineData(spine, allocator));
        }

        /// <summary>
        /// 创建ProfileData并自动注册管理
        /// </summary>
        public PathJobsUtility.ProfileData CreateProfileData(PathProfile profile, Allocator allocator = Allocator.Persistent)
        {
            return CreateResource(() => new PathJobsUtility.ProfileData(profile, allocator));
        }

        /// <summary>
        /// 创建RecipeData并自动注册管理
        /// </summary>
        public RecipeData CreateRecipeData(StylizedRoadRecipe recipe, 
            Dictionary<TerrainLayer, int> terrainLayerMap, 
            float roadWorldWidth, 
            Allocator allocator = Allocator.Persistent)
        {
            return CreateResource(() => new RecipeData(recipe, terrainLayerMap, roadWorldWidth, allocator));
        }

        /// <summary>
        /// 创建NativeArray并自动注册管理
        /// </summary>
        public NativeArray<T> CreateNativeArray<T>(int length, Allocator allocator = Allocator.Persistent) 
            where T : struct
        {
            ThrowIfDisposed();
            var owner = MrPathV2.Memory.UnifiedMemory.Instance.RentNativeArray<T>(length, allocator);
            _resources.Add(owner); // 跟踪 IMemoryOwner，方便统一释放
            return owner.Collection;
        }

        /// <summary>
        /// 创建NativeList并自动注册管理
        /// </summary>
        public NativeList<T> CreateNativeList<T>(int initialCapacity, Allocator allocator = Allocator.Persistent) 
            where T : unmanaged
        {
            ThrowIfDisposed();
            var owner = MrPathV2.Memory.UnifiedMemory.Instance.RentNativeList<T>(initialCapacity, allocator);
            _resources.Add(owner);
            return owner.Collection;
        }

        /// <summary>
        /// 注册JobHandle以便统一管理
        /// </summary>
        public void RegisterJobHandle(JobHandle handle)
        {
            ThrowIfDisposed();
            _jobHandles.Add(handle);
        }

        /// <summary>
        /// 等待所有注册的Job完成
        /// </summary>
        public void CompleteAllJobs()
        {
            ThrowIfDisposed();
            
            if (_jobHandles.Count > 0)
            {
                var handleArray = new NativeArray<JobHandle>(_jobHandles.Count, Allocator.Temp);
                for (int i = 0; i < _jobHandles.Count; i++)
                {
                    handleArray[i] = _jobHandles[i];
                }
                
                var combinedHandle = JobHandle.CombineDependencies(handleArray);
                combinedHandle.Complete();
                
                handleArray.Dispose();
                _jobHandles.Clear();
            }
        }

        /// <summary>
        /// 获取当前管理的资源数量
        /// </summary>
        public int ManagedResourceCount => _resources.Count;

        /// <summary>
        /// 获取当前管理的Job数量
        /// </summary>
        public int ManagedJobCount => _jobHandles.Count;

        /// <summary>
        /// 释放所有管理的资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // 首先完成所有Job
                CompleteAllJobs();

                // 然后释放所有资源
                foreach (var resource in _resources)
                {
                    try
                    {
                        resource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"释放资源时发生错误: {ex.Message}");
                    }
                }
            }
            finally
            {
                _resources.Clear();
                _jobHandles.Clear();
                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JobResourceManager));
        }

        /// <summary>
        /// NativeArray包装器，用于统一的IDisposable管理
        /// </summary>
        private class NativeArrayWrapper<T> : IDisposable where T : struct
        {
            private NativeArray<T> _array;
            private bool _disposed = false;

            public NativeArrayWrapper(NativeArray<T> array)
            {
                _array = array;
            }

            public void Dispose()
            {
                if (!_disposed && _array.IsCreated)
                {
                    _array.Dispose();
                    _disposed = true;
                }
            }
        }

        /// <summary>
        /// NativeList包装器，用于统一的IDisposable管理
        /// </summary>
        private class NativeListWrapper<T> : IDisposable where T : unmanaged
        {
            private NativeList<T> _list;
            private bool _disposed = false;

            public NativeListWrapper(NativeList<T> list)
            {
                _list = list;
            }

            public void Dispose()
            {
                if (!_disposed && _list.IsCreated)
                {
                    _list.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}