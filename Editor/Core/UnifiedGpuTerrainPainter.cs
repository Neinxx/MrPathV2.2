using System;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一GPU地形绘制器（示例/过渡版本）
    /// </summary>
    public sealed class UnifiedGpuTerrainPainter : IUnifiedTerrainPainter, IDisposable
    {
        private ComputeShader _paintShader;

        public UnifiedGpuTerrainPainter()
        {
            InitializeGpuResources();
        }

        public bool IsSupported => SystemInfo.supportsComputeShaders;
        public PainterType Type => PainterType.GPU;

        private void InitializeGpuResources()
        {
            // 加载计算着色器
            _paintShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");
            if (_paintShader == null)
            {
                UnityEngine.Debug.LogError("[UnifiedGpuTerrainPainter] 无法加载计算着色器 'PaintSplatmapCompute'");
            }
        }

        public void Dispose()
        {
            // 示例版本无需额外释放
        }

        // 简化的参数结构（示例）
        private struct GpuComputeParams
        {
            public Vector2Int Resolution;
            public Vector3 TerrainPosition;
            public Vector3 TerrainSize;
            public float PathWidth;
            public float FalloffDistance;
        }

        public async Task<TerrainPaintResult> PaintAsync(PathCreator pathCreator, bool isPreview = false, CancellationToken cancellationToken = default)
        {
            var result = Paint(pathCreator, isPreview);
            await Task.Yield();
            return result;
        }

        public TerrainPaintResult Paint(PathCreator pathCreator, bool isPreview = false)
        {
            if (_paintShader == null)
            {
                return TerrainPaintResult.CreateFailure("计算着色器未加载", PainterType.GPU);
            }

            if (pathCreator == null)
            {
                return TerrainPaintResult.CreateFailure("PathCreator为空", PainterType.GPU);
            }

            var pathProfile = pathCreator.profile;
            var pathData = pathCreator.pathData;

            if (pathProfile == null)
            {
                return TerrainPaintResult.CreateFailure("PathProfile为空", PainterType.GPU);
            }

            // 查找与PathCreator关联的地形
            var terrain = FindNearestTerrain(pathCreator.transform.position);
            if (terrain == null)
            {
                return TerrainPaintResult.CreateFailure("无法找到相关地形", PainterType.GPU);
            }

            var computeParams = new GpuComputeParams
            {
                Resolution = new Vector2Int(terrain.terrainData.alphamapResolution, terrain.terrainData.alphamapResolution),
                TerrainPosition = terrain.transform.position,
                TerrainSize = terrain.terrainData.size,
                PathWidth = pathProfile.roadWidth,
                FalloffDistance = pathProfile.falloffWidth
            };

            try
            {
                // 1. 生成真实路径遮罩
                var roadMask = GenerateRoadMaskFromPath(terrain, pathData, pathProfile);

                // 2. 执行着色器
                var rt = ExecuteComputeShader(computeParams, roadMask, pathData, terrain, isPreview);

                // 3. 应用到地形
                if (!isPreview)
                {
                    ApplyToTerrain(terrain, rt, pathProfile);
                }

                // 4. 创建预览纹理
                Texture2D previewTexture = null;
                if (isPreview)
                {
                    // 注册到全局预览缓存，供材质预览直接使用权重数组
                    global::MrPathV2.Editor.Terrain.GpuPreviewCache.Register(terrain, rt);
                    previewTexture = CreatePreviewTexture(rt);
                }

                // 5. 清理资源
                if (roadMask != null) UnityEngine.Object.DestroyImmediate(roadMask);
                if (!isPreview && rt != null) rt.Release();

                return TerrainPaintResult.CreateSuccess(PainterType.GPU, 0f, previewTexture);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedGpuTerrainPainter] 绘制失败: {ex.Message}");
                return TerrainPaintResult.CreateFailure(ex.Message, PainterType.GPU);
            }
        }

        private Texture2D GenerateRoadMaskFromPath(UnityEngine.Terrain terrain, PathData pathData, PathProfile pathProfile)
        {
            var terrainData = terrain.terrainData;
            var resolution = terrainData.alphamapResolution;
            var terrainPos = terrain.transform.position;
            var terrainSize = terrainData.size;

            // 创建遮罩纹理
            var maskTexture = new Texture2D(resolution, resolution, TextureFormat.R8, false);
            var pixels = new byte[resolution * resolution];

            // 使用路径数据生成真实的道路遮罩
            if (pathData != null && pathProfile != null)
            {
                // 创建路径脊线 - 修复构造函数调用
                var knotCount = pathData.KnotCount;
                if (knotCount == 0) return maskTexture;

                var points = new Vector3[knotCount];
                var tangents = new Vector3[knotCount];
                var normals = new Vector3[knotCount];
                var timestamps = new float[knotCount];

                // 简易法线与切线估算（基于相邻点差分），时间戳使用索引
                for (int i = 0; i < knotCount; i++)
                {
                    var knot = pathData.GetKnot(i);
                    points[i] = knot.Position;
                    timestamps[i] = i;

                    Vector3 prev = i > 0 ? pathData.GetKnot(i - 1).Position : knot.Position;
                    Vector3 next = i < knotCount - 1 ? pathData.GetKnot(i + 1).Position : knot.Position;
                    var tangent = (next - prev);
                    tangents[i] = tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
                    normals[i] = Vector3.up;
                }

                var spine = new PathSpine(points, tangents, normals, timestamps);

                // 获取路径宽度参数
                float roadWidth = pathProfile.roadWidth;
                float falloffWidth = pathProfile.falloffWidth;
                float totalWidth = roadWidth + falloffWidth * 2f;

                // 计算路径包围盒（世界坐标）并加上过渡宽度的外扩
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < points.Length; i++)
                {
                    var p = points[i];
                    min.x = Mathf.Min(min.x, p.x);
                    min.y = Mathf.Min(min.y, p.z);
                    max.x = Mathf.Max(max.x, p.x);
                    max.y = Mathf.Max(max.y, p.z);
                }
                // 外扩到道路+过渡宽度
                var expand = totalWidth * 0.5f;
                min -= new Vector2(expand, expand);
                max += new Vector2(expand, expand);

                // 将世界坐标包围盒转换为像素坐标ROI
                float invSizeX = resolution / terrainSize.x;
                float invSizeZ = resolution / terrainSize.z;
                int roiMinX = Mathf.Clamp(Mathf.FloorToInt((min.x - terrainPos.x) * invSizeX), 0, resolution - 1);
                int roiMaxX = Mathf.Clamp(Mathf.CeilToInt((max.x - terrainPos.x) * invSizeX), 0, resolution - 1);
                int roiMinY = Mathf.Clamp(Mathf.FloorToInt((min.y - terrainPos.z) * invSizeZ), 0, resolution - 1);
                int roiMaxY = Mathf.Clamp(Mathf.CeilToInt((max.y - terrainPos.z) * invSizeZ), 0, resolution - 1);

                // 仅在ROI内计算，显著减少像素计算量
                for (int y = roiMinY; y <= roiMaxY; y++)
                {
                    for (int x = roiMinX; x <= roiMaxX; x++)
                    {
                        // 将像素坐标转换为世界坐标
                        float worldX = terrainPos.x + (float)x / resolution * terrainSize.x;
                        float worldZ = terrainPos.z + (float)y / resolution * terrainSize.z;
                        Vector3 worldPos = new Vector3(worldX, 0, worldZ);

                        // 计算到路径的最短距离
                        float distance = CalculateDistanceToPath(spine, worldPos);

                        // 计算遮罩值
                        byte maskValue = 0;
                        if (distance <= roadWidth * 0.5f)
                        {
                            maskValue = 255; // 道路内部
                        }
                        else if (distance <= roadWidth * 0.5f + falloffWidth)
                        {
                            float t = 1.0f - (distance - roadWidth * 0.5f) / falloffWidth;
                            maskValue = (byte)(t * 255);
                        }

                        pixels[y * resolution + x] = maskValue;
                    }
                }
            }

            maskTexture.LoadRawTextureData(pixels);
            maskTexture.Apply();

            return maskTexture;
        }

        // 添加计算到路径最短距离的方法
        private float CalculateDistanceToPath(PathSpine spine, Vector3 worldPos)
        {
            if (spine.Points == null || spine.Points.Length < 2)
                return float.MaxValue;

            float minDistance = float.MaxValue;
            Vector3 worldPos2D = new Vector3(worldPos.x, 0, worldPos.z);

            // 遍历所有路径段，找到最短距离
            for (int i = 0; i < spine.Points.Length - 1; i++)
            {
                Vector3 segmentStart = new Vector3(spine.Points[i].x, 0, spine.Points[i].z);
                Vector3 segmentEnd = new Vector3(spine.Points[i + 1].x, 0, spine.Points[i + 1].z);

                // 计算点到线段的最短距离
                Vector3 segmentVector = segmentEnd - segmentStart;
                Vector3 pointVector = worldPos2D - segmentStart;

                float segmentLengthSq = Vector3.Dot(segmentVector, segmentVector);

                // 处理极短线段
                if (segmentLengthSq < 0.0001f)
                {
                    float distToPoint = Vector3.Distance(worldPos2D, segmentStart);
                    minDistance = Mathf.Min(minDistance, distToPoint);
                    continue;
                }

                // 计算投影点参数 t
                float t = Mathf.Clamp01(Vector3.Dot(pointVector, segmentVector) / segmentLengthSq);

                // 计算投影点
                Vector3 closestPoint = segmentStart + t * segmentVector;

                // 计算距离
                float distance = Vector3.Distance(worldPos2D, closestPoint);
                minDistance = Mathf.Min(minDistance, distance);
            }

            return minDistance;
        }

        private void ApplyToTerrain(UnityEngine.Terrain terrain, RenderTexture rt, PathProfile pathProfile)
        {
            if (rt == null || terrain == null || pathProfile == null || pathProfile.roadRecipe == null)
                return;

            var terrainData = terrain.terrainData;
            var resolution = terrainData.alphamapResolution;
            var layerCount = terrainData.alphamapLayers;

            // 解析地形层映射
            var layerMap = LayerResolver.ResolveEnsurePresentSmart(terrain, pathProfile.roadRecipe);
            if (layerMap == null || layerMap.Count == 0)
                return;

            // 读取当前地形纹理
            var alphamaps = terrainData.GetAlphamaps(0, 0, resolution, resolution);

            if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray)
            {
                // 从每个切片读取权重（使用R通道），写入对应层
                int sliceCount = Mathf.Min(layerCount, rt.volumeDepth);
                for (int layer = 0; layer < sliceCount; layer++)
                {
                    // 使用与源纹理一致的格式，避免 CopyTexture 因内存大小不同报错
                    var sliceRT = new RenderTexture(resolution, resolution, 0, rt.format)
                    {
                        enableRandomWrite = false
                    };
                    sliceRT.Create();
                    Graphics.CopyTexture(rt, layer, 0, sliceRT, 0, 0);

                    var tempTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
                    RenderTexture.active = sliceRT;
                    tempTex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    tempTex.Apply();
                    RenderTexture.active = null;

                    var pixels = tempTex.GetPixels();
                    for (int y = 0; y < resolution; y++)
                    {
                        for (int x = 0; x < resolution; x++)
                        {
                            var c = pixels[y * resolution + x];
                            alphamaps[y, x, layer] = c.r;
                        }
                    }

                    sliceRT.Release();
                    UnityEngine.Object.DestroyImmediate(sliceRT);
                    UnityEngine.Object.DestroyImmediate(tempTex);
                }

                terrainData.SetAlphamaps(0, 0, alphamaps);
                terrain.Flush();
            }
            else
            {
                // 创建临时纹理来读取渲染纹理数据（旧路径，单纹理，RGBA映射前四层）
                var tempTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                tempTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                tempTexture.Apply();
                RenderTexture.active = null;

                var texPixels = tempTexture.GetPixels();
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        var maskValue = texPixels[y * resolution + x].r; // [0,1]
                        if (maskValue <= 0.01f) continue; // 提前返回：无需修改

                        // 应用层不透明度；避免对 maskValue 再次插值造成双重衰减
                        foreach (var layer in pathProfile.roadRecipe.layers)
                        {
                            if (layer == null) continue;
                            if (!layerMap.TryGetValue(layer.contentLayer, out int layerIndex)) continue;
                            if (layerIndex < 0 || layerIndex >= layerCount) continue;

                            float strength = Mathf.Clamp01(layer.opacity * maskValue);
                            alphamaps[y, x, layerIndex] = strength;
                        }

                        // 归一化一次即可，避免每层重复归一化带来的开销
                        NormalizeWeights(alphamaps, y, x, layerCount);
                    }
                }

                terrainData.SetAlphamaps(0, 0, alphamaps);
                terrain.Flush();
                UnityEngine.Object.DestroyImmediate(tempTexture);
            }
        }

        private void NormalizeWeights(float[,,] alphamaps, int y, int x, int layerCount)
        {
            float sum = 0;
            for (int i = 0; i < layerCount; i++)
            {
                sum += alphamaps[y, x, i];
            }

            if (sum > 0.01f)
            {
                for (int i = 0; i < layerCount; i++)
                {
                    alphamaps[y, x, i] /= sum;
                }
            }
        }

        private Texture2D CreatePreviewTexture(RenderTexture rt)
        {
            if (rt == null)
                return null;

            var resolution = rt.width;
            var previewTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            // 如果是Texture2DArray，则拷贝第0层到二维纹理进行预览
            if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray)
            {
                // 防御：数组层数为0则直接返回空预览，避免拷贝错误
                if (rt.volumeDepth <= 0)
                {
                    return null;
                }

                var sliceRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
                {
                    enableRandomWrite = false
                };
                sliceRT.Create();
                Graphics.CopyTexture(rt, 0, 0, sliceRT, 0, 0);
                RenderTexture.active = sliceRT;
                previewTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                previewTexture.Apply();
                RenderTexture.active = null;
                sliceRT.Release();
                UnityEngine.Object.DestroyImmediate(sliceRT);
            }
            else
            {
                RenderTexture.active = rt;
                previewTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                previewTexture.Apply();
                RenderTexture.active = null;
            }

            return previewTexture;
        }

        // 查找最近的地形
        private UnityEngine.Terrain FindNearestTerrain(Vector3 position)
        {
            // 获取场景中的所有地形
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
                return null;

            // 如果只有一个地形，直接返回
            if (terrains.Length == 1)
                return terrains[0];

            // 查找最近的地形
            UnityEngine.Terrain nearestTerrain = null;
            float nearestDistance = float.MaxValue;

            foreach (var terrain in terrains)
            {
                if (terrain == null)
                    continue;

                // 检查点是否在地形范围内
                var terrainPos = terrain.transform.position;
                var terrainSize = terrain.terrainData.size;

                // 如果点在地形范围内，直接返回该地形
                if (position.x >= terrainPos.x && position.x <= terrainPos.x + terrainSize.x &&
                    position.z >= terrainPos.z && position.z <= terrainPos.z + terrainSize.z)
                {
                    return terrain;
                }

                // 计算到地形中心的距离
                var terrainCenter = terrainPos + new Vector3(terrainSize.x * 0.5f, 0, terrainSize.z * 0.5f);
                var distance = Vector3.Distance(new Vector3(position.x, 0, position.z),
                                              new Vector3(terrainCenter.x, 0, terrainCenter.z));

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTerrain = terrain;
                }
            }

            return nearestTerrain;
        }

        private RenderTexture ExecuteComputeShader(GpuComputeParams computeParams, Texture2D roadMask, PathData pathData, UnityEngine.Terrain terrain, bool isPreview)
        {
            var kernelIndex = _paintShader.FindKernel("paint_terrain");
            if (kernelIndex < 0)
            {
                throw new InvalidOperationException("无法找到计算着色器内核 'paint_terrain'");
            }

            // 创建输出权重纹理数组（与地形alphamap层数一致；至少为1，避免RT创建失败）
            var rawLayerCount = terrain.terrainData.alphamapLayers;
            var layerCount = Mathf.Max(rawLayerCount, 1);
            var splatWeights = new RenderTexture(
                computeParams.Resolution.x,
                computeParams.Resolution.y,
                0,
                RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = true,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = layerCount
            };
            splatWeights.Create();

            // 准备spine_data缓冲（使用路径knot位置，世界坐标）
            int knotCount = pathData?.KnotCount ?? 0;
            Vector3[] spinePoints = new Vector3[knotCount];
            for (int i = 0; i < knotCount; i++)
            {
                spinePoints[i] = pathData.GetKnot(i).Position;
            }
            var spineBuffer = new ComputeBuffer(spinePoints.Length, sizeof(float) * 3);
            if (spinePoints.Length > 0) spineBuffer.SetData(spinePoints);

            // 设置计算着色器参数
            _paintShader.SetTexture(kernelIndex, "road_mask", roadMask);
            _paintShader.SetTexture(kernelIndex, "splat_weights", splatWeights);

            // 地形参数（按compute期望的float2）
            var pos = terrain.GetPosition();
            var size = terrain.terrainData.size;
            _paintShader.SetVector("terrain_position", new Vector4(pos.x, pos.z, 0f, 0f));
            _paintShader.SetVector("terrain_size", new Vector4(size.x, size.z, 0f, 0f));

            // 路径与衰减
            // 统一参数名称：着色器使用 road_width
            _paintShader.SetFloat("road_width", computeParams.PathWidth);
            _paintShader.SetFloat("falloff_distance", computeParams.FalloffDistance);
            // 遮罩阈值与边缘宽度（与计算着色器一致）：阈值用于 road_mask 门控；边缘宽度用于软化道路边缘
            _paintShader.SetFloat("mask_threshold", 0.5f);
            _paintShader.SetFloat("edge_width_world", Mathf.Max(0.0001f, computeParams.FalloffDistance));
            // 遮罩开关：未绑定遮罩时不做遮罩门控，仍仅在 ROI 内执行
            _paintShader.SetInt("use_road_mask", roadMask ? 1 : 0);

            // 分辨率与层数
            _paintShader.SetInts("alphamap_resolution", computeParams.Resolution.x, computeParams.Resolution.y);
            _paintShader.SetInt("alphamap_layer_count", layerCount);

            // 覆盖范围（路径 ROI）
            int covMinX = 0, covMinY = 0, covMaxX = computeParams.Resolution.x - 1, covMaxY = computeParams.Resolution.y - 1;
            try
            {
                // 基于 PathData 的结点计算路径包围盒，并按宽度+衰减外扩
                if (pathData != null && pathData.KnotCount > 0)
                {
                    var first = pathData.GetKnot(0).Position;
                    float minX = first.x, maxX = first.x;
                    float minZ = first.z, maxZ = first.z;
                    for (int i = 1; i < pathData.KnotCount; i++)
                    {
                        var p = pathData.GetKnot(i).Position;
                        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                        if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                    }

                    var margin = Mathf.Max(0.25f, computeParams.PathWidth * 0.5f + computeParams.FalloffDistance);
                    minX -= margin; maxX += margin;
                    minZ -= margin; maxZ += margin;

                    var terrainPos = terrain.GetPosition();
                    var terrainSize = terrain.terrainData.size;
                    int res = computeParams.Resolution.x; // square assumption

                    covMinX = Mathf.Clamp(Mathf.FloorToInt(((minX - terrainPos.x) / terrainSize.x) * res), 0, res - 1);
                    covMaxX = Mathf.Clamp(Mathf.CeilToInt(((maxX - terrainPos.x) / terrainSize.x) * res), 0, res - 1);
                    covMinY = Mathf.Clamp(Mathf.FloorToInt(((minZ - terrainPos.z) / terrainSize.z) * res), 0, res - 1);
                    covMaxY = Mathf.Clamp(Mathf.CeilToInt(((maxZ - terrainPos.z) / terrainSize.z) * res), 0, res - 1);
                }
            }
            catch { /* 安全回退到全图 */ }

            _paintShader.SetInts("coverage_min", covMinX, covMinY);
            _paintShader.SetInts("coverage_max", covMaxX, covMaxY);

            // 默认参数与禁用图层混合（无layer_params_buffer）
            _paintShader.SetInt("layer_count", 0);
            _paintShader.SetInt("contour_point_count", 0);
            _paintShader.SetVector("contour_bounds", Vector4.zero);
            _paintShader.SetFloat("mask_threshold", 0.5f);
            _paintShader.SetFloat("edge_width_world", computeParams.FalloffDistance);

            // 绑定spine数据
            if (spinePoints.Length > 0)
            {
                _paintShader.SetBuffer(kernelIndex, "spine_data", spineBuffer);
                _paintShader.SetInt("spine_point_count", spinePoints.Length);
            }
            else
            {
                _paintShader.SetInt("spine_point_count", 0);
            }

            // 计算线程组数量
            var threadGroupsX = Mathf.CeilToInt(computeParams.Resolution.x / 8.0f);
            var threadGroupsY = Mathf.CeilToInt(computeParams.Resolution.y / 8.0f);

            // 执行计算着色器
            _paintShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

            // 释放缓冲区
            spineBuffer.Release();

            return splatWeights;
        }
    }
}
