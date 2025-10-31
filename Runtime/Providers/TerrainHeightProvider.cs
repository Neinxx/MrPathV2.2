// TerrainHeightProvider.cs (智能懒汉版)

using System;
using System.Collections.Generic;
using __temp.MrPathV2.Runtime.Interfaces;
using __temp.MrPathV2.Runtime.Memory;
using Unity.Collections;
using UnityEngine;

// NEW: access UnifiedMemory and MemoryOwner
namespace __temp.MrPathV2.Runtime.Providers
{
    public class TerrainHeightProvider : IHeightProvider
    {

        // 订阅过的 TerrainData 集合，用于在释放或重建缓存时解绑事件
        private readonly HashSet<TerrainData> _mSubscribedTerrainData = new HashSet<TerrainData>();

        private readonly List<TerrainCache> _mTerrainCaches = new List<TerrainCache>();
        private bool _mIsDirty = true; // 初始状态为"脏"，强制在第一次使用时构建缓存
        private bool _mIsInitialized;
        
        // 性能优化：缓存terrain状态，避免频繁检查
        private int _mLastTerrainCount = -1;
        private float _mLastCheckTime = 0f;
        private const float TERRAIN_CHECK_INTERVAL = 1.0f; // 每秒最多检查一次terrain变化

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
        /// 【核心】在需要时才构建或重建缓存 - 性能优化版本
        /// </summary>
        private void EnsureCacheIsUpToDate()
        {
            // 快速路径：如果已初始化且不是脏数据，则检查是否需要定期验证
            if (_mIsInitialized && !_mIsDirty)
            {
                var currentTime = Time.realtimeSinceStartup;
                
                // 如果距离上次检查时间不足间隔，直接返回
                if (currentTime - _mLastCheckTime < TERRAIN_CHECK_INTERVAL)
                    return;
                
                // 更新检查时间
                _mLastCheckTime = currentTime;
                
                // 快速检查：只比较terrain数量
                var activeTerrains = Terrain.activeTerrains;
                var currentTerrainCount = activeTerrains?.Length ?? 0;
                
                if (currentTerrainCount == _mLastTerrainCount)
                    return; // 数量没变，认为没有变化
                
                // 数量变了，标记为脏数据
                _mLastTerrainCount = currentTerrainCount;
                _mIsDirty = true;
            }
            
            // 需要更新缓存
            if (!ShouldUpdateCache())
                return;
        
            // 执行缓存更新流程
            CleanupOldCache();
            InitializeNewCache();
        }
        
        /// <summary>
        /// 检查是否需要更新缓存 - 简化版本
        /// </summary>
        /// <returns>是否需要更新缓存</returns>
        private bool ShouldUpdateCache()
        {
            // 只检查脏标记，terrain变化检查已移至EnsureCacheIsUpToDate
            return _mIsDirty;
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
                // 检查地形对象是否为null
                if (terrain == null)
                {
                    Debug.LogWarning("[TerrainHeightProvider] Skipping null terrain in activeTerrains");
                    continue;
                }
                
                // 检查地形数据是否为null
                if (terrain.terrainData == null)
                {
                    Debug.LogWarning($"[TerrainHeightProvider] Terrain '{terrain.name}' has null terrainData, skipping");
                    continue;
                }
                
                CreateTerrainCache(terrain);
            }
        
            _mIsInitialized = true;
            _mIsDirty = false;
            
            // 更新terrain计数缓存
            _mLastTerrainCount = activeTerrains?.Length ?? 0;
            _mLastCheckTime = Time.realtimeSinceStartup;
        }
        
        /// <summary>
        /// 为单个地形创建缓存
        /// </summary>
        /// <param name="terrain">地形对象</param>
        private void CreateTerrainCache(Terrain terrain)
        {
            // 双重检查以确保安全
            if (terrain == null)
            {
                Debug.LogError("[TerrainHeightProvider] Cannot create cache for null terrain");
                return;
            }
            
            var data = terrain.terrainData;
            if (data == null)
            {
                Debug.LogError($"[TerrainHeightProvider] Cannot create cache for terrain '{terrain.name}' - terrainData is null");
                return;
            }
            
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
            // 直接清理，无需遍历null引用
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
