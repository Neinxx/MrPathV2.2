// 请用此完整代码替换你的 TerrainHeightProvider.cs

using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// 【乾坤无界 • 多地形版】一个高性能的地形高度与法线查询器。
/// 它在初始化时缓存所有活动地形的数据，并能根据世界坐标自动查询正确地形。
/// </summary>
public class TerrainHeightProvider : System.IDisposable
{
    // 内部数据结构，用于缓存单个地形的信息
    private class TerrainCache
    {
        public Terrain terrain;
        public TerrainData data;
        public Rect bounds; // 使用2D Rect进行高效的范围判断
        public NativeArray<float> heights;
        public int resolution;
        public Vector3 position;
        public Vector3 size;
    }

    private readonly List<TerrainCache> m_TerrainCaches;
    private readonly bool m_IsInitialized;

    public TerrainHeightProvider()
    {
        m_TerrainCaches = new List<TerrainCache>();
        var activeTerrains = Terrain.activeTerrains;

        if (activeTerrains == null || activeTerrains.Length == 0)
        {
            m_IsInitialized = false;
            return;
        }

        // 遍历并缓存所有地形
        foreach (var terrain in activeTerrains)
        {
            var data = terrain.terrainData;
            var position = terrain.GetPosition();
            var size = data.size;

            var heights2D = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.Persistent);
            // 扁平化高度数据
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
    }

    /// <summary>
    /// 根据世界坐标，从对应的地形缓存中获取高度。
    /// </summary>
    public float GetHeight(Vector3 worldPos)
    {
        if (!m_IsInitialized) return worldPos.y;

        TerrainCache cache = FindCacheForPosition(worldPos);
        if (cache == null) return worldPos.y; // 不在任何缓存的地形上

        // 将世界坐标转换为归一化的地形坐标
        float normX = (worldPos.x - cache.position.x) / cache.size.x;
        float normZ = (worldPos.z - cache.position.z) / cache.size.z;

        // Clamp to prevent out of bounds, though FindCacheForPosition should handle it
        normX = Mathf.Clamp01(normX);
        normZ = Mathf.Clamp01(normZ);

        int hX = Mathf.FloorToInt(normX * (cache.resolution - 1));
        int hY = Mathf.FloorToInt(normZ * (cache.resolution - 1));

        float h = cache.heights[hY * cache.resolution + hX];
        return h * cache.size.y + cache.position.y;
    }

    /// <summary>
    /// 根据世界坐标，获取对应地形的表面法线。
    /// </summary>
    public Vector3 GetNormal(Vector3 worldPos)
    {
        if (!m_IsInitialized) return Vector3.up;

        TerrainCache cache = FindCacheForPosition(worldPos);
        if (cache == null) return Vector3.up;

        float normX = (worldPos.x - cache.position.x) / cache.size.x;
        float normZ = (worldPos.z - cache.position.z) / cache.size.z;

        return cache.data.GetInterpolatedNormal(normX, normZ);
    }

    /// <summary>
    /// 查找包含指定世界坐标的地形缓存。
    /// </summary>
    private TerrainCache FindCacheForPosition(Vector3 worldPos)
    {
        // 性能提示: 如果地形数量巨大，可以考虑使用四叉树等空间分割数据结构来加速查找
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
        if (m_TerrainCaches == null) return;
        foreach (var cache in m_TerrainCaches)
        {
            if (cache.heights.IsCreated)
            {
                cache.heights.Dispose();
            }
        }
        m_TerrainCaches.Clear();
    }
}