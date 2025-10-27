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
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int AlphamapLayerCountID = Shader.PropertyToID("_AlphamapLayerCount");
        private static readonly int TerrainPositionID = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeID = Shader.PropertyToID("_TerrainSize");
        private static readonly int SpinePointsID = Shader.PropertyToID("_SpinePoints");
        private static readonly int SpineTangentsID = Shader.PropertyToID("_SpineTangents");
        private static readonly int SpineNormalsID = Shader.PropertyToID("_SpineNormals");
        private static readonly int SpineDataID = Shader.PropertyToID("SpineData");
        private static readonly int SpinePointCountID = Shader.PropertyToID("_SpinePointCount");
        private static readonly int RoadWidthID = Shader.PropertyToID("_RoadWidth");
        private static readonly int PathLengthID = Shader.PropertyToID("_PathLength");
        private static readonly int ForceHorizontalID = Shader.PropertyToID("_ForceHorizontal");
        private static readonly int CoverageMinID = Shader.PropertyToID("_CoverageMin");
        private static readonly int CoverageMaxID = Shader.PropertyToID("_CoverageMax");
        private static readonly int LayerParamsID = Shader.PropertyToID("LayerParamsBuffer");
        private static readonly int TerrainTexturesID = Shader.PropertyToID("_TerrainTextures");
        private static readonly int NumActiveLayersID = Shader.PropertyToID("_NumActiveLayers");
        private static readonly int LayerCountID = Shader.PropertyToID("_LayerCount");
        private static readonly int AlphamapOffsetID = Shader.PropertyToID("_AlphamapOffset");
        private static readonly int TerrainOffsetID = Shader.PropertyToID("_TerrainOffset");
        private static readonly int FalloffDistanceID = Shader.PropertyToID("_FalloffDistance");
        private static readonly int BrushStrengthID = Shader.PropertyToID("_BrushStrength");
        private static readonly int BrushSizeID = Shader.PropertyToID("_BrushSize");
        private static readonly int SplatWeightsID = Shader.PropertyToID("_SplatWeights");

        /// <summary>
        /// 运行一次 GPU 预览调度。仅在 Editor 下有效。
        /// </summary>
        public static bool TryRun(UnityEngine.Terrain terrain, PathSpine spine, PathProfile profile, CancellationToken token = default)
        {
#if !UNITY_EDITOR
            return false;
#endif
            try
            {
                if (terrain == null || terrain.terrainData == null || profile == null || spine.VertexCount < 2)
                    return false;

                var td = terrain.terrainData;
                var alphaMapTextures = td.alphamapTextures;
                if (alphaMapTextures == null || alphaMapTextures.Length == 0)
                    return false;

                var resolution = td.alphamapResolution;
                var layers = td.alphamapLayers;

                var format = alphaMapTextures[0].graphicsFormat;
                // 如果原始格式不可作为 RWTexture 使用，则回退为 UNorm，保证计算着色器可写
                if (!IsGraphicsFormatRWCompatible(format))
                {
                    format = GraphicsFormat.R8G8B8A8_UNorm;
                }

                // Load compute shader and kernel
                var compute = LoadPaintCompute();
                if (compute == null) return false;
                var kernel = compute.FindKernel("PaintTerrain");
                if (kernel == -1) return false;

                // Build spine/profile data
                using var spineData = new PathJobsUtility.SpineData(spine, Allocator.TempJob);
                using var profileData = new PathJobsUtility.ProfileData(profile, Allocator.TempJob);
                if (!spineData.IsCreated || !profileData.IsCreated)
                    return false;

                // Generate road contour & bounds (用于覆盖区域计算)
                RoadContourGenerator.GenerateContour(spine, profile, out var contour, out var contourBounds, Allocator.TempJob);
                contour.Dispose();

                // Determine coverage (复用 PaintTerrainCommand 的逻辑)
                var (useLimit, coverageMin, coverageMax) = CalculateCoverageArea(terrain, contourBounds);
                if (!useLimit) return false;
                var numPixelsX = coverageMax.x - coverageMin.x + 1;
                var numPixelsY = coverageMax.y - coverageMin.y + 1;
                if (numPixelsX <= 0 || numPixelsY <= 0)
                    return false;

                // Ensure GPU recipe data
                var recipeId = profile.roadRecipe ? profile.roadRecipe.GetInstanceID() : 0;
                if (!_recipeGpuCache.TryGetValue(recipeId, out var gpuData) || gpuData == null)
                {
                    gpuData = new RecipeGpuDataManager();
                    _recipeGpuCache[recipeId] = gpuData;
                }
                var layerMap = LayerResolver.Resolve(terrain, profile.roadRecipe, interactive: false);
                gpuData.UpdateData(profile.roadRecipe, layerMap);
                if (gpuData.LayerParamsBuffer == null || !gpuData.LayerParamsBuffer.IsValid() || gpuData.ActiveLayerCount <= 0)
                {
                    return false;
                }

                // Acquire working RT (cached by terrain)
                var tempAlphaMaps = AcquireAlphaMapRT(terrain, alphaMapTextures, resolution, format);
                if (tempAlphaMaps == null || !tempAlphaMaps.IsCreated())
                {
                    gpuData.Dispose();
                    return false;
                }
                SyncAlphaMapsToRT(alphaMapTextures, tempAlphaMaps);

                // Build compute buffers
                ComputeBuffer pointsBuffer = null, tangentsBuffer = null, normalsBuffer = null;
                try
                {
                    token.ThrowIfCancellationRequested();

                    (pointsBuffer, tangentsBuffer, normalsBuffer) = SetupSpineBuffers(spineData);

                    // Bind common + inputs
                    BindCommonParams(compute, terrain, td, profileData, coverageMin, coverageMax, resolution, layers, spineData);

                    compute.SetBuffer(kernel, SpinePointsID, pointsBuffer);
                    compute.SetBuffer(kernel, SpineTangentsID, tangentsBuffer);
                    compute.SetBuffer(kernel, SpineNormalsID, normalsBuffer);
                    compute.SetBuffer(kernel, SpineDataID, pointsBuffer); // 兼容 Compute 中使用的旧变量名
                    compute.SetInt(SpinePointCountID, spineData.Points.Length);

                    compute.SetInt(NumActiveLayersID, gpuData.ActiveLayerCount);
                    compute.SetInt(LayerCountID, gpuData.ActiveLayerCount);
                    compute.SetBuffer(kernel, LayerParamsID, gpuData.LayerParamsBuffer);
                    if (gpuData.TerrainTextureArray != null)
                    {
                        compute.SetTexture(kernel, TerrainTexturesID, gpuData.TerrainTextureArray);
                    }

                    compute.SetTexture(kernel, SplatWeightsID, tempAlphaMaps);

                    // Dispatch
                    var (gx, gy) = CalculateDispatchGroups(compute, kernel, numPixelsX, numPixelsY);
                    if (gx <= 0 || gy <= 0)
                    {
                        gpuData.Dispose();
                        return false;
                    }
                    compute.Dispatch(kernel, gx, gy, 1);

                    // 预览模式：不读回，不写 Terrain；仅将 RT 留在缓存，供材质绑定。

                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                finally
                {
                    pointsBuffer?.Release();
                    tangentsBuffer?.Release();
                    normalsBuffer?.Release();
                    // gpuData is cached; do not dispose here.
#if !UNITY_EDITOR
                    tempAlphaMaps?.Release();
#endif
                }
            }
            catch
            {
                return false;
            }
        }

        private static ComputeShader LoadPaintCompute()
        {
            var cs = Resources.Load<ComputeShader>("PaintSplatmapCompute");
            if (cs != null) return cs;
#if UNITY_EDITOR
            cs = Resources.Load<ComputeShader>("Editor/Resources/PaintSplatmapCompute");
            if (cs != null) return cs;
            var guids = AssetDatabase.FindAssets("PaintSplatmapCompute t:ComputeShader");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (cs != null) return cs;
            }
#endif
            return null;
        }

        private static (ComputeBuffer points, ComputeBuffer tangents, ComputeBuffer normals) SetupSpineBuffers(PathJobsUtility.SpineData spineData)
        {
            var points = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
            if (spineData.Points.Length > 0) points.SetData(spineData.Points);

            var tangents = new ComputeBuffer(math.max(1, spineData.Tangents.Length), sizeof(float) * 3);
            if (spineData.Tangents.Length > 0) tangents.SetData(spineData.Tangents);

            var normals = new ComputeBuffer(math.max(1, spineData.Normals.Length), sizeof(float) * 3);
            if (spineData.Normals.Length > 0) normals.SetData(spineData.Normals);

            return (points, tangents, normals);
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
            PathJobsUtility.ProfileData prof, int2 covMin, int2 covMax, int res, int layerCount, PathJobsUtility.SpineData spineData)
        {
            cs.SetInts(AlphamapResolutionID, res, res);
            cs.SetInt(AlphamapLayerCountID, layerCount);
            var tpos = t.GetPosition();
            cs.SetVector(TerrainPositionID, new Vector4(tpos.x, tpos.z, 0f, 0f));
            cs.SetVector(TerrainSizeID, new Vector4(data.size.x, data.size.z, 0f, 0f));
            cs.SetInts(AlphamapOffsetID, covMin.x, covMin.y);
            cs.SetInts(TerrainOffsetID, 0, 0);
            cs.SetFloat(FalloffDistanceID, Mathf.Max(0.001f, prof.RoadWidth * 0.6f));
            cs.SetFloat(BrushStrengthID, 1.0f);
            cs.SetFloat(BrushSizeID, Mathf.Max(0.001f, prof.RoadWidth));
            cs.SetFloat(RoadWidthID, prof.RoadWidth);
            cs.SetFloat(PathLengthID, CalculatePathLengthFromSpine(spineData));
            cs.SetBool(ForceHorizontalID, prof.ForceHorizontal);
            cs.SetInts(CoverageMinID, covMin.x, covMin.y);
            cs.SetInts(CoverageMaxID, covMax.x, covMax.y);
        }

        private static (bool useLimit, int2 pixelMin, int2 pixelMax) CalculateCoverageArea(UnityEngine.Terrain terrain, float4 bounds)
        {
            var terrainBounds = new Bounds(terrain.GetPosition() + terrain.terrainData.size / 2f, terrain.terrainData.size);
            var roadBounds = new Bounds(new Vector3(bounds.x + bounds.z * 0.5f, 0f, bounds.y + bounds.w * 0.5f), new Vector3(bounds.z, terrainBounds.size.y, bounds.w));
            var intersectCenter = terrainBounds.ClosestPoint(roadBounds.center);
            var intersectMin = Vector3.Min(terrainBounds.min, roadBounds.min);
            var intersectMax = Vector3.Max(terrainBounds.max, roadBounds.max);
            var terrainMinX = terrainBounds.min.x;
            var terrainMinZ = terrainBounds.min.z;
            var terrainSizeX = terrainBounds.size.x;
            var terrainSizeZ = terrainBounds.size.z;
            var resolution = terrain.terrainData.alphamapResolution;
            var invSizeX = 1.0f / Mathf.Max(1e-5f, terrainSizeX);
            var invSizeZ = 1.0f / Mathf.Max(1e-5f, terrainSizeZ);

            var intersectMinX = Mathf.Max(intersectMin.x, terrainBounds.min.x);
            var intersectMinZ = Mathf.Max(intersectMin.z, terrainBounds.min.z);
            var intersectMaxX = Mathf.Min(intersectMax.x, terrainBounds.max.x);
            var intersectMaxZ = Mathf.Min(intersectMax.z, terrainBounds.max.z);

            var pixelMinX = Mathf.FloorToInt((intersectMinX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMinZ = Mathf.FloorToInt((intersectMinZ - terrainMinZ) * invSizeZ * (resolution - 1));
            var pixelMaxX = Mathf.CeilToInt((intersectMaxX - terrainMinX) * invSizeX * (resolution - 1));
            var pixelMaxZ = Mathf.CeilToInt((intersectMaxZ - terrainMinZ) * invSizeZ * (resolution - 1));

            pixelMinX = Mathf.Clamp(pixelMinX, 0, resolution - 1);
            pixelMinZ = Mathf.Clamp(pixelMinZ, 0, resolution - 1);
            pixelMaxX = Mathf.Clamp(pixelMaxX, 0, resolution - 1);
            pixelMaxZ = Mathf.Clamp(pixelMaxZ, 0, resolution - 1);

            return (true, new int2(pixelMinX, pixelMinZ), new int2(pixelMaxX, pixelMaxZ));
        }

        private static RenderTexture AcquireAlphaMapRT(UnityEngine.Terrain terrain, Texture2D[] alphaMapTextures, int resolution, GraphicsFormat format)
        {
            var arrayCount = alphaMapTextures.Length;
            RenderTexture tempAlphaMaps = null;
#if UNITY_EDITOR
            if (EditorGpuPreviewCache.TryGet(terrain, out var cached) &&
                cached != null &&
                cached.width == resolution && cached.height == resolution &&
                cached.volumeDepth == arrayCount &&
                cached.graphicsFormat == format)
            {
                tempAlphaMaps = cached;
            }
#endif
            if (tempAlphaMaps == null)
            {
                var desc = new RenderTextureDescriptor(resolution, resolution);
                desc.graphicsFormat = format;
                desc.dimension = TextureDimension.Tex2DArray;
                desc.volumeDepth = arrayCount;
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
            }
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

        private static (int gx, int gy) CalculateDispatchGroups(ComputeShader cs, int kernel, int numPixelsX, int numPixelsY)
        {
            cs.GetKernelThreadGroupSizes(kernel, out var tx, out var ty, out var tz);
            var gx = Mathf.CeilToInt(numPixelsX / (float)tx);
            var gy = Mathf.CeilToInt(numPixelsY / (float)ty);
            return (gx, gy);
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
        private static readonly Dictionary<int, RecipeGpuDataManager> _recipeGpuCache = new Dictionary<int, RecipeGpuDataManager>();

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
            foreach (var kvp in _recipeGpuCache)
            {
                kvp.Value?.Dispose();
            }
            _recipeGpuCache.Clear();
        }
    }
}


