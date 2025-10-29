// TerrainHeightProvider.cs (智能懒汉版)

using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Memory;
using Unity.Collections;
using UnityEngine;

// NEW: access UnifiedMemory and MemoryOwner
namespace MrPathV2.Runtime.Providers
{
    public class TerrainHeightProvider : IHeightProvider
    {

        // 订阅过的 TerrainData 集合，用于在释放或重建缓存时解绑事件
        private readonly HashSet<TerrainData> _mSubscribedTerrainData = new HashSet<TerrainData>();

        private readonly List<TerrainCache> _mTerrainCaches = new List<TerrainCache>();
        private bool _mIsDirty = true; // 初始状态为"脏"，强制在第一次使用时构建缓存
        private bool _mIsInitialized;

        /// <summary>
        ///     【新增】从外部标记缓存为“过时”的公共方法
        /// </summary>
        public void MarkAsDirty()
        {
            _mIsDirty = true;
        }

        public float GetHeight(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!_mIsInitialized) return worldPos.y;

            var cache = FindCacheForPosition(worldPos);
            if (cache == null) return worldPos.y;

            var normX = Mathf.Clamp01((worldPos.x - cache.Value.Position.x) / cache.Value.Size.x);
            var normZ = Mathf.Clamp01((worldPos.z - cache.Value.Position.z) / cache.Value.Size.z);

            var hX = Mathf.FloorToInt(normX * (cache.Value.Resolution - 1));
            var hY = Mathf.FloorToInt(normZ * (cache.Value.Resolution - 1));

            var h = cache.Value.Heights[hY * cache.Value.Resolution + hX];
            return h * cache.Value.Size.y + cache.Value.Position.y;
        }

        public Vector3 GetNormal(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!_mIsInitialized) return Vector3.up;

            var cache = FindCacheForPosition(worldPos);
            if (cache == null) return Vector3.up;

            var normX = (worldPos.x - cache.Value.Position.x) / cache.Value.Size.x;
            var normZ = (worldPos.z - cache.Value.Position.z) / cache.Value.Size.z;

            return cache.Value.Data.GetInterpolatedNormal(normX, normZ);
        }

        public void Dispose()
        {
            UnsubscribeAllTerrainData();
            foreach (var cache in _mTerrainCaches)
            {
                cache.Dispose();
            }
            _mTerrainCaches.Clear();
            // 不再需要释放arrayPool
        }

        /// <summary>
        ///     【核心】在需要时才构建或重建缓存
        /// </summary>
        private void EnsureCacheIsUpToDate()
        {
            // 提前返回：如果不需要更新缓存则直接返回
            if (!ShouldUpdateCache())
                return;
        
            // 执行缓存更新流程
            CleanupOldCache();
            InitializeNewCache();
        }
        
        /// <summary>
        /// 检查是否需要更新缓存
        /// </summary>
        /// <returns>是否需要更新缓存</returns>
        private bool ShouldUpdateCache()
        {
            // 如果当前标记为脏数据，则需要更新
            if (_mIsDirty)
                return true;
        
            // 检查地形集合是否发生变化
            return HasTerrainSetChanged();
        }
        
        /// <summary>
        /// 检查地形集合是否发生变化（新增/删除/替换）
        /// </summary>
        /// <returns>地形集合是否发生变化</returns>
        private bool HasTerrainSetChanged()
        {
            var activeTerrains = Terrain.activeTerrains;
            
            // 如果当前没有地形而缓存中有地形，或者当前有地形而缓存中没有地形
            if (activeTerrains == null || activeTerrains.Length == 0)
                return _mTerrainCaches.Count > 0;
        
            // 如果地形数量不一致
            if (activeTerrains.Length != _mTerrainCaches.Count)
                return true;
        
            // 检查每个地形的数据是否一致
            for (var i = 0; i < activeTerrains.Length; i++)
            {
                if (activeTerrains[i].terrainData != _mTerrainCaches[i].Data)
                {
                    return true;
                }
            }
        
            return false;
        }
        
        /// <summary>
        /// 清理旧的缓存数据
        /// </summary>
        private void CleanupOldCache()
        {
            UnsubscribeAllTerrainData();
            
            foreach (var cache in _mTerrainCaches)
            {
                cache.Dispose();
            }
            
            _mTerrainCaches.Clear();
        }
        
        /// <summary>
        /// 初始化新的缓存数据
        /// </summary>
        private void InitializeNewCache()
        {
            var activeTerrains = Terrain.activeTerrains;
            
            // 如果没有活动地形，标记为未初始化并返回
            if (activeTerrains == null || activeTerrains.Length == 0)
            {
                _mIsInitialized = false;
                _mIsDirty = false;
                return;
            }
        
            // 为每个地形创建缓存
            foreach (var terrain in activeTerrains)
            {
                CreateTerrainCache(terrain);
            }
        
            _mIsInitialized = true;
            _mIsDirty = false;
        }
        
        /// <summary>
        /// 为单个地形创建缓存
        /// </summary>
        /// <param name="terrain">地形对象</param>
        private void CreateTerrainCache(Terrain terrain)
        {
            var data = terrain.terrainData;
            var position = terrain.GetPosition();
            var size = data.size;
        
            var heights2D = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            var owner = UnifiedMemory.Instance.RentNativeArray<float>(heights2D.Length, Allocator.Persistent);
            var heightsNative = owner.Collection;
        
            for (var y = 0; y < data.heightmapResolution; y++)
            {
                for (var x = 0; x < data.heightmapResolution; x++)
                {
                    heightsNative[y * data.heightmapResolution + x] = heights2D[y, x];
                }
            }
        
            _mTerrainCaches.Add(new TerrainCache
            {
                Terrain = terrain,
                Data = data,
                Bounds = new Rect(position.x, position.z, size.x, size.z),
                HeightsOwner = owner,
                Resolution = data.heightmapResolution,
                Position = position,
                Size = size
            });
        
            // 订阅地形数据的高度变更事件
            SubscribeTerrainData(data);
        }

        private TerrainCache? FindCacheForPosition(Vector3 worldPos)
        {
            foreach (var cache in _mTerrainCaches)
            {
                if (cache.Bounds.Contains(new Vector2(worldPos.x, worldPos.z)))
                {
                    return cache;
                }
            }
            return null;
        }

        // 事件与订阅管理
        private void SubscribeTerrainData(TerrainData data)
        {
            if (!data || !_mSubscribedTerrainData.Add(data)) return;
            // 某些旧版 Unity 不包含 TerrainData.heightmapChanged 事件，使用条件编译兼容
        }

        private void UnsubscribeAllTerrainData()
        {
            if (_mSubscribedTerrainData.Count == 0) return;
            foreach (var unused in _mSubscribedTerrainData.Where(data => !data)) { }
            _mSubscribedTerrainData.Clear();
        }

        private struct TerrainCache : IDisposable
        {
            public Terrain Terrain;
            public TerrainData Data;
            public Rect Bounds;
            // REPLACED: NativeArray<float> heights;
            public MemoryOwner<NativeArray<float>> HeightsOwner; // 持有包装器以便安全释放
            public readonly NativeArray<float> Heights => HeightsOwner.Collection; // 便捷访问器
            public int Resolution;
            public Vector3 Position;
            public Vector3 Size;

            public void Dispose()
            {
                HeightsOwner?.Dispose(); // 统一释放
            }
        }
    }
}
