
using System;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Jobs; // For PathJobsUtility
using __temp.MrPathV2._2.Runtime.Jobs.Extensions; // For SafeDispose
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Settings;
using System.Collections.Generic; // For Dictionary
using System.Linq;
using UnityEditor;
using UnityEngine.Experimental.Rendering; // For Linq
#if UNITY_EDITOR
using EditorGpuPreviewCache = __temp.MrPathV2._2.Editor.Terrain.GpuPreviewCache;
#endif

namespace __temp.MrPathV2._2.Editor.Terrain
{
    public class GpuTerrainPainter : ITerrainPainter
    {
        private ComputeShader _paintComputeShader;
        private int _kernelHandle = -1;

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
                _paintComputeShader = Resources.Load<ComputeShader>("Editor/Resources/PaintSplatmapCompute");

                if (_paintComputeShader == null)
                {
                    // Try AssetDatabase approach (Editor only)
#if UNITY_EDITOR
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("PaintSplatmapCompute t:ComputeShader");
                    if (guids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        _paintComputeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                        Debug.Log($"[GpuTerrainPainter] Loaded via AssetDatabase from: {path}");
                    }
#endif
                }
            }

            if (_paintComputeShader != null)
            {
                Debug.Log($"[GpuTerrainPainter] ComputeShader loaded successfully.");

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
            // --- Input Validation ---
            Debug.Log($"[GpuTerrainPainter] Starting validation. KernelHandle: {_kernelHandle}, ComputeShader: {(_paintComputeShader != null ? "Valid" : "Null")}");

            if (_paintComputeShader == null)
            {
                Debug.LogError("[GpuTerrainPainter] ComputeShader is null - cannot proceed.");
                return;
            }

            if (_kernelHandle == -1)
            {
                Debug.LogError("[GpuTerrainPainter] Kernel handle is -1 - PaintTerrain kernel not found.");
                return;
            }

            // Additional kernel validation
            try
            {
                bool hasKernel = _paintComputeShader.HasKernel("PaintTerrain");
                Debug.Log($"[GpuTerrainPainter] HasKernel('PaintTerrain'): {hasKernel}");

                if (!hasKernel)
                {
                    Debug.LogError("[GpuTerrainPainter] ComputeShader does not have 'PaintTerrain' kernel (check for compile errors).");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] Exception checking kernel: {ex.Message}");
                return;
            }
            if (recipeGpuData?.LayerParamsBuffer == null || !recipeGpuData.LayerParamsBuffer.IsValid()) { Debug.LogError("[GpuTerrainPainter] Recipe GPU data LayerParamsBuffer is invalid or null."); return; }
            if (!spineData.IsCreated) { Debug.LogError("[GpuTerrainPainter] Invalid SpineData provided."); return; }
            var td = terrain.terrainData;
            if (td == null) { Debug.LogError("[GpuTerrainPainter] TerrainData is null."); return; }
            var alphaMapTextures = td.alphamapTextures;
            if (alphaMapTextures == null || alphaMapTextures.Length == 0) { Debug.LogError($"[GpuTerrainPainter] Terrain '{terrain.name}' does not have alphamap textures."); return; }

            // --- Graphics Format Check ---
            GraphicsFormat format = alphaMapTextures[0].graphicsFormat;
            int arrayCount = alphaMapTextures.Length;
            // --- FIX for FormatUsage ---
            // Use LoadStore (10) as seen in screenshot, or RandomWrite if available
            if (!SystemInfo.IsFormatSupported(format, FormatUsage.LoadStore))
            {
                // Fallback check for older Unity
                if (!SystemInfo.IsFormatSupported(format, (FormatUsage)10 /*LoadStore*/))
                {
                    Debug.LogError($"[GpuTerrainPainter] Terrain alphamap format '{format}' is not supported for Compute Shader RWTexture (LoadStore Usage).");
                    return;
                }
            }
            // ------------------------

            int resolution = td.alphamapResolution;
            int layers = td.alphamapLayers;
            int numPixelsX = coverageMax.x - coverageMin.x + 1;
            int numPixelsY = coverageMax.y - coverageMin.y + 1;

            if (numPixelsX <= 0 || numPixelsY <= 0) return;

            // Indicates whether a cached RenderTexture was reused (Editor only)
            bool reusedCachedRt = false;
            RenderTexture tempAlphaMaps = null;
            ComputeBuffer spinePointsBuffer = null;
            ComputeBuffer spineTangentsBuffer = null;
            ComputeBuffer spineNormalsBuffer = null;

            try
            {
                token.ThrowIfCancellationRequested();

#if UNITY_EDITOR
                // Try to reuse a cached RenderTexture for real-time GPU preview in the editor
                if (EditorGpuPreviewCache.TryGet(terrain, out var cached) &&
                    cached != null &&
                    cached.width == resolution && cached.height == resolution &&
                    cached.volumeDepth == arrayCount &&
                    cached.graphicsFormat == format)
                {
                    tempAlphaMaps = cached;
                    reusedCachedRt = true;
                }
#endif

                // 1. Create Temporary Writable AlphaMap Texture Array (if no suitable cached RT)
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
                    if (!tempAlphaMaps.Create()) { Debug.LogError("[GpuTerrainPainter] Failed to create temporary RenderTexture for alphamaps."); return; }
#if UNITY_EDITOR
                    // Register the newly created RT to the global cache for future reuse
                    EditorGpuPreviewCache.Register(terrain, tempAlphaMaps);
#endif
                }

                // Always sync RT from current Terrain alphamaps to avoid stale/zero weights
                for (int i = 0; i < arrayCount && i < alphaMapTextures.Length; i++)
                {
                    Graphics.CopyTexture(alphaMapTextures[i], 0, 0, tempAlphaMaps, i, 0);
                }

                token.ThrowIfCancellationRequested();

                // 2. Prepare Compute Shader Inputs
                _paintComputeShader.SetInts(AlphamapResolutionID, resolution, resolution);
                _paintComputeShader.SetInt(AlphamapLayerCountID, layers);
                var tpos = terrain.GetPosition();
                _paintComputeShader.SetVector(TerrainPositionID, new Vector4(tpos.x, tpos.z, 0f, 0f));
                _paintComputeShader.SetVector(TerrainSizeID, new Vector4(td.size.x, td.size.z, 0f, 0f));
                _paintComputeShader.SetInts(AlphamapOffsetID, coverageMin.x, coverageMin.y);
                _paintComputeShader.SetInts(TerrainOffsetID, 0, 0);
                _paintComputeShader.SetFloat(FalloffDistanceID, Mathf.Max(0.001f, profileData.RoadWidth * 0.6f));
                _paintComputeShader.SetFloat(BrushStrengthID, 1.0f);
                _paintComputeShader.SetFloat(BrushSizeID, Mathf.Max(0.001f, profileData.RoadWidth));

                // --- Upload Spine Data ---
                spinePointsBuffer = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
                if (spineData.Points.Length > 0) spinePointsBuffer.SetData(spineData.Points);
                _paintComputeShader.SetBuffer(_kernelHandle, SpinePointsID, spinePointsBuffer);
                _paintComputeShader.SetBuffer(_kernelHandle, SpineDataID, spinePointsBuffer); // Set the same buffer for SpineData

                spineTangentsBuffer = new ComputeBuffer(math.max(1, spineData.Tangents.Length), sizeof(float) * 3);
                if (spineData.Tangents.Length > 0) spineTangentsBuffer.SetData(spineData.Tangents);
                _paintComputeShader.SetBuffer(_kernelHandle, SpineTangentsID, spineTangentsBuffer);

                spineNormalsBuffer = new ComputeBuffer(math.max(1, spineData.Normals.Length), sizeof(float) * 3);
                if (spineData.Normals.Length > 0) spineNormalsBuffer.SetData(spineData.Normals);
                _paintComputeShader.SetBuffer(_kernelHandle, SpineNormalsID, spineNormalsBuffer);

                _paintComputeShader.SetInt(SpinePointCountID, spineData.Points.Length);
                // -------------------------

                _paintComputeShader.SetFloat(RoadWidthID, profileData.RoadWidth);
                float pathLength = CalculatePathLengthFromSpine(spineData);
                _paintComputeShader.SetFloat(PathLengthID, pathLength);
                _paintComputeShader.SetBool(ForceHorizontalID, profileData.ForceHorizontal);
                _paintComputeShader.SetInts(CoverageMinID, coverageMin.x, coverageMin.y);
                _paintComputeShader.SetInts(CoverageMaxID, coverageMax.x, coverageMax.y);

                // --- Recipe Data ---
                _paintComputeShader.SetInt(NumActiveLayersID, recipeGpuData.ActiveLayerCount);
                _paintComputeShader.SetInt(LayerCountID, recipeGpuData.ActiveLayerCount);
                _paintComputeShader.SetBuffer(_kernelHandle, LayerParamsID, recipeGpuData.LayerParamsBuffer);
                if (recipeGpuData.TerrainTextureArray != null)
                {
                    _paintComputeShader.SetTexture(_kernelHandle, TerrainTexturesID, recipeGpuData.TerrainTextureArray);
                }
                // -------------------

                // 3. Set Output Texture
                Debug.Log($"[GpuTerrainPainter] Binding RenderTexture to compute shader. Texture valid: {tempAlphaMaps != null && tempAlphaMaps.IsCreated()}, enableRandomWrite: {tempAlphaMaps?.enableRandomWrite}");

                _paintComputeShader.SetTexture(_kernelHandle, SplatWeightsID, tempAlphaMaps);

                // Verify texture binding by checking if the texture is properly set
                if (tempAlphaMaps == null || !tempAlphaMaps.IsCreated() || !tempAlphaMaps.enableRandomWrite)
                {
                    Debug.LogError($"[GpuTerrainPainter] RenderTexture binding failed! Texture null: {tempAlphaMaps == null}, Created: {tempAlphaMaps?.IsCreated()}, RandomWrite: {tempAlphaMaps?.enableRandomWrite}");
                    return;
                }

                token.ThrowIfCancellationRequested();

                // 4. Dispatch Compute Shader
                Debug.Log($"[GpuTerrainPainter] About to get kernel thread group sizes. KernelHandle: {_kernelHandle}");

                try
                {
                    // Try to query thread group sizes; fall back to known [numthreads(8,8,1)] if it fails
                    uint threadsX = 0, threadsY = 0, threadsZ = 0;
                    try
                    {
                        _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out threadsX, out threadsY, out threadsZ);
                        Debug.Log($"[GpuTerrainPainter] Thread group sizes: {threadsX}x{threadsY}x{threadsZ}");
                    }
                    catch (System.Exception exSizes)
                    {
                        threadsX = 8; threadsY = 8; threadsZ = 1;
                        Debug.LogWarning($"[GpuTerrainPainter] GetKernelThreadGroupSizes failed: {exSizes.Message}. Using fallback 8x8x1.");
                    }

                    if (threadsX == 0 || threadsY == 0)
                    {
                        threadsX = 8; threadsY = 8; threadsZ = 1;
                        Debug.LogWarning("[GpuTerrainPainter] Invalid 0 thread sizes returned. Using fallback 8x8x1.");
                    }

                    int groupsX = Mathf.CeilToInt((float)numPixelsX / threadsX);
                    int groupsY = Mathf.CeilToInt((float)numPixelsY / threadsY);

                    Debug.Log($"[GpuTerrainPainter] Dispatch groups: {groupsX}x{groupsY} for coverage {numPixelsX}x{numPixelsY}");

                    if (groupsX > 0 && groupsY > 0)
                    {
                        Debug.Log($"[GpuTerrainPainter] Dispatching compute shader with {groupsX}x{groupsY} groups...");
                        _paintComputeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
                        Debug.Log("[GpuTerrainPainter] Compute shader dispatched successfully");

                        // Proper GPU synchronization - ensure compute shader completes before readback
                        var cmdBuffer = new UnityEngine.Rendering.CommandBuffer();
                        cmdBuffer.name = "GpuTerrainPainter Sync";

                        Graphics.ExecuteCommandBuffer(cmdBuffer);
                        cmdBuffer.Release();

                        // Additional synchronization
                        GL.Flush();
                        GL.InvalidateState();

                        // Force GPU synchronization - wait for all operations to complete
                        GL.IssuePluginEvent(0);

                        Debug.Log("[GpuTerrainPainter] GPU synchronization completed");
                    }
                    else
                    {
                        Debug.LogWarning("[GpuTerrainPainter] Invalid dispatch groups - skipping");
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GpuTerrainPainter] Exception during compute shader dispatch: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    return;
                }

                token.ThrowIfCancellationRequested();

                // 5. Copy Result back to TerrainData
                //    We use Graphics.CopyTexture for speed. This assumes alphamapTextures[0] is the destination.
                //    This requires the GPU to be finished.
                //    A better way for editor scripting is often to just readback and SetAlphamaps.

                // Ensure GPU operations are complete before readback
                Graphics.ExecuteCommandBuffer(new UnityEngine.Rendering.CommandBuffer());

                // IMPORTANT: Always read back and apply to terrain, regardless of preview mode
                // The GPU preview cache is for visual preview only, actual terrain data should always be updated

                // Validate RenderTexture before readback
                if (tempAlphaMaps == null)
                {
                    Debug.LogError("[GpuTerrainPainter] tempAlphaMaps is null - cannot perform readback");
                    return;
                }

                if (!tempAlphaMaps.IsCreated())
                {
                    Debug.LogError("[GpuTerrainPainter] tempAlphaMaps is not created - cannot perform readback");
                    return;
                }

                Debug.Log($"[GpuTerrainPainter] RenderTexture validation - Created: {tempAlphaMaps.IsCreated()}, " +
                         $"Size: {tempAlphaMaps.width}x{tempAlphaMaps.height}x{tempAlphaMaps.volumeDepth}, " +
                         $"Format: {tempAlphaMaps.graphicsFormat}, Dimension: {tempAlphaMaps.dimension}, " +
                         $"EnableRandomWrite: {tempAlphaMaps.enableRandomWrite}");

                // Check if the texture format is compatible with AsyncGPUReadback
                if (tempAlphaMaps.dimension != TextureDimension.Tex2DArray)
                {
                    Debug.LogError($"[GpuTerrainPainter] Invalid texture dimension: {tempAlphaMaps.dimension}, expected Tex2DArray");
                    return;
                }

                Debug.Log($"[GpuTerrainPainter] About to request readback from texture: {tempAlphaMaps.width}x{tempAlphaMaps.height}x{tempAlphaMaps.volumeDepth}, format: {tempAlphaMaps.graphicsFormat}, dimension: {tempAlphaMaps.dimension}");

                // Try a simpler readback approach first - read the entire texture without specifying regions
                // This should work for Texture2DArray and automatically handle all layers
                AsyncGPUReadbackRequest request;
                try
                {
                    Debug.Log("[GpuTerrainPainter] Attempting AsyncGPUReadback.Request without format conversion...");
                    request = AsyncGPUReadback.Request(tempAlphaMaps, 0);
                    Debug.Log($"[GpuTerrainPainter] AsyncGPUReadback.Request initiated successfully. Request layerCount: {request.layerCount}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GpuTerrainPainter] Exception during AsyncGPUReadback.Request: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    return;
                }
                // Note: The complex parameter version was causing errors, using simpler overload instead
                while (!request.done)
                {
                    if (token.IsCancellationRequested)
                    {
                        Debug.LogWarning("[GpuTerrainPainter] Cancellation requested during GPU readback.");
                        token.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();

                if (request.hasError)
                {
                    Debug.LogError($"[GpuTerrainPainter] AsyncGPUReadback encountered an error! Texture format: {tempAlphaMaps.graphicsFormat}, Dimension: {tempAlphaMaps.dimension}, Size: {tempAlphaMaps.width}x{tempAlphaMaps.height}x{tempAlphaMaps.volumeDepth}");
                    return;
                }
                else if (!request.done)
                {
                    Debug.LogError("[GpuTerrainPainter] AsyncGPUReadback is not done but no error reported!");
                    return;
                }
                else
                {
                    await Task.Yield(); // Ensure main thread
                    token.ThrowIfCancellationRequested();

                    if (td.alphamapTextureCount > 0)
                    {
                        try
                        {
                            // Convert RGBA8 per-slice data to terrain layer format
                            int width = resolution;
                            int height = resolution;
                            int layerCount = layers;
                            int sliceCount = request.layerCount;
                            Debug.Log($"[GpuTerrainPainter] Readback ok. Converting {sliceCount} slices ({width}x{height}) to {layerCount} layers...");

                            float[,,] layerData = new float[height, width, layerCount];
                            for (int slice = 0; slice < sliceCount; slice++)
                            {
                                var sliceData = request.GetData<Color32>(slice);
                                if (!sliceData.IsCreated || sliceData.Length != width * height)
                                {
                                    Debug.LogError($"[GpuTerrainPainter] Slice {slice} data invalid. Length: {(sliceData.IsCreated ? sliceData.Length : 0)}, Expected: {width * height}");
                                    continue;
                                }

                                int baseLayer = slice * 4;
                                for (int i = 0; i < sliceData.Length; i++)
                                {
                                    int x = i % width;
                                    int y = i / width;
                                    var c = sliceData[i];
                                    float r = c.r / 255f;
                                    float g = c.g / 255f;
                                    float b = c.b / 255f;
                                    float a = c.a / 255f;
                                    if (baseLayer < layerCount) layerData[y, x, baseLayer] = r;
                                    if (baseLayer + 1 < layerCount) layerData[y, x, baseLayer + 1] = g;
                                    if (baseLayer + 2 < layerCount) layerData[y, x, baseLayer + 2] = b;
                                    if (baseLayer + 3 < layerCount) layerData[y, x, baseLayer + 3] = a;
                                }
                            }

                            // 读回保护：对每个像素的所有图层做归一化，并在和为零时回退到第0层
                            // 这可以避免因为拷贝失败或计算得到全零而导致整片地形呈现为黑色
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    float sum = 0f;
                                    for (int l = 0; l < layerCount; l++) sum += layerData[y, x, l];
                                    if (sum <= 1e-5f)
                                    {
                                        if (layerCount > 0)
                                        {
                                            // 回退：将第 0 层置为 1
                                            for (int l = 0; l < layerCount; l++) layerData[y, x, l] = 0f;
                                            layerData[y, x, 0] = 1f;
                                        }
                                    }
                                    else
                                    {
                                        float inv = 1f / sum;
                                        for (int l = 0; l < layerCount; l++) layerData[y, x, l] *= inv;
                                    }
                                }
                            }

                            td.SetAlphamaps(0, 0, layerData);
                            terrain.Flush();
                            EditorUtility.SetDirty(td);
                        }
                        catch (System.InvalidOperationException ex)
                        {
                            Debug.LogError($"[GpuTerrainPainter] Failed to get readback data: {ex.Message}. Request done: {request.done}, hasError: {request.hasError}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[GpuTerrainPainter] Operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] Error during execution: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                // 6. Cleanup GPU resources
                spinePointsBuffer?.Release();
                spineTangentsBuffer?.Release();
                spineNormalsBuffer?.Release();
#if UNITY_EDITOR
                // In the editor the RenderTexture is managed by the global cache for potential reuse
#else
                tempAlphaMaps?.Release();
#endif
            }
        }

        private float CalculatePathLengthFromSpine(PathJobsUtility.SpineData spineData)
        {
            if (!spineData.IsCreated || spineData.Points.Length < 2) return 0f;
            float length = 0f;
            for (int i = 1; i < spineData.Points.Length; i++)
            {
                length += math.distance(spineData.Points[i], spineData.Points[i - 1]);
            }
            return length;
        }

        private float[,,] ConvertRGBAToLayerFormat(NativeArray<float> rgbaData, int width, int height, int layers)
        {
            float[,,] layerData = new float[height, width, layers];

            // For Texture2DArray, the readback data is organized as:
            // [layer0_pixels][layer1_pixels][layer2_pixels]...
            // Each pixel in a layer has multiple channels depending on the format

            int pixelsPerLayer = width * height;
            int channelsPerPixel = rgbaData.Length / (pixelsPerLayer * layers);

            Debug.Log($"[GpuTerrainPainter] Readback analysis: Total data length: {rgbaData.Length}, Pixels per layer: {pixelsPerLayer}, Layers: {layers}, Calculated channels per pixel: {channelsPerPixel}");

            if (!rgbaData.IsCreated)
            {
                Debug.LogError("RGBA readback data is not created!");
                return layerData;
            }

            // Handle different data layouts based on actual data size
            if (rgbaData.Length == pixelsPerLayer * layers)
            {
                // Single channel per pixel, layer-separated format
                Debug.Log("[GpuTerrainPainter] Using single-channel layer-separated format");
                for (int layer = 0; layer < layers; layer++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int dataIndex = layer * pixelsPerLayer + y * width + x;
                            layerData[y, x, layer] = rgbaData[dataIndex];
                        }
                    }
                }
            }
            else if (rgbaData.Length == pixelsPerLayer * layers * 4)
            {
                // RGBA format with 4 channels per pixel for all layers (Texture2DArray)
                Debug.Log("[GpuTerrainPainter] Using RGBA Texture2DArray format");
                for (int layer = 0; layer < layers; layer++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            // For Texture2DArray RGBA format: layer-major order with 4 channels per pixel
                            int pixelIndex = layer * pixelsPerLayer * 4 + (y * width + x) * 4;
                            // Use the alpha channel (index 3) for terrain layer data
                            layerData[y, x, layer] = rgbaData[pixelIndex + 3];
                        }
                    }
                }
            }
            else if (rgbaData.Length == pixelsPerLayer * 4)
            {
                // RGBA format with 4 channels per pixel (single layer)
                Debug.Log("[GpuTerrainPainter] Using RGBA interleaved format");
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelIndex = y * width + x;
                        int rgbaIndex = pixelIndex * 4;

                        // Extract layer data from RGBA channels
                        for (int layer = 0; layer < Mathf.Min(layers, 4); layer++)
                        {
                            layerData[y, x, layer] = rgbaData[rgbaIndex + layer];
                        }

                        // Fill remaining layers with 0
                        for (int layer = 4; layer < layers; layer++)
                        {
                            layerData[y, x, layer] = 0f;
                        }
                    }
                }
            }
            else
            {
                Debug.LogError($"Unsupported readback data format! Data length: {rgbaData.Length}, Expected single-channel: {pixelsPerLayer * layers}, Expected RGBA single layer: {pixelsPerLayer * 4}, Expected RGBA all layers: {pixelsPerLayer * layers * 4}");
                return layerData;
            }

            return layerData;
        }

        private float[,,] ConvertReadbackTo3D(NativeArray<float> flatData, int width, int height, int depth)
        {
            float[,,] data3D = new float[height, width, depth];
            if (!flatData.IsCreated || flatData.Length != width * height * depth)
            {
                Debug.LogError($"Readback data length mismatch! Expected {width * height * depth}, got {(flatData.IsCreated ? flatData.Length : 0)}");
                return data3D;
            }
            int index = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int z = 0; z < depth; z++)
                        data3D[y, x, z] = flatData[index++];
            return data3D;
        }

        public void Dispose() { }
    }
}