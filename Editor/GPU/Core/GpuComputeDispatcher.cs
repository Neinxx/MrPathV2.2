using System;
using MrPathV2.Editor.GPU.Core;
using MrPathV2.Runtime.Core.Noise;
using UnityEngine;
using UnityEngine.Rendering;

namespace MrPathV2.Editor.GPU
{
    /// <summary>
    /// GPU计算调度器 - 高效的计算着色器调度和执行
    /// 设计原则：最优化的线程组调度，最小化状态切换
    /// </summary>
    public sealed class GpuComputeDispatcher : IDisposable
    {
        #region Dependencies
        private readonly GpuResourceManager _resourceManager;
        #endregion

        #region Shader Properties
        private static class ShaderProperties
        {
            // 与计算着色器一致的属性名
            public static readonly int AlphamapResolution = Shader.PropertyToID("alphamap_resolution");
            public static readonly int TerrainPosition = Shader.PropertyToID("terrain_position");
            public static readonly int TerrainSize = Shader.PropertyToID("terrain_size");
            public static readonly int RoadWidth = Shader.PropertyToID("road_width");
            public static readonly int FalloffDistance = Shader.PropertyToID("falloff_distance");
            public static readonly int LayerCount = Shader.PropertyToID("layer_count");
            public static readonly int CoverageMin = Shader.PropertyToID("coverage_min");
            public static readonly int CoverageMax = Shader.PropertyToID("coverage_max");
            public static readonly int ContourBounds = Shader.PropertyToID("contour_bounds");
            public static readonly int AlphamapLayerCount = Shader.PropertyToID("alphamap_layer_count");
            public static readonly int SpinePointCount = Shader.PropertyToID("spine_point_count");
            public static readonly int ContourPointCount = Shader.PropertyToID("contour_point_count");
            public static readonly int TerrainOffset = Shader.PropertyToID("terrain_offset");
            public static readonly int AlphamapOffset = Shader.PropertyToID("alphamap_offset");
            public static readonly int DebugMode = Shader.PropertyToID("debug_mode");
            public static readonly int EdgeWidthWorld = Shader.PropertyToID("edge_width_world");

            // 缓冲区
            public static readonly int SpineDataBuffer = Shader.PropertyToID("spine_data");
            public static readonly int ContourDataBuffer = Shader.PropertyToID("road_contour");
            public static readonly int LayerParamsBuffer = Shader.PropertyToID("layer_params_buffer");

            // 纹理
            public static readonly int SplatWeights = Shader.PropertyToID("splat_weights");
            public static readonly int RoadMask = Shader.PropertyToID("road_mask");
            public static readonly int RoadSDF = Shader.PropertyToID("road_sdf");
            public static readonly int MaskThreshold = Shader.PropertyToID("mask_threshold");
            public static readonly int UseRoadMask = Shader.PropertyToID("use_road_mask");

            // 噪声LUT（统一 CPU/GPU 来源）
            public static readonly int NoiseLUT = Shader.PropertyToID("_NoiseLUT");
            public static readonly int NoiseLutSize = Shader.PropertyToID("_NoiseLutSize");
        }
        #endregion

        #region Compute Shader Info
        private struct ComputeShaderInfo
        {
            public ComputeShader Shader;
            public int KernelIndex;
            public Vector3Int ThreadGroupSize;
            public bool IsValid;
        }

        private ComputeShaderInfo _paintTerrainShader;
        #endregion

        #region State
        private bool _isInitialized;
        private bool _isDisposed;
        #endregion

        #region Constructor
        public GpuComputeDispatcher(GpuResourceManager resourceManager)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        }
        #endregion

        #region Initialization
        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                LoadComputeShaders();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuComputeDispatcher] 初始化失败: {ex.Message}");
                throw;
            }
        }

        private void LoadComputeShaders()
        {
            // 加载地形绘制着色器
            var paintShader = _resourceManager.GetComputeShader("PaintSplatmapCompute");
            if (paintShader != null)
            {
                var kernelIndex = paintShader.FindKernel("paint_terrain");
                if (kernelIndex >= 0)
                {
                    paintShader.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);

                    _paintTerrainShader = new ComputeShaderInfo
                    {
                        Shader = paintShader,
                        KernelIndex = kernelIndex,
                        ThreadGroupSize = new Vector3Int((int)x, (int)y, (int)z),
                        IsValid = true
                    };
                }
                else
                {
                    Debug.LogError("[GpuComputeDispatcher] 未找到 paint_terrain 内核");

                }
            }
            else
            {
                Debug.LogError("[GpuComputeDispatcher] 未找到 PaintSplatmapCompute 计算着色器");
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// 执行GPU计算
        /// </summary>
        public RenderTexture ExecuteCompute(GpuDataStreamer.GpuDataPacket dataPacket, bool isPreview)
        {
            ValidateState();
            ValidateDataPacket(dataPacket);

            if (!_paintTerrainShader.IsValid)
            {
                throw new InvalidOperationException("地形绘制着色器未正确加载");
            }

            try
            {
                // 1. 绑定所有参数（含预览阈值）
                BindComputeParameters(dataPacket, isPreview);

                // 2. 计算线程组数量：使用 ROI 局部调度，减少无关像素计算
                var cp = dataPacket.ComputeParams;
                var threadGroups = CalculateThreadGroupsROI(cp, _paintTerrainShader.ThreadGroupSize);

                // 3. 调度计算着色器
                var shader = _paintTerrainShader.Shader;
                var kernel = _paintTerrainShader.KernelIndex;

                // 使用CommandBuffer进行批量操作以提高性能
                using (var cmd = new CommandBuffer { name = "TerrainPaint" })
                {
                    cmd.DispatchCompute(shader, kernel, threadGroups.x, threadGroups.y, threadGroups.z);
                    Graphics.ExecuteCommandBuffer(cmd);
                }

                // 4. 返回结果纹理
                return dataPacket.AlphaMapTexture;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuComputeDispatcher] 执行计算失败: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Parameter Binding
        private void BindComputeParameters(GpuDataStreamer.GpuDataPacket dataPacket, bool isPreview)
        {
            var shader = _paintTerrainShader.Shader;
            var kernel = _paintTerrainShader.KernelIndex;
            var cp = dataPacket.ComputeParams;

            // 标量/向量参数（匹配 compute 名称和维度）
            shader.SetInts(ShaderProperties.AlphamapResolution, cp.Resolution.x, cp.Resolution.y);
            shader.SetVector(ShaderProperties.TerrainPosition, new Vector4(cp.TerrainPosition.x, cp.TerrainPosition.z, 0f, 0f));
            shader.SetVector(ShaderProperties.TerrainSize, new Vector4(cp.TerrainSize.x, cp.TerrainSize.z, 0f, 0f));
            shader.SetFloat(ShaderProperties.RoadWidth, cp.PathWidth);
            shader.SetFloat(ShaderProperties.FalloffDistance, cp.FalloffDistance);
            shader.SetInt(ShaderProperties.LayerCount, cp.LayerCount);
            shader.SetInt(ShaderProperties.AlphamapLayerCount, cp.AlphamapLayerCount);
            shader.SetInt(ShaderProperties.SpinePointCount, cp.SpinePointCount);
            shader.SetInt(ShaderProperties.ContourPointCount, cp.ContourPointCount);
            shader.SetFloat(ShaderProperties.EdgeWidthWorld, cp.EdgeWidthWorld);
            shader.SetInt(ShaderProperties.DebugMode, 0);
            // ROI 偏移：仅写入受影响的区域，提高效率
            var roiMinX = Mathf.Max(0, (int)cp.CoverageArea.x);
            var roiMinY = Mathf.Max(0, (int)cp.CoverageArea.y);
            shader.SetInts(ShaderProperties.TerrainOffset, 0, 0);
            shader.SetInts(ShaderProperties.AlphamapOffset, roiMinX, roiMinY);
            shader.SetVector(ShaderProperties.ContourBounds, cp.ContourBounds);

            // coverage_min / coverage_max 来自 CoverageArea
            var minX = (int)cp.CoverageArea.x;
            var minY = (int)cp.CoverageArea.y;
            var maxX = (int)cp.CoverageArea.z;
            var maxY = (int)cp.CoverageArea.w;
            shader.SetInts(ShaderProperties.CoverageMin, minX, minY);
            shader.SetInts(ShaderProperties.CoverageMax, maxX, maxY);

            // 遮罩阈值：预览与正式保持一致的语义
            var threshold = isPreview ? 0.2f : 0f;
            shader.SetFloat(ShaderProperties.MaskThreshold, threshold);
            shader.SetInt(ShaderProperties.UseRoadMask, dataPacket.RoadMask != null ? 1 : 0);

            // 绑定缓冲区
            if (dataPacket.SpineBuffer != null)
            {
                shader.SetBuffer(kernel, ShaderProperties.SpineDataBuffer, dataPacket.SpineBuffer);
            }
            if (dataPacket.ContourBuffer != null)
            {
                shader.SetBuffer(kernel, ShaderProperties.ContourDataBuffer, dataPacket.ContourBuffer);
            }
            if (dataPacket.LayerParamsBuffer != null)
            {
                shader.SetBuffer(kernel, ShaderProperties.LayerParamsBuffer, dataPacket.LayerParamsBuffer);
            }

            // 绑定纹理
            if (dataPacket.AlphaMapTexture != null)
            {
                shader.SetTexture(kernel, ShaderProperties.SplatWeights, dataPacket.AlphaMapTexture);
            }
            if (dataPacket.RoadMask != null)
            {
                shader.SetTexture(kernel, ShaderProperties.RoadMask, dataPacket.RoadMask);
            }
            else
            {
                // 兜底绑定：避免 Unity 对未设置的 Texture2D 属性在 Dispatch 时抛出错误
                shader.SetTexture(kernel, ShaderProperties.RoadMask, Texture2D.blackTexture);
            }
            // 可选：SDF 路面距离场（目前未使用）
            // if (roadSDFTexture != null) shader.SetTexture(kernel, ShaderProperties.RoadSDF, roadSDFTexture);

            // 绑定统一噪声LUT（供 BlendMaskLibrary.hlsl 使用）
            var lut = NoiseLutProvider.GetOrCreateLut();
            if (lut != null)
            {
                shader.SetInt(ShaderProperties.NoiseLutSize, lut.width);
                shader.SetTexture(kernel, ShaderProperties.NoiseLUT, lut);
            }
            else
            {
                shader.SetInt(ShaderProperties.NoiseLutSize, 0);
            }
        }
        #endregion

        #region Thread Group Calculation
        private Vector3Int CalculateThreadGroups(Vector2Int resolution, Vector3Int threadGroupSize)
        {
            // 计算需要的线程组数量，确保覆盖所有像素
            int groupsX = Mathf.CeilToInt((float)resolution.x / threadGroupSize.x);
            int groupsY = Mathf.CeilToInt((float)resolution.y / threadGroupSize.y);
            int groupsZ = 1; // 2D纹理只需要一层

            return new Vector3Int(groupsX, groupsY, groupsZ);
        }

        /// <summary>
        /// 基于 ROI（CoverageArea）计算最小线程组数量，仅调度受影响区域
        /// </summary>
        private Vector3Int CalculateThreadGroupsROI(GpuDataStreamer.GpuComputeParams cp, Vector3Int threadGroupSize)
        {
            var roiWidth = Mathf.Max(1, (int)(cp.CoverageArea.z - cp.CoverageArea.x));
            var roiHeight = Mathf.Max(1, (int)(cp.CoverageArea.w - cp.CoverageArea.y));

            // 防御：如果覆盖区域异常，回退到全分辨率
            if (roiWidth <= 0 || roiHeight <= 0)
            {
                return CalculateThreadGroups(cp.Resolution, threadGroupSize);
            }

            int groupsX = Mathf.CeilToInt((float)roiWidth / threadGroupSize.x);
            int groupsY = Mathf.CeilToInt((float)roiHeight / threadGroupSize.y);
            int groupsZ = 1;

            return new Vector3Int(groupsX, groupsY, groupsZ);
        }
        #endregion

        #region Advanced Compute Features
        /// <summary>
        /// 执行带有自定义遮罩的计算
        /// </summary>
        public RenderTexture ExecuteComputeWithMask(GpuDataStreamer.GpuDataPacket dataPacket,
            Texture2D roadMask, Texture2D roadSDF, bool isPreview)
        {
            ValidateState();
            ValidateDataPacket(dataPacket);

            if (!_paintTerrainShader.IsValid)
            {
                throw new InvalidOperationException("地形绘制着色器未正确加载");
            }

            try
            {
                var shader = _paintTerrainShader.Shader;
                var kernel = _paintTerrainShader.KernelIndex;

                // 绑定基础参数（含预览阈值）
                BindComputeParameters(dataPacket, isPreview);

                // 绑定额外的遮罩纹理
                if (roadMask != null)
                {
                    shader.SetTexture(kernel, ShaderProperties.RoadMask, roadMask);
                }

                if (roadSDF != null)
                {
                    shader.SetTexture(kernel, ShaderProperties.RoadSDF, roadSDF);
                }

                // 计算并调度（使用 ROI 局部调度）
                var cp = dataPacket.ComputeParams;
                var threadGroups = CalculateThreadGroupsROI(cp, _paintTerrainShader.ThreadGroupSize);

                using (var cmd = new CommandBuffer { name = "TerrainPaintWithMask" })
                {
                    cmd.DispatchCompute(shader, kernel, threadGroups.x, threadGroups.y, threadGroups.z);
                    Graphics.ExecuteCommandBuffer(cmd);
                }

                return dataPacket.AlphaMapTexture;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuComputeDispatcher] fF0 执行带遮罩的计算失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 异步执行计算（用于大型地形）
        /// </summary>
        public AsyncGPUReadbackRequest ExecuteComputeAsync(GpuDataStreamer.GpuDataPacket dataPacket, bool isPreview)
        {
            ValidateState();
            ValidateDataPacket(dataPacket);

            // 先执行同步计算
            var resultTexture = ExecuteCompute(dataPacket, isPreview);

            // 启动异步读回
            return AsyncGPUReadback.Request(resultTexture);
        }
        #endregion

        #region Performance Monitoring
        /// <summary>
        /// 获取GPU性能统计
        /// </summary>
        public GpuPerformanceStats GetPerformanceStats()
        {
            // 这里可以添加GPU性能监控逻辑
            return new GpuPerformanceStats
            {
                LastDispatchTime = Time.realtimeSinceStartup,
                ThreadGroupsDispatched = 0, // 实际实现中需要跟踪
                MemoryUsage = 0 // 实际实现中需要计算
            };
        }
        #endregion

        #region Validation
        private void ValidateState()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GpuComputeDispatcher));

            if (!_isInitialized)
                throw new InvalidOperationException("GpuComputeDispatcher 未初始化");
        }

        private void ValidateDataPacket(GpuDataStreamer.GpuDataPacket dataPacket)
        {
            if (!dataPacket.IsValid)
                throw new ArgumentException("GPU数据包无效");

            if (dataPacket.AlphaMapTexture == null)
                throw new ArgumentException("AlphaMap纹理为空");

            if (dataPacket.SpineBuffer == null)
                throw new ArgumentException("脊柱数据缓冲区为空");

            if (dataPacket.LayerParamsBuffer == null)
                throw new ArgumentException("图层参数缓冲区为空");
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                // 清理工作在GpuResourceManager中统一处理
                _paintTerrainShader = default;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuComputeDispatcher] 清理资源时出错: {ex.Message}");
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
    /// GPU性能统计信息
    /// </summary>
    public struct GpuPerformanceStats
    {
        public float LastDispatchTime;
        public int ThreadGroupsDispatched;
        public long MemoryUsage;

        public override string ToString()
        {
            return $"LastDispatch: {LastDispatchTime:F3}s, ThreadGroups: {ThreadGroupsDispatched}, Memory: {MemoryUsage} bytes";
        }
    }
    #endregion
}
