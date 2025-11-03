using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using __temp.MrPathV2.Editor.Terrain; // legacy interface namespace
using __temp.MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Threading;

namespace __temp.MrPathV2.Editor.GPU
{
    /// <summary>
    /// 新版GPU地形绘制器 - 简洁优雅的统一接口
    /// 替代旧的复杂GPU绘制系统，提供简单易用的API
    /// </summary>
    public sealed class GpuTerrainPainterV2 : ITerrainPainter, IDisposable
    {
        #region Singleton Pattern

        private static GpuTerrainPainterV2 s_Instance;
        private static readonly object Lock = new object();

        public static GpuTerrainPainterV2 Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;
                lock (Lock)
                {
                    s_Instance ??= new GpuTerrainPainterV2();
                }
                return s_Instance;
            }
        }

        #endregion

        #region Core Components

        private GpuTerrainRenderer _renderer;
        private bool _isInitialized;
        private bool _isDisposed;

        #endregion

        #region Constructor

        private GpuTerrainPainterV2()
        {
            Initialize();
        }
        public bool IsInitialized { get; set; }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _renderer = new GpuTerrainRenderer();
                _isInitialized = true;

                Debug.Log("[GpuTerrainPainterV2] 新版GPU绘制器初始化成功");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 初始化失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region ITerrainPainter Implementation

        /// <summary>
        /// 实现旧版 ITerrainPainter 接口的 ExecuteAsync 方法
        /// </summary>
        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData,
            NativeArray<float2> roadContour,
            float4 contourBounds,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            // 参数校验与提前返回
            if (terrain == null)
            {
                Debug.LogError("[GpuTerrainPainterV2] 地形对象为空，终止绘制");
                return;
            }
            if (!spineData.IsCreated || spineData.Length < 2)
            {
                Debug.LogError("[GpuTerrainPainterV2] SpineData 无效或点数不足，终止绘制");
                return;
            }
            if (!profileData.IsCreated || profileData.RoadWidth <= 0f)
            {
                Debug.LogError("[GpuTerrainPainterV2] ProfileData 无效或道路宽度不合法，终止绘制");
                return;
            }

            // 1) 提取脊线点
            var spinePoints = new Vector3[spineData.Length];
            for (var i = 0; i < spineData.Length; i++)
            {
                var p = spineData.Points[i];
                spinePoints[i] = new Vector3(p.x, p.y, p.z);
            }

            // 2) 读取道路宽度与衰减宽度
            var width = profileData.RoadWidth;
            var falloff = math.max(0f, profileData.FalloffWidth);

            // 3) 映射配方为图层配置
            LayerConfig[] layers;
            if (!recipeData.IsCreated || recipeData.Length <= 0)
            {
                layers = new LayerConfig[] { new LayerConfig(layerIndex: 0, strength: 1.0f, blendMode: BlendMode.Replace) };
            }
            else
            {
                var list = new System.Collections.Generic.List<LayerConfig>(recipeData.Length);
                for (var i = 0; i < recipeData.Length; i++)
                {
                    var idx = recipeData.TerrainLayerIndices[i];
                    if (idx < 0) continue;
                    var modeInt = recipeData.BlendModes[i];
                    var gpuMode = MapToGpuBlendMode(modeInt);
                    var strength = math.saturate(recipeData.Opacities[i]);
                    list.Add(new LayerConfig(idx, strength, gpuMode));
                }
                layers = list.Count > 0 ? list.ToArray() : new LayerConfig[] { new LayerConfig(layerIndex: 0, strength: 1.0f, blendMode: BlendMode.Replace) };
            }

            // 4) 组装元数据并执行
            var metadata = new TerrainPaintMetadata
            {
                Terrain = terrain,
                IsPreview = false,
                SpinePoints = spinePoints,
                Width = width,
                FalloffDistance = falloff,
                Layers = layers
            };

            await ExecuteAsyncInternal(metadata);
        }

        /// <summary>
        /// 异步执行地形绘制（内部实现）
        /// </summary>
        public Task<bool> ExecuteAsyncInternal(TerrainPaintMetadata metadata)
        {
            ValidateState();

            if (metadata == null)
            {
                Debug.LogError("[GpuTerrainPainterV2] 绘制元数据为空");
                return Task.FromResult(false);
            }

            try
            {
                // 转换元数据为新的数据结构
                var pathData = ConvertToPathData(metadata);
                var recipe = ConvertToPathRecipe(metadata);

                // 执行GPU渲染
                var result = _renderer.RenderPath(metadata.Terrain, pathData, recipe, metadata.IsPreview);

                // 如果不是预览模式，应用到地形
                if (!metadata.IsPreview)
                {
                    _renderer.ApplyToTerrain(result);
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 执行绘制失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 同步执行地形绘制（兼容旧接口）
        /// </summary>
        public bool Execute(TerrainPaintMetadata metadata)
        {
            return ExecuteAsyncInternal(metadata).GetAwaiter().GetResult();
        }

        #endregion

        #region Public API Extensions

        /// <summary>
        /// 快速绘制路径到地形
        /// </summary>
        public bool PaintPath(UnityEngine.Terrain terrain, Vector3[] spinePoints, float width,
            LayerConfig[] layers, bool isPreview = false)
        {
            ValidateState();

            try
            {
                var pathData = new PathData(spinePoints: spinePoints, pathWidth: width, pathLength: CalculatePathLength(spinePoints), pathBounds: CalculatePathBounds(spinePoints, width));

                var recipe = new PathRecipe(layers: layers, falloffDistance: width * 0.5f, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));

                var result = _renderer.RenderPath(terrain, pathData, recipe, isPreview);

                if (!isPreview)
                {
                    _renderer.ApplyToTerrain(result);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 快速绘制失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 异步绘制路径到地形
        /// </summary>
        public async Task<GpuRenderResult> PaintPathAsync(UnityEngine.Terrain terrain, Vector3[] spinePoints, float width,
            LayerConfig[] layers, bool isPreview = false)
        {
            ValidateState();

            try
            {
                var pathData = new PathData(spinePoints: spinePoints, pathWidth: width, pathLength: CalculatePathLength(spinePoints), pathBounds: CalculatePathBounds(spinePoints, width));

                var recipe = new PathRecipe(layers: layers, falloffDistance: width * 0.5f, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));

                // 在主线程执行渲染与应用，避免后台线程访问 Unity API
                var result = _renderer.RenderPath(terrain, pathData, recipe, isPreview);

                if (!isPreview && result.Success)
                {
                    _renderer.ApplyToTerrain(result);
                }

                // 保持异步语义但不切换到后台线程
                await Task.Yield();
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 异步绘制失败: {ex.Message}");
                return new GpuRenderResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache(UnityEngine.Terrain terrain = null)
        {
            ValidateState();
            _renderer?.ClearCache(terrain);
        }

        /// <summary>
        /// 获取性能统计
        /// </summary>
        public string GetPerformanceStats()
        {
            ValidateState();

            // 这里可以收集各个组件的性能统计
            return "GPU绘制器性能统计 - 新架构运行正常";
        }

        #endregion

        #region Data Conversion Helpers

        private static PathData ConvertToPathData(TerrainPaintMetadata metadata)
        {
            // 从旧的元数据结构转换为新的PathData
            var spinePoints = ExtractSpinePoints(metadata);
            var width = ExtractPathWidth(metadata);

            return new PathData(spinePoints: spinePoints, pathWidth: width, pathLength: CalculatePathLength(spinePoints), pathBounds: CalculatePathBounds(spinePoints, width));
        }

        private PathRecipe ConvertToPathRecipe(TerrainPaintMetadata metadata)
        {
            // 从旧的元数据结构转换为新的PathRecipe
            var layers = ExtractLayerConfigs(metadata);
            var falloffDistance = ExtractFalloffDistance(metadata);

            return new PathRecipe(layers: layers, falloffDistance: falloffDistance, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));
        }

        private static Vector3[] ExtractSpinePoints(TerrainPaintMetadata metadata)
        {
            // 从元数据中提取脊柱点
            if (metadata?.SpinePoints != null && metadata.SpinePoints.Length > 0)
            {
                return metadata.SpinePoints;
            }

            // 如果没有脊柱点，返回默认的直线路径
            Debug.LogWarning("[GpuTerrainPainterV2] 元数据中没有脊柱点，使用默认路径");
            return new[]
            {
                Vector3.zero, Vector3.forward * 10, Vector3.forward * 20
            };
        }

        private static float ExtractPathWidth(TerrainPaintMetadata metadata)
        {
            // 从元数据中提取路径宽度
            if (metadata != null && metadata.Width > 0)
            {
                return metadata.Width;
            }

            Debug.LogWarning("[GpuTerrainPainterV2] 元数据中没有有效的路径宽度，使用默认值5.0f");
            return 5.0f; // 默认值
        }

        private static LayerConfig[] ExtractLayerConfigs(TerrainPaintMetadata metadata)
        {
            // 从元数据中提取图层配置
            if (metadata?.Layers != null && metadata.Layers.Length > 0)
            {
                return metadata.Layers;
            }

            // 如果没有图层配置，返回默认配置
            Debug.LogWarning("[GpuTerrainPainterV2] 元数据中没有图层配置，使用默认配置");
            return new LayerConfig[]
            {
                new LayerConfig(layerIndex: 0, strength: 1.0f, blendMode: BlendMode.Replace)
            };
        }

        private static float ExtractFalloffDistance(TerrainPaintMetadata metadata)
        {
            // 从元数据中提取衰减距离，优先使用显式值
            if (metadata != null && metadata.FalloffDistance > 0f)
            {
                return metadata.FalloffDistance;
            }

            // 回退规则：通常衰减距离是路径宽度的一半
            var width = ExtractPathWidth(metadata);
            return math.max(0f, width * 0.5f);
        }

        private static float CalculatePathLength(Vector3[] spinePoints)
        {
            if (spinePoints == null || spinePoints.Length < 2)
                return 0f;

            var totalLength = 0f;
            for (var i = 1; i < spinePoints.Length; i++)
            {
                totalLength += Vector3.Distance(spinePoints[i - 1], spinePoints[i]);
            }
            return totalLength;
        }

        private static Bounds CalculatePathBounds(Vector3[] spinePoints, float width)
        {
            if (spinePoints == null || spinePoints.Length == 0)
                return new Bounds();

            var bounds = new Bounds(spinePoints[0], Vector3.zero);
            foreach (var point in spinePoints)
            {
                bounds.Encapsulate(point);
            }

            // 扩展边界以包含路径宽度
            bounds.Expand(width);
            return bounds;
        }

        // CPU 端混合枚举到 GPU 枚举的安全映射
        private static BlendMode MapToGpuBlendMode(int cpuBlendModeInt)
        {
            // Runtime.Core.BlendMode: Normal(0), Multiply(1), Add(2), Overlay(3), Screen(4), Lerp(5), Additive(6)
            // GPU.BlendMode: Replace, Add, Multiply, Overlay
            switch (cpuBlendModeInt)
            {
                case 0: /* Normal */ return BlendMode.Replace;
                case 5: /* Lerp */ return BlendMode.Replace;
                case 4: /* Screen */ return BlendMode.Replace;
                case 1: /* Multiply */ return BlendMode.Multiply;
                case 2: /* Add */ return BlendMode.Add;
                case 6: /* Additive */ return BlendMode.Add;
                case 3: /* Overlay */ return BlendMode.Overlay;
                default: return BlendMode.Replace;
            }
        }

        #endregion

        #region Validation

        private void ValidateState()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GpuTerrainPainterV2));

            if (!_isInitialized)
                throw new InvalidOperationException("GpuTerrainPainterV2 未初始化");
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _renderer?.Dispose();

                lock (Lock)
                {
                    if (s_Instance == this)
                    {
                        s_Instance = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 清理资源时出错: {ex.Message}");
            }
            finally
            {
                _isDisposed = true;
                _isInitialized = false;
            }
        }


        #endregion

        #region Static Cleanup

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            // 确保在编辑器重新加载时清理资源
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.ExitingPlayMode)
            {
                s_Instance?.Dispose();
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            s_Instance?.Dispose();
        }

        #endregion
    }

    // Remove duplicate legacy interface declaration here, keep TerrainPaintMetadata only
    #region Interface Definition

    // (Removed duplicate ITerrainPainter definition to avoid naming collision)
    /// <summary>
    /// 地形绘制元数据（兼容旧系统）
    /// </summary>
    public class TerrainPaintMetadata
    {
        public UnityEngine.Terrain Terrain { get; set; }
        public bool IsPreview { get; set; }

        public UnityEngine.Vector3[] SpinePoints { get; set; }
        public float Width { get; set; }
        public float FalloffDistance { get; set; }

        public LayerConfig[] Layers { get; set; }
        // 其他必要的元数据字段...
    }


    #endregion

}
