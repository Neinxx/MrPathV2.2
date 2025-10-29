using System;
using System.Threading;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using EditorGpuPreviewCache = MrPathV2.Editor.Terrain.GpuPreviewCache;
#endif
using System.Collections.Generic;
using MrPathV2.Editor.Settings;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    ///     Editor 实时 GPU 预览运行器：
    ///     - 计算道路覆盖区域
    ///     - 构建 Spine/Profile/Recipe 的 GPU 输入
    ///     - 调度 PaintSplatmapCompute 到缓存的 RenderTexture (Tex2DArray)
    ///     注意：不会读回并写回到 Terrain，仅用于预览材质绑定。
    /// </summary>
    public static class GpuPreviewRunner
    {
        // Shader property IDs (保持与 GpuTerrainPainter 一致)
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("alphamap_resolution");
        private static readonly int AlphamapLayerCountID = Shader.PropertyToID("alphamap_layer_count");
        private static readonly int TerrainPositionID = Shader.PropertyToID("terrain_position");
        private static readonly int TerrainSizeID = Shader.PropertyToID("terrain_size");
        private static readonly int SpinePointsID = Shader.PropertyToID("_SpinePoints");
        private static readonly int SpineDataID = Shader.PropertyToID("spine_data");
        private static readonly int SpinePointCountID = Shader.PropertyToID("spine_point_count");
        private static readonly int RoadContourID = Shader.PropertyToID("road_contour");
        private static readonly int ContourPointCountID = Shader.PropertyToID("contour_point_count");
        private static readonly int RoadWidthID = Shader.PropertyToID("road_width");
        private static readonly int PathLengthID = Shader.PropertyToID("_PathLength");
        private static readonly int CoverageMinID = Shader.PropertyToID("coverage_min");
        private static readonly int CoverageMaxID = Shader.PropertyToID("coverage_max");
        private static readonly int LayerParamsID = Shader.PropertyToID("layer_params_buffer");
        private static readonly int LayerCountID = Shader.PropertyToID("layer_count");
        private static readonly int AlphamapOffsetID = Shader.PropertyToID("alphamap_offset");
        private static readonly int TerrainOffsetID = Shader.PropertyToID("terrain_offset");
        private static readonly int FalloffDistanceID = Shader.PropertyToID("falloff_distance");
    
        private static readonly int SplatWeightsID = Shader.PropertyToID("splat_weights");
    
        // 新增：道路遮罩与边界、阈值、边缘宽度等必要属性
        private static readonly int RoadMaskTexID = Shader.PropertyToID("road_mask");
        private static readonly int MaskThresholdID = Shader.PropertyToID("mask_threshold");
        private static readonly int EdgeWidthWorldID = Shader.PropertyToID("edge_width_world");
        private static readonly int ContourBoundsID = Shader.PropertyToID("contour_bounds");

        /// <summary>
        /// 运行一次 GPU 预览调度。仅在 Editor 下有效。
        /// </summary>
        public static bool TryRun(UnityEngine.Terrain terrain, PathSpine spine, PathProfile profile,
            CancellationToken token = default)
        {
#if !UNITY_EDITOR
            return false;
#endif
            try
            {
                Debug.Log(
                    $"[GpuPreviewRunner] TryRun called - terrain: {terrain?.name}, spine vertices: {spine.VertexCount}, profile: {profile?.name}");

                if (terrain == null || terrain.terrainData == null || profile == null || spine.VertexCount < 2)
                {
                    Debug.LogWarning(
                        $"[GpuPreviewRunner] Early return - terrain: {terrain != null}, terrainData: {terrain?.terrainData != null}, profile: {profile != null}, spine vertices: {spine.VertexCount}");
                    return false;
                }

                var td = terrain.terrainData;

                // 延后获取 alphamap 信息，先确保地形图层已就绪

                // Load compute shader and kernel
                var compute = LoadPaintCompute();
                if (compute == null)
                {
                    Debug.LogError("[GpuPreviewRunner] Failed to load compute shader");
                    return false;
                }

                var kernel = compute.FindKernel("paint_terrain");
                if (kernel == -1)
                {
                    Debug.LogError("[GpuPreviewRunner] PaintTerrain kernel not found");
                    return false;
                }

                // 现在获取 alphamap 纹理与参数（确保图层已存在）
                var alphaMapTextures = td.alphamapTextures;
                if (alphaMapTextures == null || alphaMapTextures.Length == 0)
                {
                    Debug.LogWarning("[GpuPreviewRunner] No alphamap textures found");
                    return false;
                }

                var resolution = td.alphamapResolution;
                var layers = td.alphamapLayers;
                var format = alphaMapTextures[0].graphicsFormat;
                if (!IsGraphicsFormatRWCompatible(format))
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm;
                }

                Debug.Log(
                    $"[GpuPreviewRunner] Compute shader loaded successfully, resolution: {resolution}, layers: {layers}, format: {format}");

                // Build spine/profile data
                using var spineData = new PathJobsUtility.SpineData(spine, Allocator.TempJob);
                using var profileData = new PathJobsUtility.ProfileData(profile, Allocator.TempJob);
                if (!spineData.IsCreated || !profileData.IsCreated)
                {
                    Debug.LogWarning("[GpuPreviewRunner] Failed to create spine or profile data");
                    return false;
                }

                Debug.Log(
                    $"[GpuPreviewRunner] Spine data created - points: {spineData.Points.Length}, tangents: {spineData.Tangents.Length}, normals: {spineData.Normals.Length}");

                // Generate road contour & bounds (用于覆盖区域计算)
                RoadContourGenerator.GenerateContour(spine, profile, out var contour, out var contourBounds,
                    Allocator.TempJob);
                // NOTE: contour 在 finally 中统一释放，避免提前处置导致无法绑定到 Compute

                // Fallback: if contour bounds are degenerate (zero size), derive bounds from spine with margin
                {
                    var boundsWidth = contourBounds.z - contourBounds.x;
                    var boundsHeight = contourBounds.w - contourBounds.y;
                    var degenerate = boundsWidth <= 0f || boundsHeight <= 0f || float.IsNaN(boundsWidth) ||
                                     float.IsNaN(boundsHeight) || float.IsInfinity(boundsWidth) ||
                                     float.IsInfinity(boundsHeight);
                    if (degenerate)
                    {
                        var pts = spine.Points;
                        if (pts != null && pts.Length > 0)
                        {
                            var minX = pts[0].x;
                            var minZ = pts[0].z;
                            var maxX = pts[0].x;
                            var maxZ = pts[0].z;
                            for (var i = 1; i < pts.Length; i++)
                            {
                                var p = pts[i];
                                if (p.x < minX) minX = p.x;
                                if (p.z < minZ) minZ = p.z;
                                if (p.x > maxX) maxX = p.x;
                                if (p.z > maxZ) maxZ = p.z;
                            }

                            var margin = Mathf.Max(0.25f, profile.roadWidth * 0.5f + profile.falloffWidth);
                            contourBounds = new float4(minX - margin, minZ - margin, maxX + margin, maxZ + margin);
                            Debug.Log(
                                $"[GpuPreviewRunner] Contour bounds degenerate; using spine-derived fallback bounds: min=({contourBounds.x},{contourBounds.y}) max=({contourBounds.z},{contourBounds.w})");
                        }
                    }
                }

                // Determine coverage (复用 PaintTerrainCommand 的逻辑)
                var (useLimit, coverageMin, coverageMax) = CalculateCoverageArea(terrain, contourBounds);
                if (!useLimit)
                {
                    Debug.LogWarning("[GpuPreviewRunner] Coverage calculation failed");
                    return false;
                }

                var numPixelsX = coverageMax.x - coverageMin.x + 1;
                var numPixelsY = coverageMax.y - coverageMin.y + 1;
                if (numPixelsX <= 0 || numPixelsY <= 0)
                {
                    Debug.LogWarning($"[GpuPreviewRunner] Invalid pixel coverage - X: {numPixelsX}, Y: {numPixelsY}");
                    return false;
                }

                Debug.Log(
                    $"[GpuPreviewRunner] Coverage area - min: ({coverageMin.x}, {coverageMin.y}), max: ({coverageMax.x}, {coverageMax.y}), pixels: {numPixelsX}x{numPixelsY}");

                // Ensure GPU recipe data
                var recipeId = profile.roadRecipe ? profile.roadRecipe.GetInstanceID() : 0;
                if (!RecipeGpuCache.TryGetValue(recipeId, out var gpuData) || gpuData == null)
                {
                    gpuData = new RecipeGpuDataManager();
                    RecipeGpuCache[recipeId] = gpuData;
                }

                var layerMap = LayerResolver.ResolveEnsurePresent(terrain, profile.roadRecipe);
                gpuData.UpdateData(profile.roadRecipe, layerMap);
                if (gpuData.LayerParamsBuffer == null || !gpuData.LayerParamsBuffer.IsValid() ||
                    gpuData.ActiveLayerCount <= 0)
                {
                    Debug.LogWarning("[GpuPreviewRunner] GPU recipe data is invalid");
                    return false;
                }

                Debug.Log($"[GpuPreviewRunner] GPU recipe data ready - active layers: {gpuData.ActiveLayerCount}");

                // Acquire working RT (cached by terrain)
                var tempAlphaMaps = AcquireAlphaMapRT(terrain, alphaMapTextures, resolution, format);
                if (!tempAlphaMaps || !tempAlphaMaps.IsCreated())
                {
                    Debug.LogError("[GpuPreviewRunner] Failed to acquire RenderTexture");
                    gpuData.Dispose();
                    return false;
                }

                SyncAlphaMapsToRT(alphaMapTextures, tempAlphaMaps);

                Debug.Log(
                    $"[GpuPreviewRunner] RenderTexture acquired - size: {tempAlphaMaps.width}x{tempAlphaMaps.height}, depth: {tempAlphaMaps.volumeDepth}");

                // Build compute buffers
                ComputeBuffer pointsBuffer = null;
                ComputeBuffer contourBuffer = null;
                try
                {
                    token.ThrowIfCancellationRequested();

                    // Spine 点缓冲区
                    pointsBuffer = SetupSpinePointsBuffer(spineData);

                    Debug.Log($"[GpuPreviewRunner] Spine points buffer created - points: {pointsBuffer.count}");

                    // 线程组尺寸与对齐覆盖区域
                    compute.GetKernelThreadGroupSizes(kernel, out var tx, out var ty, out var tz);
                    tx = tx == 0 ? 8u : tx;
                    ty = ty == 0 ? 8u : ty;
                    var remX = coverageMin.x % (int)tx;
                    if (remX < 0) remX += (int)tx;
                    var remY = coverageMin.y % (int)ty;
                    if (remY < 0) remY += (int)ty;
                    var alignedOffset = new int2(coverageMin.x - remX, coverageMin.y - remY);
                    var totalPixelsX = numPixelsX + remX;
                    var totalPixelsY = numPixelsY + remY;
                    var gx = Mathf.CeilToInt(totalPixelsX / (float)tx);
                    var gy = Mathf.CeilToInt(totalPixelsY / (float)ty);
                    var alignedStartX = Mathf.Clamp(alignedOffset.x, 0, resolution - 1);
                    var alignedStartY = Mathf.Clamp(alignedOffset.y, 0, resolution - 1);
                    var alignedEndX = Mathf.Clamp(alignedOffset.x + gx * (int)tx - 1, 0, resolution - 1);
                    var alignedEndY = Mathf.Clamp(alignedOffset.y + gy * (int)ty - 1, 0, resolution - 1);

                    // 高级设置：预览是否重建Basemap与安全边距
                    var adv = MrPathProjectSettings.GetOrCreateSettings()?.advancedSettings;
                    var rebuildPreviewBasemap = adv != null && adv.rebuildBasemapInRealtimePreview;
                    var margin = Mathf.Max(0, adv != null ? adv.basemapSafetyMarginPixels : 0);
                    var safeStartX = Mathf.Clamp(alignedStartX - margin, 0, resolution - 1);
                    var safeStartY = Mathf.Clamp(alignedStartY - margin, 0, resolution - 1);
                    var safeEndX = Mathf.Clamp(alignedEndX + margin, 0, resolution - 1);
                    var safeEndY = Mathf.Clamp(alignedEndY + margin, 0, resolution - 1);
                    var safeW = safeEndX - safeStartX + 1;
                    var safeH = safeEndY - safeStartY + 1;
                    Debug.Log(
                        $"[GpuPreviewRunner] Aligned coverage [{alignedStartX},{alignedStartY}]~[{alignedEndX},{alignedEndY}], groups {gx}x{gy}, safe margin {margin} -> {safeW}x{safeH}");


                    // 绑定对齐后的覆盖窗口
                    BindCommonParams(compute, terrain, td, profileData, new int2(alignedStartX, alignedStartY),
                        new int2(alignedEndX, alignedEndY), resolution, layers, spineData, contourBounds);

                    // 绑定必要输入：SpineData、LayerParamsBuffer、_LayerCount、_SplatWeights
                    compute.SetBuffer(kernel, SpineDataID, pointsBuffer);
                    compute.SetInt(SpinePointCountID, spineData.Points.Length);

                    compute.SetInt(LayerCountID, gpuData.ActiveLayerCount);
                    compute.SetBuffer(kernel, LayerParamsID, gpuData.LayerParamsBuffer);

                    // 绑定轮廓缓冲与参数（如可用）
                    if (contour.IsCreated && contour.Length > 0)
                    {
                        contourBuffer = new ComputeBuffer(contour.Length, sizeof(float) * 2);
                        contourBuffer.SetData(contour);
                        compute.SetBuffer(kernel, RoadContourID, contourBuffer);
                        compute.SetInt(ContourPointCountID, contour.Length);
                    }
                    else
                    {
                        compute.SetInt(ContourPointCountID, 0);
                    }

                    // 遮罩与边缘宽度（与 Compute 内逻辑一致）
                    compute.SetFloat(MaskThresholdID, 0.5f);
                    compute.SetFloat(EdgeWidthWorldID, profile.falloffWidth);

                    compute.SetTexture(kernel, SplatWeightsID, tempAlphaMaps);

                    // 调度（按对齐后的组数）
                    if (gx <= 0 || gy <= 0)
                    {
                        Debug.LogError($"[GpuPreviewRunner] Invalid dispatch groups - gx: {gx}, gy: {gy}");
                        gpuData.Dispose();
                        return false;
                    }

                    compute.Dispatch(kernel, gx, gy, 1);

                    // 可选：实时预览重建 Basemap（安全边距矩形）
                    if (!rebuildPreviewBasemap || safeW <= 0 || safeH <= 0) return true;
                    try
                    {
                        GpuTerrainPainter.RebuildBasemapFromRTRegion(td, tempAlphaMaps, layers, safeStartX,
                            safeStartY, safeW, safeH);
#if UNITY_EDITOR
                        terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, 100000f);
                        terrain.Flush();
                        EditorUtility.SetDirty(td);
#else
                            terrain.Flush();
#endif
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[GpuPreviewRunner] Basemap rebuild (preview) failed: {e.Message}");
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("[GpuPreviewRunner] Operation was cancelled");
                    return false;
                }
                finally
                {
                    pointsBuffer?.Release();
                    contourBuffer?.Release();
                    if (contour.IsCreated) contour.Dispose();
                    // gpuData is cached; do not dispose here.
#if !UNITY_EDITOR
                    tempAlphaMaps?.Release();
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuPreviewRunner] Exception occurred: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static ComputeShader s_PaintCompute;
        private static ComputeShader LoadPaintCompute()
        {
            if (s_PaintCompute != null) return s_PaintCompute;

            var cs = Resources.Load<ComputeShader>("PaintSplatmapCompute");
#if UNITY_EDITOR
            if (cs == null)
            {
                var guids = AssetDatabase.FindAssets("PaintSplatmapCompute t:ComputeShader");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (cs != null)
                    {
                        Debug.Log($"[GpuPreviewRunner] Loaded compute from: {path}");
                    }
                }
            }
#endif
            s_PaintCompute = cs;
            return s_PaintCompute;
        }

        private static ComputeBuffer SetupSpinePointsBuffer(PathJobsUtility.SpineData spineData)
        {
            var points = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
            if (spineData.Points.Length > 0) points.SetData(spineData.Points);
            return points;
        }

        private static bool IsGraphicsFormatRWCompatible(GraphicsFormat format)
        {
            RenderTexture rt = null;
            try
            {
                var desc = new RenderTextureDescriptor(8, 8);
                desc.graphicsFormat = format;
                desc.dimension = TextureDimension.Tex2DArray;
                desc.volumeDepth = 1;
                desc.enableRandomWrite = true;
                desc.useMipMap = false;

                rt = new RenderTexture(desc);
                if (!rt.Create())
                {
                    return false;
                }

                return rt.IsCreated();
            }
            catch
            {
                return false;
            }
            finally
            {
                if (rt != null)
                {
                    if (rt.IsCreated()) rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        private static void BindCommonParams(ComputeShader cs, UnityEngine.Terrain t, TerrainData data,
            PathJobsUtility.ProfileData prof, int2 covMin, int2 covMax, int res, int layerCount,
            PathJobsUtility.SpineData spineData, float4 contourBounds)
        {
            cs.SetInts(AlphamapResolutionID, res, res);
            cs.SetInt(AlphamapLayerCountID, layerCount);
            var tpos = t.GetPosition();
            cs.SetVector(TerrainPositionID, new Vector4(tpos.x, tpos.z, 0f, 0f));
            cs.SetVector(TerrainSizeID, new Vector4(data.size.x, data.size.z, 0f, 0f));
            cs.SetInts(AlphamapOffsetID, covMin.x, covMin.y);
            cs.SetInts(TerrainOffsetID, 0, 0);
            cs.SetFloat(FalloffDistanceID, Mathf.Max(0.001f, prof.RoadWidth * 0.6f));
            cs.SetFloat(RoadWidthID, prof.RoadWidth);
            cs.SetFloat(PathLengthID, CalculatePathLengthFromSpine(spineData));
            cs.SetInts(CoverageMinID, covMin.x, covMin.y);
            cs.SetInts(CoverageMaxID, covMax.x, covMax.y);
            cs.SetVector(ContourBoundsID, new Vector4(contourBounds.x, contourBounds.y, contourBounds.z, contourBounds.w));
        }

        private static (bool useLimit, int2 pixelMin, int2 pixelMax) CalculateCoverageArea(UnityEngine.Terrain terrain,
            float4 bounds)
        {
            var td = terrain.terrainData;
            var terrainBounds = new Bounds(terrain.GetPosition() + td.size / 2f, td.size);
            // 修正：正确解析 bounds = [minX, minZ, maxX, maxZ]，并用 (max-min) 作为尺寸、(min+max)/2 作为中心
            var roadMinX = bounds.x;
            var roadMinZ = bounds.y;
            var roadMaxX = bounds.z;
            var roadMaxZ = bounds.w;
            var roadCenter = new Vector3((roadMinX + roadMaxX) * 0.5f, terrainBounds.center.y,
                (roadMinZ + roadMaxZ) * 0.5f);
            var roadSizeX = Mathf.Max(0f, roadMaxX - roadMinX);
            var roadSizeZ = Mathf.Max(0f, roadMaxZ - roadMinZ);
            var roadBounds = new Bounds(roadCenter, new Vector3(roadSizeX, terrainBounds.size.y, roadSizeZ));

            // Correct intersection (not union) between terrain and road bounds
            var minX = Mathf.Max(terrainBounds.min.x, roadBounds.min.x);
            var minZ = Mathf.Max(terrainBounds.min.z, roadBounds.min.z);
            var maxX = Mathf.Min(terrainBounds.max.x, roadBounds.max.x);
            var maxZ = Mathf.Min(terrainBounds.max.z, roadBounds.max.z);

            // No overlap
            if (maxX <= minX || maxZ <= minZ)
            {
                return (false, default, default);
            }

            var resolution = td.alphamapResolution;
            var invSizeX = 1.0f / Mathf.Max(1e-5f, terrainBounds.size.x);
            var invSizeZ = 1.0f / Mathf.Max(1e-5f, terrainBounds.size.z);
            var terrainMinX = terrainBounds.min.x;
            var terrainMinZ = terrainBounds.min.z;

            var pixelMinX = Mathf.FloorToInt((minX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMinZ = Mathf.FloorToInt((minZ - terrainMinZ) * invSizeZ * (resolution - 1));
            var pixelMaxX = Mathf.CeilToInt((maxX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMaxZ = Mathf.CeilToInt((maxZ - terrainMinZ) * invSizeZ * (resolution - 1));

            pixelMinX = Mathf.Clamp(pixelMinX, 0, resolution - 1);
            pixelMinZ = Mathf.Clamp(pixelMinZ, 0, resolution - 1);
            pixelMaxX = Mathf.Clamp(pixelMaxX, 0, resolution - 1);
            pixelMaxZ = Mathf.Clamp(pixelMaxZ, 0, resolution - 1);

            return (true, new int2(pixelMinX, pixelMinZ), new int2(pixelMaxX, pixelMaxZ));
        }

        private static RenderTexture AcquireAlphaMapRT(UnityEngine.Terrain terrain, Texture2D[] alphaMapTextures,
            int resolution, GraphicsFormat format)
        {
            var td = terrain.terrainData;
            var requiredSlices = Mathf.CeilToInt(td.alphamapLayers / 4f);
            RenderTexture tempAlphaMaps = null;
#if UNITY_EDITOR
            if (EditorGpuPreviewCache.TryGet(terrain, out var cached) &&
                cached != null &&
                cached.width == resolution && cached.height == resolution &&
                cached.volumeDepth == requiredSlices &&
                cached.graphicsFormat == format)
            {
                tempAlphaMaps = cached;
            }
#endif
            if (tempAlphaMaps != null) return tempAlphaMaps;
            var desc = new RenderTextureDescriptor(resolution, resolution);
            desc.graphicsFormat = format;
            desc.dimension = TextureDimension.Tex2DArray;
            desc.volumeDepth = requiredSlices;
            desc.enableRandomWrite = true;
            desc.useMipMap = false;
            desc.msaaSamples = 1;

            tempAlphaMaps = new RenderTexture(desc)
            {
                filterMode = FilterMode.Point,
                name = "Preview_Alphamap_RT"
            };
            if (!tempAlphaMaps.Create())
            {
                Debug.LogError("[GpuPreviewRunner] Failed to create temporary RenderTexture.");
                return null;
            }
#if UNITY_EDITOR
            EditorGpuPreviewCache.Register(terrain, tempAlphaMaps);
#endif

            return tempAlphaMaps;
        }

        private static void SyncAlphaMapsToRT(Texture2D[] alphaMapTextures, RenderTexture rt)
        {
            var arrayCount = alphaMapTextures.Length;
            for (var i = 0; i < arrayCount && i < alphaMapTextures.Length; i++)
            {
                Graphics.CopyTexture(alphaMapTextures[i], 0, 0, rt, i, 0);
            }
        }

        private static float CalculatePathLengthFromSpine(PathJobsUtility.SpineData spineData)
        {
            if (!spineData.IsCreated || spineData.Points.Length < 2) return 0f;
            var length = 0f;
            for (var i = 1; i < spineData.Points.Length; i++)
            {
                length += math.distance(spineData.Points[i], spineData.Points[i - 1]);
            }

            return length;
        }

        private static readonly Dictionary<int, RecipeGpuDataManager> RecipeGpuCache = new();

        static GpuPreviewRunner()
        {
#if UNITY_EDITOR
            // 清理缓存以避免 Domain Reload / 退出编辑器时资源泄漏
            AssemblyReloadEvents.beforeAssemblyReload += DisposeAllCachedGpuData;
            EditorApplication.quitting += DisposeAllCachedGpuData;
#endif
        }

        private static void DisposeAllCachedGpuData()
        {
            foreach (var kvp in RecipeGpuCache)
            {
                kvp.Value?.Dispose();
            }

            RecipeGpuCache.Clear();
        }
        

    }
}