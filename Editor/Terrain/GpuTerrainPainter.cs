
using System;
using System.Threading;
using System.Threading.Tasks;
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

namespace MrPathV2.Editor.Terrain
{
    public class GpuTerrainPainter : ITerrainPainter
    {

        // --- Cache Shader Property IDs ---
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int AlphamapLayerCountID = Shader.PropertyToID("_AlphamapLayerCount");
        private static readonly int TerrainPositionID = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeID = Shader.PropertyToID("_TerrainSize");
        private static readonly int SpinePointsID = Shader.PropertyToID("_SpinePoints");
        private static readonly int SpineTangentsID = Shader.PropertyToID("_SpineTangents");
        private static readonly int SpineNormalsID = Shader.PropertyToID("_SpineNormals");
        private static readonly int SpinePointCountID = Shader.PropertyToID("_SpinePointCount");
        private static readonly int RoadWidthID = Shader.PropertyToID("_RoadWidth");
        private static readonly int PathLengthID = Shader.PropertyToID("_PathLength");
        private static readonly int ForceHorizontalID = Shader.PropertyToID("_ForceHorizontal");
        private static readonly int CoverageMinID = Shader.PropertyToID("_CoverageMin");
        private static readonly int CoverageMaxID = Shader.PropertyToID("_CoverageMax");
        private static readonly int LayerParamsID = Shader.PropertyToID("LayerParamsBuffer");
        private static readonly int SpineDataID = Shader.PropertyToID("SpineData");
        private static readonly int TerrainTexturesID = Shader.PropertyToID("_TerrainTextures");
        private static readonly int NumActiveLayersID = Shader.PropertyToID("_NumActiveLayers");
        private static readonly int LayerCountID = Shader.PropertyToID("_LayerCount");
        private static readonly int AlphamapOffsetID = Shader.PropertyToID("_AlphamapOffset");
        private static readonly int TerrainOffsetID = Shader.PropertyToID("_TerrainOffset");
        private static readonly int FalloffDistanceID = Shader.PropertyToID("_FalloffDistance");
        private static readonly int BrushStrengthID = Shader.PropertyToID("_BrushStrength");
        private static readonly int BrushSizeID = Shader.PropertyToID("_BrushSize");
        private static readonly int SplatWeightsID = Shader.PropertyToID("_SplatWeights");
        private readonly int _kernelHandle = -1;
        private readonly ComputeShader _paintComputeShader;
        // --------------------------------

        public GpuTerrainPainter()
        {
            // Try multiple loading approaches
            _paintComputeShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");

            // Alternative loading if first attempt fails
            if (_paintComputeShader == null)
            {
                Debug.LogWarning("[GpuTerrainPainter] First attempt failed, trying alternative paths...");

                // Try with full path
                _paintComputeShader = Resources.Load<ComputeShader>($"Editor/Resources/PaintSplatmapCompute");

                if (_paintComputeShader == null)
                {
                    // Try AssetDatabase approach (Editor only)
#if UNITY_EDITOR
                    var guids = AssetDatabase.FindAssets("PaintSplatmapCompute t:ComputeShader");
                    if (guids.Length > 0)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        _paintComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                        Debug.Log($"[GpuTerrainPainter] Loaded via AssetDatabase from: {path}");
                    }
#endif
                }
            }

            if (_paintComputeShader != null)
            {
                Debug.Log("[GpuTerrainPainter] ComputeShader loaded successfully.");

                _kernelHandle = _paintComputeShader.FindKernel("PaintTerrain");
                Debug.Log($"[GpuTerrainPainter] PaintTerrain kernel handle: {_kernelHandle}");

                if (_kernelHandle == -1)
                {
                    Debug.LogError("[GpuTerrainPainter] 'PaintTerrain' kernel not found in ComputeShader!");
                }
            }
            else
            {
                Debug.LogError("[GpuTerrainPainter] PaintSplatmapCompute.compute shader not found in any location!");
            }
        }

        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData, // Not used
            RecipeGpuDataManager recipeGpuData, // Used
            NativeArray<float2> roadContour,
            float4 contourBounds,
            int2 coverageMin,
            int2 coverageMax,
            CancellationToken token)
        {
            // 0) 验证与准备元数据
            if (!ValidateAndPrepare(terrain, spineData, recipeGpuData, coverageMin, coverageMax,
                    out var td, out var alphaMapTextures, out var format, out var resolution,
                    out var layers, out var numPixelsX, out var numPixelsY))
            {
                return;
            }

            // 1) 获取并同步工作 RT
            var tempAlphaMaps = AcquireAlphaMapRT(terrain, alphaMapTextures, resolution, format);
            if (tempAlphaMaps == null || !tempAlphaMaps.IsCreated())
            {
                Debug.LogError("[GpuTerrainPainter] Failed to acquire working RenderTexture.");
                return;
            }
            SyncAlphaMapsToRT(alphaMapTextures, tempAlphaMaps);

            // 2) 构建与绑定输入
            ComputeBuffer pointsBuffer = null, tangentsBuffer = null, normalsBuffer = null;
            try
            {
                token.ThrowIfCancellationRequested();

                (pointsBuffer, tangentsBuffer, normalsBuffer) = SetupSpineBuffers(spineData);
                BindShaderInputs(terrain, td, profileData, spineData, recipeGpuData,
                    pointsBuffer, tangentsBuffer, normalsBuffer,
                    coverageMin, coverageMax, resolution, layers, tempAlphaMaps);

                token.ThrowIfCancellationRequested();

                // 3) 调度计算
                var (gx, gy) = CalculateDispatchGroups(numPixelsX, numPixelsY);
                if (gx <= 0 || gy <= 0)
                {
                    Debug.LogWarning("[GpuTerrainPainter] Invalid dispatch groups.");
                    return;
                }
                DispatchPaint(gx, gy);

                token.ThrowIfCancellationRequested();

                // 4) 读回并应用到 Terrain
                await ReadbackAndApplyAsync(tempAlphaMaps, td, terrain, layers, resolution, token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[GpuTerrainPainter] Operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] Error during execution: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                pointsBuffer?.Release();
                tangentsBuffer?.Release();
                normalsBuffer?.Release();
#if !UNITY_EDITOR
                tempAlphaMaps?.Release();
#endif
            }
        }

        // 验证与准备
        private bool ValidateAndPrepare(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            RecipeGpuDataManager recipeGpuData,
            int2 coverageMin,
            int2 coverageMax,
            out TerrainData td,
            out Texture2D[] alphaMapTextures,
            out GraphicsFormat format,
            out int resolution,
            out int layers,
            out int numPixelsX,
            out int numPixelsY)
        {
            td = null;
            alphaMapTextures = null;
            format = GraphicsFormat.None;
            resolution = 0;
            layers = 0;
            numPixelsX = 0;
            numPixelsY = 0;

            if (_paintComputeShader == null)
            {
                Debug.LogError("[GpuTerrainPainter] ComputeShader is null.");
                return false;
            }
            if (_kernelHandle == -1 || !_paintComputeShader.HasKernel("PaintTerrain"))
            {
                Debug.LogError("[GpuTerrainPainter] 'PaintTerrain' kernel not found.");
                return false;
            }
            if (!spineData.IsCreated)
            {
                Debug.LogError("[GpuTerrainPainter] Invalid SpineData.");
                return false;
            }
            td = terrain.terrainData;
            if (td == null)
            {
                Debug.LogError("[GpuTerrainPainter] TerrainData is null.");
                return false;
            }
            alphaMapTextures = td.alphamapTextures;
            if (alphaMapTextures == null || alphaMapTextures.Length == 0)
            {
                Debug.LogError($"[GpuTerrainPainter] Terrain '{terrain.name}' has no alphamap textures.");
                return false;
            }
            resolution = td.alphamapResolution;
            layers = td.alphamapLayers;
            numPixelsX = coverageMax.x - coverageMin.x + 1;
            numPixelsY = coverageMax.y - coverageMin.y + 1;
            if (numPixelsX <= 0 || numPixelsY <= 0)
            {
                return false;
            }
            format = alphaMapTextures[0].graphicsFormat;
            if (!IsGraphicsFormatRWCompatible(format))
            {
                Debug.LogError($"[GpuTerrainPainter] GraphicsFormat '{format}' is not RWTexture compatible.");
                return false;
            }
            if (recipeGpuData?.LayerParamsBuffer != null && recipeGpuData.LayerParamsBuffer.IsValid()) return true;
            Debug.LogError("[GpuTerrainPainter] Invalid RecipeGpuData.");
            return false;
        }

        private static bool IsGraphicsFormatRWCompatible(GraphicsFormat fmt)
        {
            return SystemInfo.IsFormatSupported(fmt, FormatUsage.LoadStore) || SystemInfo.IsFormatSupported(fmt, (FormatUsage)10);
        }

        // 获取并同步工作 RT
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
                tempAlphaMaps = new RenderTexture(resolution, resolution, 0, format)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = arrayCount,
                    enableRandomWrite = true,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    name = "Temp_Alphamap_RT"
                };
                if (!tempAlphaMaps.Create())
                {
                    Debug.LogError("[GpuTerrainPainter] Failed to create temporary RenderTexture.");
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

        private (ComputeBuffer points, ComputeBuffer tangents, ComputeBuffer normals) SetupSpineBuffers(PathJobsUtility.SpineData spineData)
        {
            var points = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
            if (spineData.Points.Length > 0) points.SetData(spineData.Points);

            var tangents = new ComputeBuffer(math.max(1, spineData.Tangents.Length), sizeof(float) * 3);
            if (spineData.Tangents.Length > 0) tangents.SetData(spineData.Tangents);

            var normals = new ComputeBuffer(math.max(1, spineData.Normals.Length), sizeof(float) * 3);
            if (spineData.Normals.Length > 0) normals.SetData(spineData.Normals);

            return (points, tangents, normals);
        }

        private void BindShaderInputs(
            UnityEngine.Terrain terrain,
            TerrainData td,
            PathJobsUtility.ProfileData profileData,
            PathJobsUtility.SpineData spineData,
            RecipeGpuDataManager recipeGpuData,
            ComputeBuffer pointsBuffer,
            ComputeBuffer tangentsBuffer,
            ComputeBuffer normalsBuffer,
            int2 coverageMin,
            int2 coverageMax,
            int resolution,
            int layers,
            RenderTexture outputRT)
        {
            BindCommonParams(_paintComputeShader, terrain, td, profileData, coverageMin, coverageMax, resolution, layers,spineData);

            _paintComputeShader.SetBuffer(_kernelHandle, SpinePointsID, pointsBuffer);
            _paintComputeShader.SetBuffer(_kernelHandle, SpineTangentsID, tangentsBuffer);
            _paintComputeShader.SetBuffer(_kernelHandle, SpineNormalsID, normalsBuffer);
            _paintComputeShader.SetBuffer(_kernelHandle, SpineDataID, pointsBuffer); // 兼容旧变量名
            _paintComputeShader.SetInt(SpinePointCountID, spineData.Points.Length);

            _paintComputeShader.SetInt(NumActiveLayersID, recipeGpuData.ActiveLayerCount);
            _paintComputeShader.SetInt(LayerCountID, recipeGpuData.ActiveLayerCount);
            _paintComputeShader.SetBuffer(_kernelHandle, LayerParamsID, recipeGpuData.LayerParamsBuffer);
            if (recipeGpuData.TerrainTextureArray != null)
            {
                _paintComputeShader.SetTexture(_kernelHandle, TerrainTexturesID, recipeGpuData.TerrainTextureArray);
            }

            _paintComputeShader.SetTexture(_kernelHandle, SplatWeightsID, outputRT);
        }

        private static void BindCommonParams(ComputeShader cs, UnityEngine.Terrain t, TerrainData data,
            PathJobsUtility.ProfileData prof, int2 covMin, int2 covMax, int res, int layerCount,PathJobsUtility.SpineData spineData)
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

        private (int gx, int gy) CalculateDispatchGroups(int pixelsX, int pixelsY)
        {
            uint tx = 8, ty = 8, tz = 1;
            try
            {
                _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out tx, out ty, out tz);
            }
            catch
            {
                tx = 8; ty = 8; tz = 1;
            }
            tx = tx == 0 ? 8u : tx;
            ty = ty == 0 ? 8u : ty;
            var gx = Mathf.CeilToInt(pixelsX / (float)tx);
            var gy = Mathf.CeilToInt(pixelsY / (float)ty);
            return (gx, gy);
        }

        private void DispatchPaint(int groupsX, int groupsY)
        {
            _paintComputeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
        }

        private async Task ReadbackAndApplyAsync(RenderTexture rt, TerrainData data, UnityEngine.Terrain terrain, int layerCount, int res, CancellationToken ct)
        {
            AsyncGPUReadbackRequest request;
            try
            {
                request = AsyncGPUReadback.Request(rt);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] AsyncGPUReadback.Request failed: {ex.Message}");
                return;
            }
            while (!request.done)
            {
                if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            if (request.hasError)
            {
                Debug.LogError("[GpuTerrainPainter] AsyncGPUReadback encountered an error.");
                return;
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            if (data.alphamapTextureCount <= 0) return;

            try
            {
                var width = res;
                var height = res;
                var sliceCount = request.layerCount;
                var layerData = new float[height, width, layerCount];

                for (var slice = 0; slice < sliceCount; slice++)
                {
                    var sliceData = request.GetData<Color32>(slice);
                    if (!sliceData.IsCreated || sliceData.Length != width * height) continue;
                    var baseLayer = slice * 4;
                    for (var i = 0; i < sliceData.Length; i++)
                    {
                        var x = i % width;
                        var y = i / width;
                        var c = sliceData[i];
                        var r = c.r / 255f; var g = c.g / 255f; var b = c.b / 255f; var a = c.a / 255f;
                        if (baseLayer < layerCount) layerData[y, x, baseLayer] = r;
                        if (baseLayer + 1 < layerCount) layerData[y, x, baseLayer + 1] = g;
                        if (baseLayer + 2 < layerCount) layerData[y, x, baseLayer + 2] = b;
                        if (baseLayer + 3 < layerCount) layerData[y, x, baseLayer + 3] = a;
                    }
                }

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var sum = 0f;
                        for (var l = 0; l < layerCount; l++) sum += layerData[y, x, l];
                        if (sum <= 1e-5f)
                        {
                            if (layerCount > 0)
                            {
                                for (var l = 0; l < layerCount; l++) layerData[y, x, l] = 0f;
                                layerData[y, x, 0] = 1f;
                            }
                        }
                        else
                        {
                            var inv = 1f / sum;
                            for (var l = 0; l < layerCount; l++) layerData[y, x, l] *= inv;
                        }
                    }
                }

                data.SetAlphamaps(0, 0, layerData);
#if UNITY_EDITOR
                terrain.Flush();
                EditorUtility.SetDirty(data);
#else
                terrain.Flush();
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] Failed to apply readback: {ex.Message}");
            }
        }

        public void Dispose() { }

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

    }
}
