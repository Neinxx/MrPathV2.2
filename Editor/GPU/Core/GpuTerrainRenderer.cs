using System;
using System.Linq;
using MrPathV2.Editor.GPU.Core;
using UnityEngine;
using UnityEditor;
using MrPathV2.Editor.GPU;

namespace MrPathV2.Editor.GPU
{
    /// <summary>
    /// 统一的GPU地形绘制器 - 新架构的核心入口点
    /// 设计原则：简洁、优雅、高效、安全
    /// </summary>
    public sealed class GpuTerrainRenderer : IDisposable
    {
        #region Core Components
        private readonly GpuResourceManager _resourceManager;
        private readonly GpuDataStreamer _dataStreamer;
        private readonly GpuComputeDispatcher _computeDispatcher;
        private Pipeline.TerrainPaintPipeline _pipeline;
        private readonly GpuRenderCache _renderCache;
        #endregion

        #region State Management
        private bool _isInitialized;
        private bool _isDisposed;
        #endregion

        #region Constructor & Initialization
        public GpuTerrainRenderer()
        {
            _resourceManager = new GpuResourceManager();
            _dataStreamer = new GpuDataStreamer(_resourceManager);
            _computeDispatcher = new GpuComputeDispatcher(_resourceManager);
            _renderCache = new GpuRenderCache(_resourceManager);
            _pipeline = new Pipeline.TerrainPaintPipeline(_dataStreamer, _computeDispatcher);

            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _resourceManager.Initialize();
                _dataStreamer.Initialize();
                _computeDispatcher.Initialize();
                _renderCache.Initialize();

                _isInitialized = true;

                // 注册清理回调
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainRenderer] 初始化失败: {ex.Message}");
                Dispose();
                throw;
            }
        }
        #endregion

        #region Public API - 简洁统一的接口
        /// <summary>
        /// 渲染路径到地形 - 统一的入口点
        /// </summary>
        /// <param name="terrain">目标地形</param>
        /// <param name="pathData">路径数据</param>
        /// <param name="recipe">绘制配方</param>
        /// <param name="isPreview">是否为预览模式</param>
        /// <returns>渲染结果</returns>
        public GpuRenderResult RenderPath(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe, bool isPreview = false)
        {
            ValidateState();
            ValidateInputs(terrain, pathData, recipe);

            try
            {
                // 1. 检查缓存
                var cacheKey = GenerateCacheKey(terrain, pathData, recipe);
                if (_renderCache.TryGetCachedResult(cacheKey, out var cachedResult))
                {
                    return cachedResult;
                }

                // 2/3. 执行按阶段的管线（数据准备 + 栅格化）
                var (renderTexture, cp, pipelineError) = _pipeline.Execute(terrain, pathData, recipe, isPreview);
                if (!string.IsNullOrEmpty(pipelineError))
                {
                    throw new InvalidOperationException(pipelineError);
                }

                // 4. 创建结果（包含 ROI 与层数以便高效写回）
                var result = new GpuRenderResult
                {
                    RenderTexture = renderTexture,
                    Terrain = terrain,
                    IsPreview = isPreview,
                    Timestamp = DateTime.UtcNow,
                    CoverageArea = cp.CoverageArea,
                    AlphamapLayerCount = cp.AlphamapLayerCount,
                    Resolution = cp.Resolution
                };

                // 5. 缓存结果
                _renderCache.CacheResult(cacheKey, result);

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainRenderer] 渲染失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 应用渲染结果到地形
        /// </summary>
        public void ApplyToTerrain(GpuRenderResult result)
        {
            ValidateState();
            if (result == null || result.IsDisposed)
                throw new ArgumentException("无效的渲染结果");

            try
            {
                _dataStreamer.ApplyToTerrain(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainRenderer] 应用到地形失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 清除指定地形的缓存
        /// </summary>
        public void ClearCache(UnityEngine.Terrain terrain = null)
        {
            ValidateState();
            _renderCache.ClearCache(terrain);
        }
        #endregion

        #region Cache Key Generation
        private static string GenerateCacheKey(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe)
        {
            // 使用高效的哈希算法生成缓存键
            var terrainId = terrain.GetInstanceID();
            var pathHash = pathData.GetHashCode();
            var recipeHash = recipe.GetHashCode();

            return $"{terrainId}_{pathHash}_{recipeHash}";
        }
        #endregion

        #region Validation
        private void ValidateState()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GpuTerrainRenderer));

            if (!_isInitialized)
                throw new InvalidOperationException("GpuTerrainRenderer 未初始化");
        }

        private static void ValidateInputs(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            if (pathData == null)
                throw new ArgumentNullException(nameof(pathData));

            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            if (terrain.terrainData == null)
                throw new ArgumentException("地形数据无效", nameof(terrain));
        }
        #endregion

        #region Event Handlers
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.ExitingPlayMode)
            {
                ClearCache();
            }
        }

        private void OnBeforeAssemblyReload()
        {
            Dispose();
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                // 注销事件
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;

                // 清理组件
                _renderCache?.Dispose();
                _computeDispatcher?.Dispose();
                _dataStreamer?.Dispose();
                _resourceManager?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainRenderer] 清理资源时出错: {ex.Message}");
            }
            finally
            {
                _isDisposed = true;
                _isInitialized = false;
            }
        }
        #endregion
    }

    #region Supporting Types
    /// <summary>
    /// GPU渲染结果
    /// </summary>
    public sealed class GpuRenderResult : IDisposable
    {
        public RenderTexture RenderTexture { get; set; }
        public UnityEngine.Terrain Terrain { get; set; }
        public bool IsPreview { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsDisposed { get; private set; }
        public Vector4 CoverageArea { get; set; }
        public int AlphamapLayerCount { get; set; }
        public Vector2Int Resolution { get; set; }

        /// <summary>
        /// 渲染是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 错误消息（如果渲染失败）
        /// </summary>
        public string ErrorMessage { get; set; }

        public void Dispose()
        {
            if (IsDisposed) return;

            // RenderTexture 由 GpuResourceManager 统一管理与复用，
            // 此处不再销毁以避免外部仍在使用时出现“对象已被销毁”的错误。
            // 仅标记为已处置并断开引用，交由资源管理器在适当时机释放。
            RenderTexture = null;

            IsDisposed = true;
        }
    }

    /// <summary>
    /// 路径数据结构
    /// </summary>
    public class PathData
    {
        public PathData(Vector3[] spinePoints, float pathWidth, float pathLength, Bounds pathBounds)
        {
            SpinePoints = spinePoints;
            PathWidth = pathWidth;
            PathLength = pathLength;
            PathBounds = pathBounds;
        }
        public Vector3[] SpinePoints { get; }
        public Vector3[] ContourPoints { get; set; }
        public float PathWidth { get; }
        public float PathLength { get; }
        public Bounds PathBounds { get; }

        public override int GetHashCode()
        {
            // 高效的哈希计算
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + PathWidth.GetHashCode();
                hash = hash * 23 + PathLength.GetHashCode();
                hash = hash * 23 + PathBounds.GetHashCode();

                return SpinePoints == null ? hash : SpinePoints.Aggregate(hash, (current, t) => current * 23 + t.GetHashCode());

            }
        }
    }

    /// <summary>
    /// 路径绘制配方
    /// </summary>
    public class PathRecipe
    {
        public PathRecipe(LayerConfig[] layers, float falloffDistance, AnimationCurve falloffCurve)
        {
            Layers = layers;
            FalloffDistance = falloffDistance;
            FalloffCurve = falloffCurve;
        }
        public PathRecipe()
        {
            throw new NotImplementedException();
        }
        public LayerConfig[] Layers { get; }
        public float FalloffDistance { get; }
        public AnimationCurve FalloffCurve { get; set; }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + FalloffDistance.GetHashCode();

                return Layers == null ? hash : Layers.Aggregate(hash, (current, t) => current * 23 + t.GetHashCode());

            }
        }
    }

    public class LayerConfig
    {
        public LayerConfig(int layerIndex, float strength, BlendMode blendMode)
        {
            LayerIndex = layerIndex;
            Strength = strength;
            BlendMode = blendMode;
            Mask = null;
        }

        // 新增：可携带与该图层关联的遮罩对象（可为空）
        public LayerConfig(int layerIndex, float strength, BlendMode blendMode, Runtime.Core.BlendMasks.BlendMaskBase mask)
        {
            LayerIndex = layerIndex;
            Strength = strength;
            BlendMode = blendMode;
            Mask = mask;
        }

        public int LayerIndex { get; }
        public float Strength { get; }
        public BlendMode BlendMode { get; }
        public Runtime.Core.BlendMasks.BlendMaskBase Mask { get; }

        public override int GetHashCode()
        {
            return HashCode.Combine(LayerIndex, Strength, BlendMode, Mask != null ? Mask.GetHashCode() : 0);
        }
    }

    public enum BlendMode
    {
        Replace,
        Add,
        Multiply,
        Overlay
    }
    #endregion
}
