using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using MrPathV2.Editor.Terrain; // legacy interface namespace
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Threading;
using MrPathV2.Editor.Core; // IUnifiedTerrainPainter
using MrPathV2.Runtime.Core; // PathCreator / PathData / PathProfile / StylizedRoadRecipe
using System.Linq;

namespace MrPathV2.Editor.GPU
{
    /// <summary>
    /// 新版 GPU 地形绘制器 - 简洁统一接口
    /// 替代旧版复杂的 GPU 绘制系统，提供简单易用的 API。
    /// </summary>
    public sealed class GpuTerrainPainterV2 : ITerrainPainter, IUnifiedTerrainPainter, IDisposable
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

                // Debug.Log("[GpuTerrainPainterV2] 新版 GPU 绘制器初始化成功");
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
                Debug.LogError($"[GpuTerrainPainterV2] 地形对象为空，终止操作");
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

            // 2) 读取道路宽度与过渡宽度
            var width = profileData.RoadWidth;
            var falloff = math.max(0f, profileData.FalloffWidth);

            // 3) 构建图层配置用于渲染
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
                // 转换元数据为新结构
                var pathData = ConvertToPathData(metadata);
                var recipe = ConvertToPathRecipe(metadata);

                // 执行 GPU 渲染
                var result = _renderer.RenderPath(metadata.Terrain, pathData, recipe, metadata.IsPreview);

                // 非预览模式则应用到地形
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
        /// 同步执行地形绘制（兼容接口）
        /// </summary>
        public bool Execute(TerrainPaintMetadata metadata)
        {
            return ExecuteAsyncInternal(metadata).GetAwaiter().GetResult();
        }

        #endregion

        #region IUnifiedTerrainPainter Implementation

        public bool IsSupported => SystemInfo.supportsComputeShaders;
        public PainterType Type => PainterType.GPU;

        public async Task<TerrainPaintResult> PaintAsync(PathCreator pathCreator, bool isPreview = false, CancellationToken cancellationToken = default)
        {
            var r = Paint(pathCreator, isPreview);
            await Task.Yield();
            return r;
        }

        public TerrainPaintResult Paint(PathCreator pathCreator, bool isPreview = false)
        {
            ValidateState();

            if (pathCreator == null)
            {
                return TerrainPaintResult.CreateFailure("PathCreator为空", PainterType.GPU);
            }

            var profile = pathCreator.profile;
            var runtimePathData = pathCreator.pathData;
            if (profile == null)
            {
                return TerrainPaintResult.CreateFailure("PathProfile为空", PainterType.GPU);
            }
            if (runtimePathData == null)
            {
                return TerrainPaintResult.CreateFailure("PathData为空", PainterType.GPU);
            }

            // 查找最近地形
            var terrain = FindNearestTerrain(pathCreator.transform.position);
            if (terrain == null)
            {
                return TerrainPaintResult.CreateFailure("无法找到相关地形", PainterType.GPU);
            }

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            try
            {
                // 构建 GPU 渲染所需的数据结构
                var pathData = ConvertRuntimePathData(runtimePathData, profile.roadWidth);
                var recipe = ConvertProfileToRecipe(profile, terrain);

                var renderResult = _renderer.RenderPath(terrain, pathData, recipe, isPreview);

                if (isPreview)
                {
                    // 预览模式下注册 RT 到全局缓存，供材质/UI使用
                    global::MrPathV2.Editor.Terrain.GpuPreviewCache.Register(terrain, renderResult.RenderTexture);
                }
                else
                {
                    _renderer.ApplyToTerrain(renderResult);
                }

                sw.Stop();
                return TerrainPaintResult.CreateSuccess(PainterType.GPU, (float)sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainterV2] 统一接口绘制失败: {ex.Message}");
                sw.Stop();
                return TerrainPaintResult.CreateFailure(ex.Message, PainterType.GPU);
            }
        }

        private PathData ConvertRuntimePathData(Runtime.Core.PathData src, float width)
        {
            var knotCount = src?.KnotCount ?? 0;
            if (knotCount < 2)
            {
                return new PathData(Array.Empty<Vector3>(), width, 0f, new Bounds());
            }

            var points = new Vector3[knotCount];
            for (int i = 0; i < knotCount; i++)
            {
                points[i] = src.GetKnot(i).Position;
            }

            var length = CalculatePathLength(points);
            var bounds = CalculatePathBounds(points, width);
            return new PathData(points, width, length, bounds);
        }

        private PathRecipe ConvertProfileToRecipe(PathProfile profile, UnityEngine.Terrain terrain)
        {
            var roadRecipe = profile.roadRecipe;
            var layerConfigs = new System.Collections.Generic.List<LayerConfig>();

            if (roadRecipe != null)
            {
                var mapping = LayerResolver.Resolve(terrain, roadRecipe, interactive: false);
                foreach (var rl in roadRecipe.GetLayers())
                {
                    if (rl == null || !rl.enabled) continue;
                    if (!mapping.TryGetValue(rl.contentLayer, out var layerIndex)) continue;

                    var strength = math.saturate(rl.opacity);
                    var blend = MapToGpuBlendMode((int)rl.blendMode);
                    layerConfigs.Add(new LayerConfig(layerIndex, strength, blend));
                }
            }

            if (layerConfigs.Count == 0)
            {
                layerConfigs.Add(new LayerConfig(0, 1.0f, BlendMode.Replace));
            }

            return new PathRecipe(layerConfigs.ToArray(), math.max(0f, profile.falloffWidth), AnimationCurve.EaseInOut(0, 1, 1, 0));
        }

        private UnityEngine.Terrain FindNearestTerrain(Vector3 position)
        {
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return null;
            if (terrains.Length == 1) return terrains[0];

            UnityEngine.Terrain nearest = null;
            var minDist = float.MaxValue;
            foreach (var t in terrains)
            {
                if (!t || t.terrainData == null) continue;
                var bounds = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
                var dist = Vector3.SqrMagnitude(bounds.center - position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = t;
                }
            }
            return nearest;
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
                var falloff = math.max(0f, width * 0.5f);
                var pathData = new PathData(
                    spinePoints: spinePoints,
                    pathWidth: width,
                    pathLength: CalculatePathLength(spinePoints),
                    pathBounds: CalculatePathBounds(spinePoints, width));

                var recipe = new PathRecipe(layers: layers, falloffDistance: falloff, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));

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
                var falloff = math.max(0f, width * 0.5f);
                var pathData = new PathData(
                    spinePoints: spinePoints,
                    pathWidth: width,
                    pathLength: CalculatePathLength(spinePoints),
                    pathBounds: CalculatePathBounds(spinePoints, width));

                var recipe = new PathRecipe(layers: layers, falloffDistance: falloff, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));

                // 在主线程执行渲染与应用，避免后台线程访问 Unity API
                var result = _renderer.RenderPath(terrain, pathData, recipe, isPreview);

                if (!isPreview && result.Success)
                {
                    _renderer.ApplyToTerrain(result);
                }

                // 保持异步语义但不切到后台线程
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
        /// 娓呴櫎缂撳瓨
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

            // 这里可以收集各组件的性能统计
            return "GPU 绘制器性能统计 - 正常运行";
        }

        #endregion

        #region Data Conversion Helpers

        private static PathData ConvertToPathData(TerrainPaintMetadata metadata)
        {
            // 浠庢棫鐨勫厓鏁版嵁缁撴瀯杞?崲涓烘柊鐨凱athData
            var spinePoints = ExtractSpinePoints(metadata);
            var width = ExtractPathWidth(metadata);

            var falloff = ExtractFalloffDistance(metadata);
            return new PathData(
                spinePoints: spinePoints,
                pathWidth: width,
                pathLength: CalculatePathLength(spinePoints),
                pathBounds: CalculatePathBounds(spinePoints, width));
        }

        private PathRecipe ConvertToPathRecipe(TerrainPaintMetadata metadata)
        {
            // 浠庢棫鐨勫厓鏁版嵁缁撴瀯杞?崲涓烘柊鐨凱athRecipe
            var layers = ExtractLayerConfigs(metadata);
            var falloffDistance = ExtractFalloffDistance(metadata);

            return new PathRecipe(layers: layers, falloffDistance: falloffDistance, falloffCurve: AnimationCurve.EaseInOut(0, 1, 1, 0));
        }

        private static Vector3[] ExtractSpinePoints(TerrainPaintMetadata metadata)
        {
            // 浠庡厓鏁版嵁涓?彁鍙栬剨鏌辩偣
            if (metadata?.SpinePoints != null && metadata.SpinePoints.Length > 0)
            {
                return metadata.SpinePoints;
            }

            // 濡傛灉娌℃湁鑴婃煴鐐癸紝杩斿洖榛樿?鐨勭洿绾胯矾寰?
            Debug.LogWarning("[GpuTerrainPainterV2] 鍏冩暟鎹?腑娌℃湁鑴婃煴鐐癸紝浣跨敤榛樿?璺?緞");
            return new[]
            {
                Vector3.zero, Vector3.forward * 10, Vector3.forward * 20
            };
        }

        private static float ExtractPathWidth(TerrainPaintMetadata metadata)
        {
            // 浠庡厓鏁版嵁涓?彁鍙栬矾寰勫?搴?
            if (metadata != null && metadata.Width > 0)
            {
                return metadata.Width;
            }

            Debug.LogWarning("[GpuTerrainPainterV2] 鍏冩暟鎹?腑娌℃湁鏈夋晥鐨勮矾寰勫?搴︼紝浣跨敤榛樿?鍊?.0f");
            return 5.0f; // 榛樿?鍊?
        }

        private static LayerConfig[] ExtractLayerConfigs(TerrainPaintMetadata metadata)
        {
            // 浠庡厓鏁版嵁涓?彁鍙栧浘灞傞厤缃?
            if (metadata?.Layers != null && metadata.Layers.Length > 0)
            {
                return metadata.Layers;
            }

            // 濡傛灉娌℃湁鍥惧眰閰嶇疆锛岃繑鍥為粯璁ら厤缃?
            Debug.LogWarning("[GpuTerrainPainterV2] 鍏冩暟鎹?腑娌℃湁鍥惧眰閰嶇疆锛屼娇鐢ㄩ粯璁ら厤缃?");
            return new LayerConfig[]
            {
                new LayerConfig(layerIndex: 0, strength: 1.0f, blendMode: BlendMode.Replace)
            };
        }

        private static float ExtractFalloffDistance(TerrainPaintMetadata metadata)
        {
            // 浠庡厓鏁版嵁涓?彁鍙栬“鍑忚窛绂伙紝浼樺厛浣跨敤鏄惧紡鍊?
            if (metadata != null && metadata.FalloffDistance > 0f)
            {
                return metadata.FalloffDistance;
            }

            // 鍥為瑙勫垯锛氶氬父琛板噺璺濈?鏄?矾寰勫?搴︾殑涓鍗?
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

            // 计算脊线点的包围盒
            var min = spinePoints[0];
            var max = spinePoints[0];
            foreach (var point in spinePoints)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            // 扩展边界以包含道路主体与过渡区域
            // var halfWidthWithFalloff = width * 0.5f + math.max(0f, falloff);
            // var expansion = new Vector3(halfWidthWithFalloff, 0f, halfWidthWithFalloff);

            var center = (min + max) * 0.5f;
            var size = max - min * 2f; // XZ方向增加 roadWidth + 2*falloff
            return new Bounds(center, size);
        }

        // CPU 绔?贩鍚堟灇涓惧埌 GPU 鏋氫妇鐨勫畨鍏ㄦ槧灏?
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
                throw new InvalidOperationException("GpuTerrainPainterV2 尚未初始化");
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
    /// 地形绘制元数据（公共数据结构）
    /// </summary>
    public class TerrainPaintMetadata
    {
        public UnityEngine.Terrain Terrain { get; set; }
        public bool IsPreview { get; set; }

        public Vector3[] SpinePoints { get; set; }
        public float Width { get; set; }
        public float FalloffDistance { get; set; }

        public LayerConfig[] Layers { get; set; }
        // 其它常用的元数据字段...
    }


    #endregion

}

