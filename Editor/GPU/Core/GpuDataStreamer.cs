using System;
using MrPathV2.Editor.GPU.Core;
using MrPathV2.Runtime.Core;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Editor.GPU
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
        // 旧版携带多余向量，compute 仅需位置。已改为直接上传 Vector3 队列以降低带宽。

        /// <summary>
        /// 轮廓点数据结构
        /// </summary>
        // 旧版定义包含法线且按 XYZ 打包，compute 仅需 XZ 平面坐标，现改为上传 Vector2(x,z)。

        /// <summary>
        /// 与 HLSL layer_params 精确对齐的结构体（顺序与字段大小必须一致）。
        /// 对应 PaintSplatmapCompute.compute 中的：
        /// struct layer_params { int blend_mode; float opacity; int texture_index; int terrain_layer_splat_index; float4 tiling_offset; float4 tint_color; GpuMaskParams mask_params; };
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct LayerParamData
        {
            public int BlendMode;                 // layer.blend_mode
            public float Opacity;                 // layer.opacity
            public int TextureIndex;              // layer.texture_index（当前未使用，填0）
            public int TerrainLayerSplatIndex;   // layer.terrain_layer_splat_index（映射到 Terrain 的 splat 索引）
            public Vector4 TilingOffset;         // layer.tiling_offset（当前未使用，填0）
            public Vector4 TintColor;            // layer.tint_color（当前未使用，填1）
            public GpuMaskParams MaskParams;     // layer.mask_params（按需填充，默认“无遮罩”）
        }

        // 以下 GPU 遮罩参数结构体需与 Editor/Resources/BlendMaskLibrary.hlsl 保持字节对齐
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct GpuNoiseMaskParams
        {
            public float Strength;
            public float Seed;
            public Vector2 Tiling;      // x,y
            public Vector2 Offset;      // x,y
            public float OverallScale;
            public float Smooth;
            public Vector2 NoiseScale;  // x,y
            public float RotationRad;
            public int Octaves;
            public float Lacunarity;
            public float Gain;
            public int UseAsymmetricEdges;
            public float EdgeLow;
            public float EdgeHigh;
            public float Pad1;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct GpuShoulderMaskParams
        {
            public float Width;
            public float Softness;
            public float Strength;
            public float OverallScale;
            public float Smooth;
            public float Pad;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct GpuMaskParams
        {
            public int Type;
            public float Strength;
            public Vector2 Padding;
            public GpuNoiseMaskParams Noise;
            public GpuShoulderMaskParams Shoulder;
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

                // 5. 计算GPU参数（在生成RoadMask前计算，因为需要CoverageArea）
                packet.ComputeParams = CalculateComputeParams(terrain, pathData, recipe);

                // 6. 加载MaskAtlas compute shader（假设从Resources加载）
                ComputeShader maskCompute = Resources.Load<ComputeShader>("MaskAtlas");

                // 7. 生成道路遮罩纹理（使用GPU版本，传递buffer）
                packet.RoadMask = GenerateRoadMask(packet.ComputeParams, maskCompute, packet.SpineBuffer, packet.ContourBuffer);

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

            // compute 侧读取 float3，并以 xz 作为 2D 计算平面，这里直接上传世界坐标 Vector3
            var spinePositions = new Vector3[pathData.SpinePoints.Length];
            Array.Copy(pathData.SpinePoints, spinePositions, spinePositions.Length);
            return _resourceManager.GetOrCreateComputeBuffer("SpineData", spinePositions);
        }

        private ComputeBuffer PrepareContourData(PathData pathData)
        {
            if (pathData.ContourPoints == null || pathData.ContourPoints.Length == 0)
            {
                // 如果没有轮廓数据，创建空缓冲区
                var empty = new Vector2[1];
                return _resourceManager.GetOrCreateComputeBuffer("ContourData", empty);
            }

            // compute 读取 float2，并在世界 XZ 平面做点在多边形测试；只上传 (x,z)
            var contourXZ = new Vector2[pathData.ContourPoints.Length];
            for (int i = 0; i < pathData.ContourPoints.Length; i++)
            {
                var p = pathData.ContourPoints[i];
                contourXZ[i] = new Vector2(p.x, p.z);
            }

            return _resourceManager.GetOrCreateComputeBuffer("ContourData", contourXZ);
        }

        private ComputeBuffer PrepareLayerParams(PathRecipe recipe)
        {
            if (recipe.Layers == null || recipe.Layers.Length == 0)
                throw new ArgumentException("图层配置无效");

            var layerParams = new LayerParamData[recipe.Layers.Length];
            for (int i = 0; i < recipe.Layers.Length; i++)
            {
                var layer = recipe.Layers[i];

                // 计算遮罩参数：优先从 LayerConfig.Mask 填充，否则回退到无遮罩（Type=0, Strength=1）
                var maskParams = new GpuMaskParams
                {
                    Type = 0,
                    Strength = 1f,
                    Padding = Vector2.zero,
                    Noise = new GpuNoiseMaskParams(),
                    Shoulder = new GpuShoulderMaskParams()
                };

                if (layer.Mask != null)
                {
                    var dto = new GpuMaskParamsData();
                    layer.Mask.FillGpuParams(ref dto);
                    maskParams = ConvertToGpuMaskParams(dto);
                }

                layerParams[i] = new LayerParamData
                {
                    // 注意：compute 侧的 BlendWeight 期望的枚举序数为：
                    // 0=Normal(覆盖), 1=Multiply, 2=Add, 3=Overlay, 4=Screen, 5=Lerp, 6=Additive
                    // 而编辑器 GPU 枚举为：Replace(0), Add(1), Multiply(2), Overlay(3)
                    // 这里做一次转换，避免 Add/Multiply 语义对调。
                    BlendMode = MapToComputeBlendOrdinal(layer.BlendMode),
                    Opacity = Mathf.Clamp01(layer.Strength),
                    TextureIndex = 0,
                    TerrainLayerSplatIndex = layer.LayerIndex,
                    TilingOffset = Vector4.zero,
                    TintColor = new Vector4(1, 1, 1, 1),
                    MaskParams = maskParams
                };
            }

            return _resourceManager.GetOrCreateComputeBuffer("LayerParams", layerParams);
        }

        // 将编辑器侧 GPU BlendMode 映射为 compute/HLSL 侧期望的整数序数
        // Editor GPU BlendMode: Replace(0), Add(1), Multiply(2), Overlay(3)
        // HLSL BlendWeight:     0=Normal,   1=Multiply, 2=Add,     3=Overlay
        private static int MapToComputeBlendOrdinal(BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Replace: return 0;   // Normal/Override
                case BlendMode.Add: return 2;   // Add
                case BlendMode.Multiply: return 1;   // Multiply
                case BlendMode.Overlay: return 3;   // Overlay
                default: return 0;
            }
        }

        // 将运行时 DTO（全字段）转换为本管线 compute 所用的紧凑 GPU 结构
        private static GpuMaskParams ConvertToGpuMaskParams(GpuMaskParamsData src)
        {
            var result = new GpuMaskParams
            {
                Type = src.MaskType,
                Strength = src.Strength,
                Padding = Vector2.zero,
                Noise = new GpuNoiseMaskParams(),
                Shoulder = new GpuShoulderMaskParams()
            };

            // 噪声遮罩映射（字段一一对应/或近似）
            if (src.MaskType == 2) // MASK_TYPE_NOISE
            {
                result.Noise = new GpuNoiseMaskParams
                {
                    Strength = src.NoiseParams.Strength,
                    Seed = src.NoiseParams.Seed,
                    Tiling = new Vector2(src.NoiseParams.Tiling.x, src.NoiseParams.Tiling.y),
                    Offset = new Vector2(src.NoiseParams.Offset.x, src.NoiseParams.Offset.y),
                    OverallScale = src.NoiseParams.OverallScale,
                    Smooth = src.NoiseParams.Smooth,
                    NoiseScale = new Vector2(src.NoiseParams.NoiseScale.x, src.NoiseParams.NoiseScale.y),
                    RotationRad = src.NoiseParams.RotationRad,
                    Octaves = src.NoiseParams.Octaves,
                    Lacunarity = src.NoiseParams.Lacunarity,
                    Gain = src.NoiseParams.Gain,
                    UseAsymmetricEdges = src.NoiseParams.UseAsymmetricEdges ? 1 : 0,
                    EdgeLow = src.NoiseParams.EdgeLow,
                    EdgeHigh = src.NoiseParams.EdgeHigh,
                    Pad1 = 0f
                };
            }
            else if (src.MaskType == 1) // MASK_TYPE_SHOULDER
            {
                // 计算侧重点：将更全面的 DTO 映射到 compute 侧精简结构
                result.Shoulder = new GpuShoulderMaskParams
                {
                    Width = src.ShoulderParams.ShoulderWidthRatio,
                    Softness = src.ShoulderParams.EdgeFalloff,
                    Strength = src.ShoulderParams.ShoulderStrength,
                    OverallScale = src.ShoulderParams.OverallScale,
                    Smooth = src.ShoulderParams.Smooth,
                    Pad = 0f
                };
            }

            return result;
        }

        private RenderTexture PrepareAlphaMapTexture(UnityEngine.Terrain terrain)
        {
            var terrainData = terrain.terrainData;
            var alphamapResolution = terrainData.alphamapResolution;
            var layerCount = terrainData.alphamapLayers;

            // 创建或获取AlphaMap纹理数组
            var key = $"AlphaMap_{terrain.GetInstanceID()}";
            // 注意：一个 alphamap 切片包含 4 个图层（RGBA），因此体纹理深度应为 ceil(layers/4)
            var sliceCount = Mathf.Max(1, Mathf.CeilToInt(layerCount / 4f));
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
            int width = terrainData.alphamapWidth;
            int height = terrainData.alphamapHeight;
            int totalLayers = terrainData.alphamapLayers;
            int sliceCount = Mathf.Max(1, Mathf.CeilToInt(totalLayers / 4f));

            // 获取现有 alphamap 数据一次性读取
            var alphamaps = terrainData.GetAlphamaps(0, 0, width, height);

            for (int slice = 0; slice < sliceCount; slice++)
            {
                var sliceTex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                var colors = new Color[width * height];

                int baseLayer = slice * 4;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        float r = (baseLayer + 0) < totalLayers ? alphamaps[x, y, baseLayer + 0] : 0f;
                        float g = (baseLayer + 1) < totalLayers ? alphamaps[x, y, baseLayer + 1] : 0f;
                        float b = (baseLayer + 2) < totalLayers ? alphamaps[x, y, baseLayer + 2] : 0f;
                        float a = (baseLayer + 3) < totalLayers ? alphamaps[x, y, baseLayer + 3] : 0f;
                        colors[idx] = new Color(r, g, b, a);
                    }
                }

                sliceTex.SetPixels(colors);
                sliceTex.Apply();

                Graphics.CopyTexture(sliceTex, 0, 0, alphaMapTexture, slice, 0);

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(sliceTex);
                else
                    UnityEngine.Object.DestroyImmediate(sliceTex);
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

            int totalLayers = terrainData.alphamapLayers;
            int sliceCount = Mathf.Max(rt.volumeDepth, 1);
            int expectedSliceCount = Mathf.Max(1, Mathf.CeilToInt(totalLayers / 4f));
            if (sliceCount != expectedSliceCount)
            {
                Debug.LogWarning($"[GpuDataStreamer] 体纹理深度({sliceCount})与层数划分({expectedSliceCount})不一致，按 {expectedSliceCount} 处理");
                sliceCount = expectedSliceCount;
            }

            // 逐 slice ROI 读回（每 slice 对应4个 terrain layers）
            int completed = 0;
            for (int slice = 0; slice < sliceCount; slice++)
            {
                int capturedSlice = slice;
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0,
                    minX, roiWidth,
                    minY, roiHeight,
                    capturedSlice, 1,
                    request =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError($"[GpuDataStreamer] GPU读回失败（slice {capturedSlice}）");
                        return;
                    }

                    var data = request.GetData<Color>();
                    int baseLayer = capturedSlice * 4;

                    for (int y = 0; y < roiHeight; y++)
                    {
                        for (int x = 0; x < roiWidth; x++)
                        {
                            int idx = y * roiWidth + x;
                            if (idx >= data.Length) continue;

                            var c = data[idx];
                            if (baseLayer + 0 < totalLayers) roiMaps[y, x, baseLayer + 0] = Mathf.Clamp01(c.r);
                            if (baseLayer + 1 < totalLayers) roiMaps[y, x, baseLayer + 1] = Mathf.Clamp01(c.g);
                            if (baseLayer + 2 < totalLayers) roiMaps[y, x, baseLayer + 2] = Mathf.Clamp01(c.b);
                            if (baseLayer + 3 < totalLayers) roiMaps[y, x, baseLayer + 3] = Mathf.Clamp01(c.a);
                        }
                    }

                    completed++;
                    if (completed >= sliceCount)
                    {
                        // 归一化：保证每个像素所有层之和为1
                        for (int y = 0; y < roiHeight; y++)
                        {
                            for (int x = 0; x < roiWidth; x++)
                            {
                                float sum = 0f;
                                for (int l = 0; l < totalLayers; l++) sum += roiMaps[y, x, l];
                                if (sum > 1e-6f)
                                {
                                    for (int l = 0; l < totalLayers; l++) roiMaps[y, x, l] /= sum;
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
            RoadContourGenerator.GenerateContour(pathSpine, tempProfile, out NativeArray<float2> roadContour, out _, Allocator.Temp);

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





        #endregion

        #region Validation



        #endregion







        // 在GpuDataStreamer类中添加新方法（单一职责：仅负责RoadMask生成）
        private Texture2D GenerateRoadMask(GpuComputeParams computeParams, ComputeShader maskCompute, ComputeBuffer spineBuffer, ComputeBuffer contourBuffer)
        {
            // 提前返回：如果无contour数据，直接返回空mask
            if (computeParams.ContourPointCount <= 0) return null;

            // 计算RoadMask分辨率（基于CoverageArea，确保精确ROI）
            int width = (int)computeParams.CoverageArea.z - (int)computeParams.CoverageArea.x;
            int height = (int)computeParams.CoverageArea.w - (int)computeParams.CoverageArea.y;
            if (width <= 0 || height <= 0) return null; // 提前返回：无效分辨率

            Texture2D roadMask = new Texture2D(width, height, TextureFormat.R8, false);
            roadMask.filterMode = FilterMode.Point; // 现代风格：点采样以提升性能

            // 设置ComputeShader参数（使用Unity API替换自定义实现）
            int kernel = maskCompute.FindKernel("BuildRoadMask"); // 假设扩展MaskAtlas.compute添加此内核
            if (kernel < 0)
            {
                Debug.LogError("BuildRoadMask kernel not found");
                UnityEngine.Object.DestroyImmediate(roadMask);
                return null;
            }

            maskCompute.SetTexture(kernel, "_RoadMask", roadMask);
            maskCompute.SetBuffer(kernel, "_SpineBuffer", spineBuffer);
            maskCompute.SetBuffer(kernel, "_ContourBuffer", contourBuffer);
            maskCompute.SetInts("_Resolution", width, height);
            maskCompute.SetVector("_TerrainPosition", computeParams.TerrainPosition);
            maskCompute.SetFloat("_PathWidth", computeParams.PathWidth);
            maskCompute.SetVector("_CoverageArea", computeParams.CoverageArea);

            // Dispatch优化：线程组基于分辨率动态计算，提升效率
            int threadGroupsX = Mathf.CeilToInt(width / 8f);
            int threadGroupsY = Mathf.CeilToInt(height / 8f);
            maskCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            return roadMask;
        }

        // 在CalculateComputeParams或相关方法中调用并绑定（应用提前返回）
        void UpdateRoadMaskAndBind(UnityEngine.Terrain terrain, Material shaderMaterial, GpuComputeParams computeParams, ComputeShader maskCompute, PathData pathData, PathRecipe recipe)
        {
            Texture2D roadMask = GenerateRoadMask(terrain, pathData, recipe);
            if (roadMask == null) return; // 提前返回：无mask时跳过绑定

            // 绑定到shader（移除硬编码参数，确保一致性）
            shaderMaterial.SetTexture("_RoadMask", roadMask);
            shaderMaterial.SetVector("_RoadMaskBounds", new Vector4(computeParams.CoverageArea.x, computeParams.CoverageArea.y, computeParams.CoverageArea.x, computeParams.CoverageArea.y));
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
