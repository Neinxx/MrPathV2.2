using System;
using System.Collections.Generic;
using MrPathV2.Editor.GPU.Core;
using UnityEngine;

namespace MrPathV2.Editor.GPU
{
    /// <summary>
    /// 智能GPU渲染缓存系统 - 高效的缓存管理和失效策略
    /// 设计原则：智能缓存策略，自动失效管理，内存使用优化
    /// </summary>
    public sealed class GpuRenderCache : IDisposable
    {
        #region Dependencies
        private readonly GpuResourceManager _resourceManager;
        #endregion

        #region Cache Entry
        private class CacheEntry
        {
            public string Key;
            public GpuRenderResult Result;
            public DateTime CreatedTime;
            public DateTime LastAccessTime;
            public int AccessCount;
            public long MemorySize;
            public bool IsValid;


            public TimeSpan Age => DateTime.UtcNow - CreatedTime;
            public TimeSpan TimeSinceLastAccess => DateTime.UtcNow - LastAccessTime;

            public void MarkAccessed()
            {
                LastAccessTime = DateTime.UtcNow;
                AccessCount++;
            }
        }
        #endregion

        #region Cache Storage
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly Dictionary<int, HashSet<string>> _terrainToKeys = new(); // Terrain ID -> Cache Keys
        private readonly object _lockObject = new();
        #endregion

        #region Cache Configuration
        private readonly CacheConfig _config;

        private struct CacheConfig
        {
            public int MaxEntries;
            public long MaxMemoryBytes;
            public TimeSpan MaxAge;
            public TimeSpan MaxIdleTime;
            public float EvictionThreshold; // 当达到此阈值时开始清理

            public static CacheConfig Default => new()
            {
                MaxEntries = 100,
                MaxMemoryBytes = 512 * 1024 * 1024, // 512MB
                MaxAge = TimeSpan.FromMinutes(30),
                MaxIdleTime = TimeSpan.FromMinutes(10),
                EvictionThreshold = 0.8f
            };
        }
        #endregion

        #region Cache Statistics
        private CacheStats _stats;

        private struct CacheStats
        {
            public int TotalEntries;
            public long TotalMemoryUsage;
            public int HitCount;
            public int MissCount;
            public int EvictionCount;

            public float HitRatio => TotalRequests > 0 ? (float)HitCount / TotalRequests : 0f;
            public int TotalRequests => HitCount + MissCount;

            public void RecordHit() => HitCount++;
            public void RecordMiss() => MissCount++;
            public void RecordEviction() => EvictionCount++;
        }
        #endregion

        #region State
        private bool _isInitialized;
        private bool _isDisposed;
        private System.Threading.Timer _cleanupTimer;
        #endregion

        #region Constructor
        public GpuRenderCache(GpuResourceManager resourceManager)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _config = CacheConfig.Default;
        }
        #endregion

        #region Initialization
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // 启动定期清理定时器
                _cleanupTimer = new System.Threading.Timer(PerformCleanup, null,
                    TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuRenderCache] 初始化失败: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// 尝试获取缓存的渲染结果
        /// </summary>
        public bool TryGetCachedResult(string key, out GpuRenderResult result)
        {
            ValidateState();
            result = null;

            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.IsValid && !entry.Result.IsDisposed)
                {
                    entry.MarkAccessed();
                    result = entry.Result;
                    _stats.RecordHit();
                    return true;
                }
                else
                {
                    // 清理无效的缓存项
                    if (entry != null)
                    {
                        RemoveCacheEntry(key, entry);
                    }

                    _stats.RecordMiss();
                    return false;
                }
            }
        }

        /// <summary>
        /// 缓存渲染结果
        /// </summary>
        public void CacheResult(string key, GpuRenderResult result)
        {
            ValidateState();
            if (string.IsNullOrEmpty(key) || result == null)
                return;

            lock (_lockObject)
            {
                // 检查是否需要清理空间
                if (ShouldEvict())
                {
                    PerformEviction();
                }

                // 如果已存在，先清理旧的
                if (_cache.TryGetValue(key, out var existingEntry))
                {
                    RemoveCacheEntry(key, existingEntry);
                }

                // 创建新的缓存项
                var entry = new CacheEntry
                {
                    Key = key,
                    Result = result,
                    CreatedTime = DateTime.UtcNow,
                    LastAccessTime = DateTime.UtcNow,
                    AccessCount = 1,
                    MemorySize = EstimateMemorySize(result),
                    IsValid = true
                };

                _cache[key] = entry;

                // 更新地形索引
                if (result.Terrain != null)
                {
                    var terrainId = result.Terrain.GetInstanceID();
                    if (!_terrainToKeys.TryGetValue(terrainId, out var keys))
                    {
                        keys = new HashSet<string>();
                        _terrainToKeys[terrainId] = keys;
                    }
                    keys.Add(key);
                }

                // 更新统计信息
                _stats.TotalEntries = _cache.Count;
                _stats.TotalMemoryUsage += entry.MemorySize;
            }
        }

        /// <summary>
        /// 清除指定地形的缓存
        /// </summary>
        public void ClearCache(UnityEngine.Terrain terrain = null)
        {
            ValidateState();

            lock (_lockObject)
            {
                if (!terrain)
                {
                    // 清除所有缓存
                    ClearAllCache();
                }
                else
                {
                    // 清除特定地形的缓存
                    var terrainId = terrain.GetInstanceID();
                    if (_terrainToKeys.TryGetValue(terrainId, out var keys))
                    {
                        var keysToRemove = new List<string>(keys);
                        foreach (var key in keysToRemove)
                        {
                            if (_cache.TryGetValue(key, out var entry))
                            {
                                RemoveCacheEntry(key, entry);
                            }
                        }
                        _terrainToKeys.Remove(terrainId);
                    }
                }
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            ValidateState();

            lock (_lockObject)
            {
                return new CacheStatistics
                {
                    TotalEntries = _stats.TotalEntries,
                    TotalMemoryUsage = _stats.TotalMemoryUsage,
                    HitCount = _stats.HitCount,
                    MissCount = _stats.MissCount,
                    HitRatio = _stats.HitRatio,
                    EvictionCount = _stats.EvictionCount
                };
            }
        }
        #endregion

        #region Cache Management
        private bool ShouldEvict()
        {
            return _cache.Count >= _config.MaxEntries * _config.EvictionThreshold ||
                   _stats.TotalMemoryUsage >= _config.MaxMemoryBytes * _config.EvictionThreshold;
        }

        private void PerformEviction()
        {
            var entriesToRemove = new List<CacheEntry>();

            // 收集需要清理的项目
            foreach (var entry in _cache.Values)
            {
                if (!entry.IsValid || entry.Result.IsDisposed ||
                    entry.Age > _config.MaxAge ||
                    entry.TimeSinceLastAccess > _config.MaxIdleTime)
                {
                    entriesToRemove.Add(entry);
                }
            }

            // 如果还需要更多空间，按LRU策略清理
            if (entriesToRemove.Count == 0 || _cache.Count - entriesToRemove.Count > _config.MaxEntries * 0.7f)
            {
                var allEntries = new List<CacheEntry>(_cache.Values);
                allEntries.Sort((a, b) => a.LastAccessTime.CompareTo(b.LastAccessTime));

                int targetCount = (int)(_config.MaxEntries * 0.6f);
                int currentCount = _cache.Count - entriesToRemove.Count;

                for (int i = 0; i < allEntries.Count && currentCount > targetCount; i++)
                {
                    if (!entriesToRemove.Contains(allEntries[i]))
                    {
                        entriesToRemove.Add(allEntries[i]);
                        currentCount--;
                    }
                }
            }

            // 执行清理
            foreach (var entry in entriesToRemove)
            {
                RemoveCacheEntry(entry.Key, entry);
                _stats.RecordEviction();
            }
        }

        private void PerformCleanup(object state)
        {
            if (_isDisposed) return;

            try
            {
                lock (_lockObject)
                {
                    PerformEviction();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GpuRenderCache] 定期清理时出错: {ex.Message}");
            }
        }

        private void RemoveCacheEntry(string key, CacheEntry entry)
        {
            try
            {
                // 清理渲染结果
                entry.Result?.Dispose();

                // 从缓存中移除
                _cache.Remove(key);

                // 从地形索引中移除
                if (entry.Result?.Terrain != null)
                {
                    var terrainId = entry.Result.Terrain.GetInstanceID();
                    if (_terrainToKeys.TryGetValue(terrainId, out var keys))
                    {
                        keys.Remove(key);
                        if (keys.Count == 0)
                        {
                            _terrainToKeys.Remove(terrainId);
                        }
                    }
                }

                // 更新统计信息
                _stats.TotalMemoryUsage -= entry.MemorySize;
                _stats.TotalEntries = _cache.Count;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GpuRenderCache] 移除缓存项时出错: {ex.Message}");
            }
        }

        private void ClearAllCache()
        {
            try
            {
                foreach (var entry in _cache.Values)
                {
                    entry.Result?.Dispose();
                }

                _cache.Clear();
                _terrainToKeys.Clear();

                _stats.TotalEntries = 0;
                _stats.TotalMemoryUsage = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuRenderCache] 清除所有缓存时出错: {ex.Message}");
            }
        }

        private long EstimateMemorySize(GpuRenderResult result)
        {
            if (result?.RenderTexture == null)
                return 0;

            var rt = result.RenderTexture;
            long pixelCount = (long)rt.width * rt.height * rt.volumeDepth;

            // 估算每像素字节数（基于格式）
            int bytesPerPixel = rt.format switch
            {
                RenderTextureFormat.ARGB32 => 4,
                RenderTextureFormat.RGB565 => 2,
                RenderTextureFormat.ARGB4444 => 2,
                RenderTextureFormat.R8 => 1,
                RenderTextureFormat.RG16 => 2,
                _ => 4 // 默认值
            };

            return pixelCount * bytesPerPixel;
        }
        #endregion

        #region Validation
        private void ValidateState()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GpuRenderCache));

            if (!_isInitialized)
                throw new InvalidOperationException("GpuRenderCache 未初始化");
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            lock (_lockObject)
            {
                try
                {
                    // 停止清理定时器
                    _cleanupTimer?.Dispose();

                    // 清除所有缓存
                    ClearAllCache();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GpuRenderCache] 清理资源时出错: {ex.Message}");
                }
                finally
                {
                    _isDisposed = true;
                    _isInitialized = false;
                }
            }
        }
        #endregion
    }

    #region Supporting Types
    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public struct CacheStatistics
    {
        public int TotalEntries;
        public long TotalMemoryUsage;
        public int HitCount;
        public int MissCount;
        public float HitRatio;
        public int EvictionCount;

        public override string ToString()
        {
            return $"Entries: {TotalEntries}, Memory: {TotalMemoryUsage / (1024 * 1024)}MB, " +
                   $"Hit Ratio: {HitRatio:P2}, Evictions: {EvictionCount}";
        }
    }
    #endregion
}
