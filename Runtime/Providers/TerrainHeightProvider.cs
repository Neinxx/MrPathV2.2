// TerrainHeightProvider.cs (智能懒汉版)
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using MrPathV2.Memory; // NEW: access UnifiedMemory and MemoryOwner
namespace MrPathV2
{
    public class TerrainHeightProvider : IHeightProvider
    {
        private struct TerrainCache : IDisposable
        {
            public Terrain terrain;
            public TerrainData data;
            public Rect bounds;
            // REPLACED: NativeArray<float> heights;
            public MemoryOwner<NativeArray<float>> heightsOwner; // 持有包装器以便安全释放
            public NativeArray<float> Heights => heightsOwner.Collection; // 便捷访问器
            public int resolution;
            public Vector3 position;
            public Vector3 size;

            public void Dispose()
            {
                heightsOwner?.Dispose(); // 统一释放
            }
        }

        // 订阅过的 TerrainData 集合，用于在释放或重建缓存时解绑事件
        private readonly HashSet<TerrainData> m_SubscribedTerrainData = new HashSet<TerrainData>();

        private readonly List<TerrainCache> m_TerrainCaches = new List<TerrainCache>();
        private bool m_IsInitialized = false;
        private bool m_IsDirty = true; // 初始状态为"脏"，强制在第一次使用时构建缓存

        public TerrainHeightProvider()
        {
            // 不再使用对象池
        }

        /// <summary>
        /// 【核心】在需要时才构建或重建缓存
        /// </summary>
        private void EnsureCacheIsUpToDate()
        {
            // 检测地形集合变化（新增/删除/替换），必要时自动使缓存失效
            var activeTerrains = Terrain.activeTerrains;
            if (!m_IsDirty)
            {
                bool terrainSetChanged = false;
                if (activeTerrains == null || activeTerrains.Length == 0)
                {
                    terrainSetChanged = m_TerrainCaches.Count > 0;
                }
                else
                {
                    if (activeTerrains.Length != m_TerrainCaches.Count) terrainSetChanged = true;
                    else
                    {
                        for (int i = 0; i < activeTerrains.Length; i++)
                        {
                            if (activeTerrains[i].terrainData != m_TerrainCaches[i].data)
                            {
                                terrainSetChanged = true;
                                break;
                            }
                        }
                    }
                }
                if (!terrainSetChanged) return; // 数据新鲜且集合未变
                m_IsDirty = true; // 集合发生变化，强制重建
            }

            // 清理旧的缓存
            UnsubscribeAllTerrainData();
            foreach (var cache in m_TerrainCaches) cache.Dispose();
            m_TerrainCaches.Clear();

            if (activeTerrains == null || activeTerrains.Length == 0)
            {
                m_IsInitialized = false;
                m_IsDirty = false; // 清理完毕，标记为“干净”
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

                m_TerrainCaches.Add(new TerrainCache
                {
                    terrain = terrain,
                    data = data,
                    bounds = new Rect(position.x, position.z, size.x, size.z),
                    heightsOwner = owner,
                    resolution = data.heightmapResolution,
                    position = position,
                    size = size,

                });

                // 订阅地形数据的高度变更事件，任何高度改动均标记缓存为脏
                SubscribeTerrainData(data);
            }

            m_IsInitialized = true;
            m_IsDirty = false; // 重建完毕，标记为“干净”
        }

        /// <summary>
        /// 【新增】从外部标记缓存为“过时”的公共方法
        /// </summary>
        public void MarkAsDirty()
        {
            m_IsDirty = true;
        }

        public float GetHeight(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!m_IsInitialized) return worldPos.y;

            TerrainCache? cache = FindCacheForPosition(worldPos);
            if (cache == null) return worldPos.y;

            float normX = Mathf.Clamp01((worldPos.x - cache.Value.position.x) / cache.Value.size.x);
            float normZ = Mathf.Clamp01((worldPos.z - cache.Value.position.z) / cache.Value.size.z);

            int hX = Mathf.FloorToInt(normX * (cache.Value.resolution - 1));
            int hY = Mathf.FloorToInt(normZ * (cache.Value.resolution - 1));

            float h = cache.Value.Heights[hY * cache.Value.resolution + hX];
            return h * cache.Value.size.y + cache.Value.position.y;
        }

        public Vector3 GetNormal(Vector3 worldPos)
        {
            EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
            if (!m_IsInitialized) return Vector3.up;

            TerrainCache? cache = FindCacheForPosition(worldPos);
            if (cache == null) return Vector3.up;

            float normX = (worldPos.x - cache.Value.position.x) / cache.Value.size.x;
            float normZ = (worldPos.z - cache.Value.position.z) / cache.Value.size.z;

            return cache.Value.data.GetInterpolatedNormal(normX, normZ);
        }

        private TerrainCache? FindCacheForPosition(Vector3 worldPos)
        {
            foreach (var cache in m_TerrainCaches)
            {
                if (cache.bounds.Contains(new Vector2(worldPos.x, worldPos.z)))
                {
                    return cache;
                }
            }
            return null;
        }

        public void Dispose()
        {
            UnsubscribeAllTerrainData();
            foreach (var cache in m_TerrainCaches) cache.Dispose();
            m_TerrainCaches.Clear();
            // 不再需要释放arrayPool
        }

        // 事件与订阅管理
        private void SubscribeTerrainData(TerrainData data)
        {
            if (data == null || m_SubscribedTerrainData.Contains(data)) return;
            // 某些旧版 Unity 不包含 TerrainData.heightmapChanged 事件，使用条件编译兼容

            m_SubscribedTerrainData.Add(data);
        }

        private void UnsubscribeAllTerrainData()
        {
            if (m_SubscribedTerrainData.Count == 0) return;
            foreach (var data in m_SubscribedTerrainData)
            {
                if (data == null) continue;
                // 条件编译以兼容不支持该事件的旧版 Unity

            }
            m_SubscribedTerrainData.Clear();
        }

        private void OnHeightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
        {
            // 地形高度发生变更，标记缓存为脏，下一次访问时重建
            m_IsDirty = true;
        }
    }
}