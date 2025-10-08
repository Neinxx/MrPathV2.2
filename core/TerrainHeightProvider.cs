// TerrainHeightProvider.cs (智能懒汉版)
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class TerrainHeightProvider : System.IDisposable
{
    private class TerrainCache
    {
        public Terrain terrain;
        public TerrainData data;
        public Rect bounds;
        public NativeArray<float> heights;
        public int resolution;
        public Vector3 position;
        public Vector3 size;

        public void Dispose()
        {
            if (heights.IsCreated) heights.Dispose();
        }
    }

    private readonly List<TerrainCache> m_TerrainCaches = new List<TerrainCache>();
    private bool m_IsInitialized = false;
    private bool m_IsDirty = true; // 初始状态为“脏”，强制在第一次使用时构建缓存

    public TerrainHeightProvider()
    {
        // 构造函数现在什么都不做，把构建操作延迟
    }

    /// <summary>
    /// 【核心】在需要时才构建或重建缓存
    /// </summary>
    private void EnsureCacheIsUpToDate()
    {
        if (!m_IsDirty) return; // 如果数据是新鲜的，直接返回

        // 清理旧的缓存
        foreach (var cache in m_TerrainCaches) cache.Dispose();
        m_TerrainCaches.Clear();

        var activeTerrains = Terrain.activeTerrains;
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
            var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.Persistent);

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
                heights = heightsNative,
                resolution = data.heightmapResolution,
                position = position,
                size = size
            });
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

        TerrainCache cache = FindCacheForPosition(worldPos);
        if (cache == null) return worldPos.y;

        float normX = Mathf.Clamp01((worldPos.x - cache.position.x) / cache.size.x);
        float normZ = Mathf.Clamp01((worldPos.z - cache.position.z) / cache.size.z);

        int hX = Mathf.FloorToInt(normX * (cache.resolution - 1));
        int hY = Mathf.FloorToInt(normZ * (cache.resolution - 1));

        float h = cache.heights[hY * cache.resolution + hX];
        return h * cache.size.y + cache.position.y;
    }

    public Vector3 GetNormal(Vector3 worldPos)
    {
        EnsureCacheIsUpToDate(); // 在访问前，确保缓存是新鲜的
        if (!m_IsInitialized) return Vector3.up;

        TerrainCache cache = FindCacheForPosition(worldPos);
        if (cache == null) return Vector3.up;

        float normX = (worldPos.x - cache.position.x) / cache.size.x;
        float normZ = (worldPos.z - cache.position.z) / cache.size.z;

        return cache.data.GetInterpolatedNormal(normX, normZ);
    }

    private TerrainCache FindCacheForPosition(Vector3 worldPos)
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
        foreach (var cache in m_TerrainCaches) cache.Dispose();
        m_TerrainCaches.Clear();
    }
}