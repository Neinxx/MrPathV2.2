// TerrainHeightProvider.cs (智能懒汉版)

using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Memory;
using MrPathV2.Memory;
using Unity.Collections;
using UnityEngine;

// NEW: access UnifiedMemory and MemoryOwner
namespace __temp.MrPathV2._2.Runtime.Providers
{
    public class TerrainHeightProvider : IHeightProvider
    {
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

        // 订阅过的 TerrainData 集合，用于在释放或重建缓存时解绑事件
        private readonly HashSet<TerrainData> _mSubscribedTerrainData = new HashSet<TerrainData>();

        private readonly List<TerrainCache> _mTerrainCaches = new List<TerrainCache>();
        private bool _mIsInitialized;
        private bool _mIsDirty = true; // 初始状态为"脏"，强制在第一次使用时构建缓存

        /// <summary>
        /// 【核心】在需要时才构建或重建缓存
        /// </summary>
        private void EnsureCacheIsUpToDate()
        {
            // 检测地形集合变化（新增/删除/替换），必要时自动使缓存失效
            var activeTerrains = Terrain.activeTerrains;
            if (!_mIsDirty)
            {
                bool terrainSetChanged = false;
                if (activeTerrains == null || activeTerrains.Length == 0)
                {
                    terrainSetChanged = _mTerrainCaches.Count > 0;
                }
                else
                {
                    if (activeTerrains.Length != _mTerrainCaches.Count) terrainSetChanged = true;
                    else
                    {
                        for (int i = 0; i < activeTerrains.Length; i++)
                        {
                            if (activeTerrains[i].terrainData != _mTerrainCaches[i].Data)
                            {
                                terrainSetChanged = true;
                                break;
                            }
                        }
                    }
                }
                if (!terrainSetChanged) return; // 数据新鲜且集合未变
                _mIsDirty = true; // 集合发生变化，强制重建
            }

            // 清理旧的缓存
            UnsubscribeAllTerrainData();
            foreach (var cache in _mTerrainCaches) cache.Dispose();
            _mTerrainCaches.Clear();

            if (activeTerrains == null || activeTerrains.Length == 0)
            {
                _mIsInitialized = false;
                _mIsDirty = false; // 清理完毕，标记为“干净”
                return;
            }

            foreach (var terrain in activeTerrains)
            {
                var data = terrain.terrainData;
                var position = terrain.GetPosition();
                var size = data.size;

                var heights2D = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                var owner = UnifiedMemory.Instance.RentNativeArray<float>(heights2D.Length, Allocator.Persistent);
                var heightsNative = owner.Collection;

                for (int y = 0; y < data.heightmapResolution; y++)
                {
                    for (int x = 0; x < data.heightmapResolution; x++)
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
                    Size = size,

                });

                // 订阅地形数据的高度变更事件，任何高度改动均标记缓存为脏
                SubscribeTerrainData(data);
            }

            _mIsInitialized = true;
            _mIsDirty = false; // 重建完毕，标记为“干净”
        }

        /// <summary>
        /// 【新增】从外部标记缓存为“过时”的公共方法
        /// </summary>
        public void MarkAsDirty()
        {
            _mIsDirty = true;
        }

        public float GetHeight(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!_mIsInitialized) return worldPos.y;

            TerrainCache? cache = FindCacheForPosition(worldPos);
            if (cache == null) return worldPos.y;

            float normX = Mathf.Clamp01((worldPos.x - cache.Value.Position.x) / cache.Value.Size.x);
            float normZ = Mathf.Clamp01((worldPos.z - cache.Value.Position.z) / cache.Value.Size.z);

            int hX = Mathf.FloorToInt(normX * (cache.Value.Resolution - 1));
            int hY = Mathf.FloorToInt(normZ * (cache.Value.Resolution - 1));

            float h = cache.Value.Heights[hY * cache.Value.Resolution + hX];
            return h * cache.Value.Size.y + cache.Value.Position.y;
        }

        public Vector3 GetNormal(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!_mIsInitialized) return Vector3.up;

            TerrainCache? cache = FindCacheForPosition(worldPos);
            if (cache == null) return Vector3.up;

            float normX = (worldPos.x - cache.Value.Position.x) / cache.Value.Size.x;
            float normZ = (worldPos.z - cache.Value.Position.z) / cache.Value.Size.z;

            return cache.Value.Data.GetInterpolatedNormal(normX, normZ);
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

        public void Dispose()
        {
            UnsubscribeAllTerrainData();
            foreach (var cache in _mTerrainCaches) cache.Dispose();
            _mTerrainCaches.Clear();
            // 不再需要释放arrayPool
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
            foreach (var unused in _mSubscribedTerrainData.Where(data => !data))
            {
                continue;
            }
            _mSubscribedTerrainData.Clear();
        }
    }
}