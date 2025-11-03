using System;
using __temp.MrPathV2.Editor.GPU.Core;
using __temp.MrPathV2.Runtime.Core;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace __temp.MrPathV2.Editor.GPU
{
    /// <summary>
    /// GPU数据流传输器 - 高效的CPU-GPU数据传输
    /// 设计原则：最小化数据拷贝，批量传输，异步处理
    /// </summary>
    public sealed class GpuDataStreamer : IDisposable
    {
        #region Dependencies
        private readonly GpuResourceManager _resourceManager;
        #endregion

        #region Data Structures
        /// <summary>
        /// GPU数据包 - 包含所有GPU计算所需的数据
        /// </summary>
        public struct GpuDataPacket
        {
            public ComputeBuffer SpineBuffer;
            public ComputeBuffer ContourBuffer;
            public ComputeBuffer LayerParamsBuffer;
            public RenderTexture AlphaMapTexture;
            public Texture2D RoadMask;
            public GpuComputeParams ComputeParams;
            public bool IsValid;
        }

        /// <summary>
        /// GPU计算参数
        /// </summary>
        public struct GpuComputeParams
        {
            public Vector2Int Resolution;
            public Vector3 TerrainPosition;
            public Vector3 TerrainSize;
            public float PathWidth;
            public float PathLength;
            public float FalloffDistance;
            public int LayerCount;
            public Vector4 CoverageArea; // x,y = min, z,w = max (in pixel coordinates)
            public Vector4 ContourBounds; // x,y = min, z,w = max (in world coordinates)
            // 新增：着色器所需的计数/层数
            public int AlphamapLayerCount;
            public int SpinePointCount;
            public int ContourPointCount;
            public float EdgeWidthWorld;
        }

        /// <summary>
        /// 脊柱点数据结构
        /// </summary>
        public struct SpinePointData
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public float Distance;
        }

        /// <summary>
        /// 轮廓点数据结构
        /// </summary>
        public struct ContourPointData
        {
            public Vector3 Position;
            public Vector3 Normal;
        }

        /// <summary>
        /// 图层参数数据结构
        /// </summary>
        public struct LayerParamData
        {
            public int LayerIndex;
            public float Strength;
            public int BlendMode;
            public Vector4 MaskParams; // 用于存储遮罩相关参数
        }
        #endregion

        #region State
        private bool _isInitialized;
        private bool _isDisposed;
        #endregion

        #region Constructor
        public GpuDataStreamer(GpuResourceManager resourceManager)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        }
        #endregion

        #region Initialization
        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }
        #endregion

        #region Public API
        /// <summary>
        /// 准备GPU数据包
        /// </summary>
        public GpuDataPacket PrepareGpuData(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe)
        {
            ValidateState();
            ValidateInputs(terrain, pathData, recipe);

            try
            {
                var packet = new GpuDataPacket();

                // 1. 准备脊柱数据
                packet.SpineBuffer = PrepareSpineData(pathData);

                // 2. 准备轮廓数据
                packet.ContourBuffer = PrepareContourData(pathData);

                // 3. 准备图层参数
                packet.LayerParamsBuffer = PrepareLayerParams(recipe);

                // 4. 准备或获取AlphaMap纹理
                packet.AlphaMapTexture = PrepareAlphaMapTexture(terrain);

                // 5. 生成道路遮罩纹理
                packet.RoadMask = GenerateRoadMask(terrain, pathData, recipe);

                // 6. 计算GPU参数
                packet.ComputeParams = CalculateComputeParams(terrain, pathData, recipe);

                packet.IsValid = true;
                return packet;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuDataStreamer] 准备GPU数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 应用渲染结果到地形（使用 ROI 与异步读回）
        /// </summary>
        public void ApplyToTerrain(GpuRenderResult result)
        {
            ValidateState();
            if (result?.RenderTexture == null || result.Terrain == null)
                throw new ArgumentException("无效的渲染结果");

            try
            {
                if (result.IsPreview)
                {
                    // 预览模式不写回地形
                    return;
                }

                ApplyRenderTextureToTerrainROI(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuDataStreamer] 应用到地形失败: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Data Preparation Methods
        private ComputeBuffer PrepareSpineData(PathData pathData)
        {
            if (pathData.SpinePoints == null || pathData.SpinePoints.Length == 0)
                throw new ArgumentException("脊柱点数据无效");

            // 构建增强的脊柱数据
            var spineData = new SpinePointData[pathData.SpinePoints.Length];
            float accumulatedDistance = 0f;

            for (int i = 0; i < pathData.SpinePoints.Length; i++)
            {
                var point = pathData.SpinePoints[i];

                // 计算前向向量
                Vector3 forward = Vector3.forward;
                if (i < pathData.SpinePoints.Length - 1)
                {
                    forward = (pathData.SpinePoints[i + 1] - point).normalized;
                }
                else if (i > 0)
                {
                    forward = (point - pathData.SpinePoints[i - 1]).normalized;
                }

                // 计算右向向量
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

                // 累积距离
                if (i > 0)
                {
                    accumulatedDistance += Vector3.Distance(pathData.SpinePoints[i - 1], point);
                }

                spineData[i] = new SpinePointData
                {
                    Position = point,
                    Forward = forward,
                    Right = right,
                    Distance = accumulatedDistance
                };
            }

            return _resourceManager.GetOrCreateComputeBuffer("SpineData", spineData);
        }

        private ComputeBuffer PrepareContourData(PathData pathData)
        {
            if (pathData.ContourPoints == null || pathData.ContourPoints.Length == 0)
            {
                // 如果没有轮廓数据，创建空缓冲区
                var emptyData = new ContourPointData[1];
                return _resourceManager.GetOrCreateComputeBuffer("ContourData", emptyData);
            }

            var contourData = new ContourPointData[pathData.ContourPoints.Length];
            for (int i = 0; i < pathData.ContourPoints.Length; i++)
            {
                contourData[i] = new ContourPointData
                {
                    Position = pathData.ContourPoints[i],
                    Normal = Vector3.up // 默认法向量，可以根据需要计算
                };
            }

            return _resourceManager.GetOrCreateComputeBuffer("ContourData", contourData);
        }

        private ComputeBuffer PrepareLayerParams(PathRecipe recipe)
        {
            if (recipe.Layers == null || recipe.Layers.Length == 0)
                throw new ArgumentException("图层配置无效");

            var layerParams = new LayerParamData[recipe.Layers.Length];
            for (int i = 0; i < recipe.Layers.Length; i++)
            {
                var layer = recipe.Layers[i];
                layerParams[i] = new LayerParamData
                {
                    LayerIndex = layer.LayerIndex,
                    Strength = layer.Strength,
                    BlendMode = (int)layer.BlendMode,
                    MaskParams = Vector4.zero // 可扩展的遮罩参数
                };
            }

            return _resourceManager.GetOrCreateComputeBuffer("LayerParams", layerParams);
        }

        private RenderTexture PrepareAlphaMapTexture(UnityEngine.Terrain terrain)
        {
            var terrainData = terrain.terrainData;
            var alphamapResolution = terrainData.alphamapResolution;
            var layerCount = terrainData.alphamapLayers;

            // 创建或获取AlphaMap纹理数组
            var key = $"AlphaMap_{terrain.GetInstanceID()}";
            var sliceCount = Mathf.Max(layerCount, 1); // 避免 volumeDepth=0
            var alphaMapTexture = _resourceManager.GetOrCreateRenderTextureEx(
                key, alphamapResolution, alphamapResolution, sliceCount, RenderTextureFormat.ARGBFloat, out var createdNew);

            // 仅在首次创建或尺寸/层数变化时同步一次，避免每次绘制的大规模 CPU 拷贝
            if (createdNew)
            {
                SyncAlphaMapToTexture(terrain, alphaMapTexture);
            }

            return alphaMapTexture;
        }

        private void SyncAlphaMapToTexture(UnityEngine.Terrain terrain, RenderTexture alphaMapTexture)
        {
            var terrainData = terrain.terrainData;
            var alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

            // 使用Graphics.CopyTexture进行高效拷贝
            for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
            {
                var layerTexture = new Texture2D(terrainData.alphamapWidth, terrainData.alphamapHeight, TextureFormat.RGBAFloat, false);
                var colors = new Color[terrainData.alphamapWidth * terrainData.alphamapHeight];

                for (int y = 0; y < terrainData.alphamapHeight; y++)
                {
                    for (int x = 0; x < terrainData.alphamapWidth; x++)
                    {
                        int index = y * terrainData.alphamapWidth + x;
                        float alpha = layer < terrainData.alphamapLayers ? alphamaps[x, y, layer] : 0f;
                        colors[index] = new Color(alpha, alpha, alpha, alpha);
                    }
                }

                layerTexture.SetPixels(colors);
                layerTexture.Apply();

                Graphics.CopyTexture(layerTexture, 0, 0, alphaMapTexture, layer, 0);

                // 清理临时纹理
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(layerTexture);
                else
                    UnityEngine.Object.DestroyImmediate(layerTexture);
            }
        }

        private GpuComputeParams CalculateComputeParams(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe)
        {
            var terrainData = terrain.terrainData;
            var terrainPos = terrain.transform.position;
            var terrainSize = terrainData.size;

            // 计算覆盖区域（像素坐标）
            var coverageArea = CalculateCoverageArea(pathData.PathBounds, terrainPos, terrainSize, terrainData.alphamapResolution);

            return new GpuComputeParams
            {
                Resolution = new Vector2Int(terrainData.alphamapResolution, terrainData.alphamapResolution),
                TerrainPosition = terrainPos,
                TerrainSize = terrainSize,
                PathWidth = pathData.PathWidth,
                PathLength = pathData.PathLength,
                FalloffDistance = recipe.FalloffDistance,
                LayerCount = recipe.Layers.Length,
                CoverageArea = coverageArea,
                ContourBounds = new Vector4(pathData.PathBounds.min.x, pathData.PathBounds.min.z,
                                          pathData.PathBounds.max.x, pathData.PathBounds.max.z),
                AlphamapLayerCount = terrainData.alphamapLayers,
                SpinePointCount = pathData.SpinePoints != null ? pathData.SpinePoints.Length : 0,
                ContourPointCount = pathData.ContourPoints != null ? pathData.ContourPoints.Length : 0,
                EdgeWidthWorld = recipe.FalloffDistance
            };
        }

        private Vector4 CalculateCoverageArea(Bounds pathBounds, Vector3 terrainPos, Vector3 terrainSize, int resolution)
        {
            // 将世界坐标转换为像素坐标
            var minX = Mathf.FloorToInt(((pathBounds.min.x - terrainPos.x) / terrainSize.x) * resolution);
            var minY = Mathf.FloorToInt(((pathBounds.min.z - terrainPos.z) / terrainSize.z) * resolution);
            var maxX = Mathf.CeilToInt(((pathBounds.max.x - terrainPos.x) / terrainSize.x) * resolution);
            var maxY = Mathf.CeilToInt(((pathBounds.max.z - terrainPos.z) / terrainSize.z) * resolution);

            // 确保在有效范围内
            minX = Mathf.Clamp(minX, 0, resolution);
            minY = Mathf.Clamp(minY, 0, resolution);
            maxX = Mathf.Clamp(maxX, 0, resolution);
            maxY = Mathf.Clamp(maxY, 0, resolution);

            return new Vector4(minX, minY, maxX, maxY);
        }
        #endregion

        #region Terrain Application
        private void ApplyRenderTextureToTerrainROI(GpuRenderResult result)
        {
            var terrain = result.Terrain;
            var terrainData = terrain.terrainData;
            var rt = result.RenderTexture;

            // 计算 ROI（像素坐标）
            var cov = result.CoverageArea;
            int minX = Mathf.Clamp(Mathf.RoundToInt(cov.x), 0, terrainData.alphamapWidth);
            int minY = Mathf.Clamp(Mathf.RoundToInt(cov.y), 0, terrainData.alphamapHeight);
            int maxX = Mathf.Clamp(Mathf.RoundToInt(cov.z), 0, terrainData.alphamapWidth);
            int maxY = Mathf.Clamp(Mathf.RoundToInt(cov.w), 0, terrainData.alphamapHeight);
            // 注意：coverageMax 通常为“包含型”边界，和 CPU 路径保持一致需 +1
            int roiWidth = Mathf.Max(1, maxX - minX + 1);
            int roiHeight = Mathf.Max(1, maxY - minY + 1);

            // 准备 ROI alphamaps
            var roiMaps = terrainData.GetAlphamaps(minX, minY, roiWidth, roiHeight);

            int sliceCount = Mathf.Max(rt.volumeDepth, 1);
            int targetLayerCount = Mathf.Min(sliceCount, terrainData.alphamapLayers);

            // 逐 slice ROI 读回，并在全部完成后一次性归一化与写回
            int completed = 0;
            for (int layer = 0; layer < targetLayerCount; layer++)
            {
                int slice = layer;
                // 读取 ROI 子矩形，仅该 slice
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0,
                    minX, roiWidth,
                    minY, roiHeight,
                    slice, 1,
                    request =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError($"[GpuDataStreamer] GPU读回失败（slice {slice}）");
                        return;
                    }

                    var data = request.GetData<Color>(); // 返回的就是该 slice 的 ROI 数据
                    // 将 ROI 数据写入对应层
                    for (int y = 0; y < roiHeight; y++)
                    {
                        for (int x = 0; x < roiWidth; x++)
                        {
                            int idx = y * roiWidth + x;
                            if (idx < data.Length)
                            {
                                roiMaps[y, x, slice] = Mathf.Clamp01(data[idx].r);
                            }
                        }
                    }

                    completed++;
                    if (completed >= targetLayerCount)
                    {
                        // 可选：归一化，保证每个像素各层权重之和为1（更符合地形渲染期望）
                        for (int y = 0; y < roiHeight; y++)
                        {
                            for (int x = 0; x < roiWidth; x++)
                            {
                                float sum = 0f;
                                for (int l = 0; l < targetLayerCount; l++) sum += roiMaps[y, x, l];
                                if (sum > 1e-6f)
                                {
                                    for (int l = 0; l < targetLayerCount; l++) roiMaps[y, x, l] /= sum;
                                }
                            }
                        }

                        terrainData.SetAlphamaps(minX, minY, roiMaps);
                    }
                });
            }
        }
        #endregion

        #region Road Mask Generation
        /// <summary>
        /// 生成道路遮罩纹理，只在道路覆盖的区域绘制
        /// </summary>
        private Texture2D GenerateRoadMask(UnityEngine.Terrain terrain, PathData pathData, PathRecipe recipe)
        {
            var terrainData = terrain.terrainData;
            var alphaMapResolution = terrainData.alphamapResolution;

            // 创建遮罩纹理
            var maskTexture = new Texture2D(alphaMapResolution, alphaMapResolution, TextureFormat.R8, false);
            var pixels = new byte[alphaMapResolution * alphaMapResolution];

            // 获取地形世界坐标和尺寸
            var terrainPos = terrain.transform.position;
            var terrainSize = terrainData.size;

            // 仅在 ROI 内生成遮罩，避免整图扫描造成卡顿
            var coverage = CalculateCoverageArea(pathData.PathBounds, terrainPos, terrainSize, alphaMapResolution);
            int minX = Mathf.Clamp(Mathf.RoundToInt(coverage.x), 0, alphaMapResolution);
            int minY = Mathf.Clamp(Mathf.RoundToInt(coverage.y), 0, alphaMapResolution);
            int maxX = Mathf.Clamp(Mathf.RoundToInt(coverage.z), 0, alphaMapResolution);
            int maxY = Mathf.Clamp(Mathf.RoundToInt(coverage.w), 0, alphaMapResolution);
            int roiWidth = Mathf.Max(1, maxX - minX + 1);
            int roiHeight = Mathf.Max(1, maxY - minY + 1);

            // 从 PathData 创建简化的 PathSpine
            var pathSpine = CreateSimplifiedPathSpine(pathData);

            // 创建临时的 PathProfile 来使用 RoadContourGenerator
            var tempProfile = CreateTempPathProfile(recipe);

            // 生成道路轮廓
            NativeArray<float2> roadContour;
            float4 bounds;
            RoadContourGenerator.GenerateContour(pathSpine, tempProfile, out roadContour, out bounds, Allocator.Temp);

            try
            {
                // 仅处理 ROI 子区域，其他区域保持默认 0
                for (int y = 0; y < roiHeight; y++)
                {
                    int texY = minY + y;
                    for (int x = 0; x < roiWidth; x++)
                    {
                        int texX = minX + x;
                        // 将纹理坐标转换为世界坐标
                        float worldX = terrainPos.x + (texX / (float)alphaMapResolution) * terrainSize.x;
                        float worldZ = terrainPos.z + (texY / (float)alphaMapResolution) * terrainSize.z;

                        // 检查点是否在道路轮廓内
                        bool isInsideRoad = IsPointInsideRoadContour(worldX, worldZ, roadContour);

                        // 设置像素值：道路内为255（白色），道路外为0（黑色）
                        pixels[texY * alphaMapResolution + texX] = isInsideRoad ? (byte)255 : (byte)0;
                    }
                }

                // 应用像素数据
                maskTexture.LoadRawTextureData(pixels);
                maskTexture.Apply();

                return maskTexture;
            }
            finally
            {
                // 清理 NativeArray
                if (roadContour.IsCreated)
                    roadContour.Dispose();
            }
        }

        /// <summary>
        /// 从 PathData 创建简化的 PathSpine
        /// </summary>
        private PathSpine CreateSimplifiedPathSpine(PathData pathData)
        {
            if (pathData.SpinePoints == null || pathData.SpinePoints.Length < 2)
            {
                return new PathSpine(new Vector3[0], new Vector3[0], new Vector3[0], new float[0]);
            }

            var points = pathData.SpinePoints;
            var tangents = new Vector3[points.Length];
            var normals = new Vector3[points.Length];
            var timestamps = new float[points.Length];

            // 计算切线
            for (int i = 0; i < points.Length; i++)
            {
                if (i == 0)
                {
                    tangents[i] = points.Length > 1 ? (points[1] - points[0]).normalized : Vector3.forward;
                }
                else if (i == points.Length - 1)
                {
                    tangents[i] = (points[i] - points[i - 1]).normalized;
                }
                else
                {
                    tangents[i] = (points[i + 1] - points[i - 1]).normalized;
                }

                normals[i] = Vector3.up; // 简化的法线
                timestamps[i] = i / (float)(points.Length - 1); // 归一化时间戳
            }

            return new PathSpine(points, tangents, normals, timestamps);
        }

        /// <summary>
        /// 创建临时的 PathProfile 用于轮廓生成
        /// </summary>
        private PathProfile CreateTempPathProfile(PathRecipe recipe)
        {
            var tempProfile = ScriptableObject.CreateInstance<PathProfile>();

            // 从 recipe 中提取道路宽度信息
            // 使用 FalloffDistance * 2 作为道路宽度的估算
            tempProfile.roadWidth = recipe.FalloffDistance * 2f;
            tempProfile.falloffWidth = recipe.FalloffDistance * 0.5f;

            return tempProfile;
        }

        /// <summary>
        /// 检查点是否在道路轮廓内（使用射线投射算法）
        /// </summary>
        private bool IsPointInsideRoadContour(float x, float z, NativeArray<float2> roadContour)
        {
            if (roadContour.Length < 3) return false;

            bool inside = false;
            int j = roadContour.Length - 1;

            for (int i = 0; i < roadContour.Length; i++)
            {
                var pi = roadContour[i];
                var pj = roadContour[j];

                if (((pi.y > z) != (pj.y > z)) &&
                    (x < (pj.x - pi.x) * (z - pi.y) / (pj.y - pi.y) + pi.x))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }
        #endregion

        #region Validation
        private void ValidateState()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GpuDataStreamer));

            if (!_isInitialized)
                throw new InvalidOperationException("GpuDataStreamer 未初始化");
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

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                // 清理工作在GpuResourceManager中统一处理
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuDataStreamer] 清理资源时出错: {ex.Message}");
            }
            finally
            {
                _isDisposed = true;
                _isInitialized = false;
            }
        }
        #endregion
    }
}
