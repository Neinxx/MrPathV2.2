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
using MrPathV2.Runtime.Mask;
#if UNITY_EDITOR
using EditorGpuPreviewCache = MrPathV2.Editor.Terrain.GpuPreviewCache;
using Object = UnityEngine.Object;
#endif
using MrPathV2.Editor.Settings;

namespace MrPathV2.Editor.Terrain
{
    public class GpuTerrainPainter : ITerrainPainter
    {

        // --- Cache Shader Property IDs ---
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("alphamap_resolution");
        private static readonly int AlphamapLayerCountID = Shader.PropertyToID("alphamap_layer_count");
        private static readonly int TerrainPositionID = Shader.PropertyToID("terrain_position");
        private static readonly int TerrainSizeID = Shader.PropertyToID("terrain_size");
        private static readonly int RoadContourID = Shader.PropertyToID("road_contour");
        private static readonly int ContourPointCountID = Shader.PropertyToID("contour_point_count");
        private static readonly int SpinePointCountID = Shader.PropertyToID("spine_point_count");
        private static readonly int RoadWidthID = Shader.PropertyToID("road_width");
        private static readonly int PathLengthID = Shader.PropertyToID("_PathLength");
        private static readonly int CoverageMinID = Shader.PropertyToID("coverage_min");
        private static readonly int CoverageMaxID = Shader.PropertyToID("coverage_max");
        private static readonly int LayerParamsID = Shader.PropertyToID("layer_params_buffer");
        private static readonly int SpineDataID = Shader.PropertyToID("spine_data");
        private static readonly int LayerCountID = Shader.PropertyToID("layer_count");
        private static readonly int AlphamapOffsetID = Shader.PropertyToID("alphamap_offset");
        private static readonly int TerrainOffsetID = Shader.PropertyToID("terrain_offset");
        private static readonly int FalloffDistanceID = Shader.PropertyToID("falloff_distance");
        private static readonly int SplatWeightsID = Shader.PropertyToID("splat_weights");
        private static readonly int RoadMaskTexID = Shader.PropertyToID("road_mask");
        private static readonly int MaskThresholdID = Shader.PropertyToID("mask_threshold");
        private static readonly int EdgeWidthWorldID = Shader.PropertyToID("edge_width_world");
        private static readonly int ContourBoundsID = Shader.PropertyToID("contour_bounds");
        private static readonly int RoadSdfTexID = Shader.PropertyToID("road_sdf");
        private static readonly int DebugModeID = Shader.PropertyToID("debug_mode");
        private static readonly int CoverageMax = Shader.PropertyToID("_CoverageMax");
        private static readonly int TerrainPosition = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSize = Shader.PropertyToID("_TerrainSize");
        private static readonly int AlphamapResolution = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int CoverageMin = Shader.PropertyToID("_CoverageMin");
        private readonly int _kernelHandle = -1;
        private readonly ComputeShader _paintComputeShader;
        // Runtime resources for mask
        private Material _roadMaskMaterial;
        private RenderTexture _roadMaskCache;
         // --------------------------------

        public GpuTerrainPainter()
        {
            // Load paint compute shader
            _paintComputeShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");
#if UNITY_EDITOR
            if (_paintComputeShader == null)
            {
                var guids = AssetDatabase.FindAssets("PaintSplatmapCompute t:ComputeShader");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _paintComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    Debug.Log($"[GpuTerrainPainter] Loaded compute from: {path}");
                }
            }
#endif
            if (_paintComputeShader == null)
            {
                Debug.LogError("[GpuTerrainPainter] PaintSplatmapCompute.compute shader not found.");
                return;
            }

            _kernelHandle = _paintComputeShader.FindKernel("paint_terrain");
            if (_kernelHandle == -1)
            {
                Debug.LogError("[GpuTerrainPainter] 'paint_terrain' kernel not found in ComputeShader.");
            }
            else
            {
                Debug.Log($"[GpuTerrainPainter] paint_terrain kernel handle: {_kernelHandle}");
            }


            // Create road mask material
            var maskShader = Shader.Find("MrPathV2/RoadMaskRender");
#if UNITY_EDITOR
            if (maskShader == null)
            {
                maskShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/__temp/MrPathV2/Shaders/RoadMaskRender.shader");
            }
#endif
            if (maskShader != null)
            {
                _roadMaskMaterial = new Material(maskShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        public Task ExecuteAsync(UnityEngine.Terrain terrain, PathJobsUtility.SpineData spineData, PathJobsUtility.ProfileData profileData, RecipeData recipeData, RecipeGpuDataManager recipeGpuData, NativeArray<float2> roadContour, float4 contourBounds,
            Vector2Int coverageMin, Vector2Int coverageMax,
            CancellationToken token)
        {
            // 0) 验证与准备元数据
            if (!ValidateAndPrepare(terrain, spineData, recipeGpuData, coverageMin, coverageMax,
                out var td, out var alphaMapTextures, out var format, out var resolution,
                out var layers, out var numPixelsX, out var numPixelsY))
            {
                return Task.CompletedTask;
            }

            // 1) 获取并同步工作 RT
            var tempAlphaMaps = AcquireAlphaMapRT(terrain, alphaMapTextures, resolution, format);
            if (tempAlphaMaps == null || !tempAlphaMaps.IsCreated())
            {
                Debug.LogError("[GpuTerrainPainter] Failed to acquire working RenderTexture.");
                return Task.CompletedTask;
            }
            // 预拷贝已在串行命令缓冲中执行，避免重复拷贝开销

            // 2) 构建与绑定输入
            ComputeBuffer pointsBuffer = null;
            ComputeBuffer contourBuffer = null; // 新增：轮廓数据
            try
            {
                token.ThrowIfCancellationRequested();

                pointsBuffer = SetupSpinePointsBuffer(spineData);
                // 新增：构建 RoadContour Buffer
                if (roadContour.IsCreated && roadContour.Length >= 3)
                {
                    contourBuffer = SetupContourBuffer(roadContour);
                }

                // --- 计算对齐后的偏移和调度组 ---
                uint tx = 8, ty = 8, tz = 1;
                try
                {
                    _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out tx, out ty, out tz);
                }
                catch
                {
                    tx = 8;
                    ty = 8;
                    tz = 1;
                }
                tx = tx == 0 ? 8u : tx;
                ty = ty == 0 ? 8u : ty;

                // 计算偏移对齐（向下取整到线程组大小的倍数）
                var remX = coverageMin.x % (int)tx;
                var remY = coverageMin.y % (int)ty;
                if (remX < 0) remX += (int)tx;
                if (remY < 0) remY += (int)ty;
                var alignedOffset = new Vector2Int(coverageMin.x - remX, coverageMin.y - remY);

                // 计算需要覆盖的像素总数（包含前置填充）
                var totalPixelsX = numPixelsX + remX;
                var totalPixelsY = numPixelsY + remY;

                // 计算调度组数量
                var gx = Mathf.CeilToInt(totalPixelsX / (float)tx);
                var gy = Mathf.CeilToInt(totalPixelsY / (float)ty);
                Debug.Log($"[GpuTerrainPainter] Dispatch groups aligned: {gx} x {gy} (threads {tx}x{ty}), Offset {alignedOffset}");

                // 使用串行 CommandBuffer：预拷贝 → Dispatch → Fence → 区域拷贝
                CommitSerialGpuPipeline(
                    terrain, td, profileData, spineData, recipeGpuData,
                    pointsBuffer,
                    contourBuffer, contourBounds, roadContour.IsCreated ? roadContour.Length : 0,
                    tempAlphaMaps,
                    coverageMin, coverageMax,
                    alignedOffset,
                    resolution, layers,
                    gx, gy
                );
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
                contourBuffer?.Release();

#if UNITY_EDITOR
                if (!(EditorGpuPreviewCache.TryGet(terrain, out var cachedRt) && cachedRt == tempAlphaMaps))
                {
                    tempAlphaMaps?.Release();
                }
#else
                tempAlphaMaps?.Release();
#endif
            }

            return Task.CompletedTask;
        }

        // 验证与准备
        private bool ValidateAndPrepare(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            RecipeGpuDataManager recipeGpuData,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
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
            if (_kernelHandle == -1 || !_paintComputeShader.HasKernel("paint_terrain"))
            {
                Debug.LogError("[GpuTerrainPainter] 'paint_terrain' kernel not found.");
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
                Debug.LogError("[GpuTerrainPainter] Alphamap textures not found.");
                return false;
            }
            var formatOk = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.Render);
            if (!formatOk)
            {
                Debug.LogError("[GpuTerrainPainter] Graphics format R8G8B8A8_UNorm not supported for RenderTexture.");
                return false;
            }
            format = GraphicsFormat.R8G8B8A8_UNorm;
            resolution = td.alphamapResolution;
            layers = td.alphamapLayers;

            numPixelsX = Mathf.Clamp(coverageMax.x - coverageMin.x + 1, 0, resolution);
            numPixelsY = Mathf.Clamp(coverageMax.y - coverageMin.y + 1, 0, resolution);
            if (numPixelsX <= 0 || numPixelsY <= 0)
            {
                Debug.LogWarning("[GpuTerrainPainter] Empty coverage area.");
                return false;
            }

            if (recipeGpuData == null || !recipeGpuData.IsReady())
            {
                Debug.LogWarning("[GpuTerrainPainter] Invalid RecipeGpuData.");
                return false;
            }

            return true;
        }

        private RenderTexture AcquireAlphaMapRT(UnityEngine.Terrain terrain, Texture2D[] alphaMapTextures, int resolution, GraphicsFormat format)
        {
            var td = terrain.terrainData;
            var requiredSlices = Mathf.CeilToInt(td.alphamapLayers / 4f);
#if UNITY_EDITOR
            // 尝试复用缓存中的工作 RT（Editor 环境）
            if (EditorGpuPreviewCache.TryGet(terrain, out var cached) && cached && cached.IsCreated())
            {
                var ok = cached.width == resolution && cached.height == resolution
                                                    && cached.volumeDepth == requiredSlices
                                                    && cached.graphicsFormat == format
                                                    && cached.dimension == TextureDimension.Tex2DArray
                                                    && cached.enableRandomWrite;
                if (ok) return cached;
                cached.Release();
                Object.DestroyImmediate(cached);
            }
#endif
            var desc = new RenderTextureDescriptor(resolution, resolution)
            {
                graphicsFormat = format,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = requiredSlices,
                enableRandomWrite = true,
                useMipMap = false,
                msaaSamples = 1
            };
            var tempAlphaMaps = new RenderTexture(desc)
            {
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
            return tempAlphaMaps;
        }

        // SyncAlphaMapsToRT removed: this class does not pre-copy; serial CommandBuffer handles copy sequence.

        private ComputeBuffer SetupSpinePointsBuffer(PathJobsUtility.SpineData spineData)
        {
            var points = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
            if (spineData.Points.Length > 0) points.SetData(spineData.Points);
            return points;
        }

        // 新增：构建 RoadContour Buffer（float2 XZ 世界坐标）
        private ComputeBuffer SetupContourBuffer(NativeArray<float2> contour)
        {
            try
            {
                if (!contour.IsCreated || contour.Length < 3) return null;
                var arr = new Vector2[contour.Length];
                for (int i = 0; i < contour.Length; i++)
                {
                    var c = contour[i];
                    arr[i] = new Vector2(c.x, c.y);
                }
                var cb = new ComputeBuffer(arr.Length, sizeof(float) * 2, ComputeBufferType.Structured);
                cb.SetData(arr);
                return cb;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GpuTerrainPainter] SetupContourBuffer failed: {e.Message}");
                return null;
            }
        }

        // BindShaderInputs removed: direct compute sets are replaced by command buffer binding,
        // and per-layer/mask parameters are unified in LayerParamsBuffer.

        private static void BindCommonParams(ComputeShader cs, UnityEngine.Terrain t, TerrainData data,
            PathJobsUtility.ProfileData prof, Vector2Int covMin, Vector2Int covMax, int res, int layerCount, PathJobsUtility.SpineData spineData, float4 contourBounds)
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

        // Helpers: Region, safe-region computation, material ensuring, and command buffer release
        private struct Region
        {
            public int startX;
            public int startY;
            public int width;
            public int height;
            public Region(int sx, int sy, int w, int h)
            {
                startX = sx;
                startY = sy;
                width = w;
                height = h;
            }
        }

        private static bool TryComputeSafeRegion(Vector2Int alignedOffset, int gx, int gy, int resolution, int margin, int threadX, int threadY, out Region aligned, out Region safe)
        {
            aligned = default;
            safe = default;
            if (gx <= 0 || gy <= 0 || resolution <= 0 || threadX <= 0 || threadY <= 0) return false;

            var dispatchPixelsX = gx * threadX;
            var dispatchPixelsY = gy * threadY;

            var alignedStartX = Mathf.Clamp(alignedOffset.x, 0, resolution - 1);
            var alignedStartY = Mathf.Clamp(alignedOffset.y, 0, resolution - 1);
            var alignedEndX = Mathf.Clamp(alignedOffset.x + dispatchPixelsX - 1, 0, resolution - 1);
            var alignedEndY = Mathf.Clamp(alignedOffset.y + dispatchPixelsY - 1, 0, resolution - 1);
            var alignedSubW = alignedEndX - alignedStartX + 1;
            var alignedSubH = alignedEndY - alignedStartY + 1;
            if (alignedSubW <= 0 || alignedSubH <= 0) return false;

            aligned = new Region(alignedStartX, alignedStartY, alignedSubW, alignedSubH);

            var safeStartX = Mathf.Clamp(alignedStartX - Mathf.Max(0, margin), 0, resolution - 1);
            var safeStartY = Mathf.Clamp(alignedStartY - Mathf.Max(0, margin), 0, resolution - 1);
            var safeEndX = Mathf.Clamp(alignedEndX + Mathf.Max(0, margin), 0, resolution - 1);
            var safeEndY = Mathf.Clamp(alignedEndY + Mathf.Max(0, margin), 0, resolution - 1);
            var safeW = safeEndX - safeStartX + 1;
            var safeH = safeEndY - safeStartY + 1;
            if (safeW <= 0 || safeH <= 0) return false;

            safe = new Region(safeStartX, safeStartY, safeW, safeH);
            return true;
        }

        private void EnsureRoadMaskMaterial()
        {
            if (_roadMaskMaterial != null) return;
            var maskShader = Shader.Find("MrPathV2/RoadMaskRender");
 #if UNITY_EDITOR
             if (maskShader == null)
                 maskShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/__temp/MrPathV2/Shaders/RoadMaskRender.shader");
 #endif
             if (maskShader == null) return;
             _roadMaskMaterial = new Material(maskShader)
             {
                 hideFlags = HideFlags.HideAndDontSave
             };
         }

        private RenderTexture GetOrCreateRoadMaskRT(int resolution)
        {
            try
            {
                if (_roadMaskCache && _roadMaskCache.IsCreated() &&
                    _roadMaskCache.width == resolution && _roadMaskCache.height == resolution)
                {
                    return _roadMaskCache;
                }
                if (_roadMaskCache)
                {
                    try { _roadMaskCache.Release(); } catch { }
                    _roadMaskCache = null;
                }
                _roadMaskCache = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R8_UNorm)
                {
                    name = "RoadMaskRT",
                    enableRandomWrite = false
                };
                _roadMaskCache.Create();
                return _roadMaskCache;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GpuTerrainPainter] GetOrCreateRoadMaskRT failed: {e.Message}");
                return null;
            }
        }

        private static void ReleaseSafely(CommandBuffer cmd)
        {
            if (cmd == null) return;
            try
            {
                cmd.Release();
            }
            catch { }
        }


        private void CommitSerialGpuPipeline(
            UnityEngine.Terrain terrain,
            TerrainData data,
            PathJobsUtility.ProfileData profileData,
            PathJobsUtility.SpineData spineData,
            RecipeGpuDataManager recipeGpuData,
            ComputeBuffer pointsBuffer,
            ComputeBuffer contourBuffer,
            float4 contourBounds,
            int contourPointCount,
            RenderTexture rt,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            Vector2Int alignedOffset,
            int resolution,
            int layerCount,
            int gx,
            int gy)
        {
            if (gx <= 0 || gy <= 0)
            {
                Debug.LogWarning("[GpuTerrainPainter] Invalid dispatch groups.");
                return;
            }

            if (data == null || terrain == null || rt == null)
            {
                Debug.LogWarning("[GpuTerrainPainter] Serial pipeline early-exit: invalid inputs.");
                return;
            }

            var copySupport = SystemInfo.copyTextureSupport;
            if ((copySupport & CopyTextureSupport.DifferentTypes) == 0)
            {
                Debug.LogWarning("[GpuTerrainPainter] CopyTexture different types not supported; GPU commit may be limited on this platform.");
            }

            var dstTextures = data.alphamapTextures;
            if (dstTextures == null || dstTextures.Length == 0) return;

            // 确保材质准备好
            EnsureRoadMaskMaterial();

            RenderTexture roadMaskRT = GetOrCreateRoadMaskRT(resolution);

            // 计算线程组大小并得到安全区域
            uint tx = 8, ty = 8, tz = 1;
            try
            {
                _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out tx, out ty, out tz);
            }
            catch
            {
                tx = 8;
                ty = 8;
                tz = 1;
            }
            tx = tx == 0 ? 8u : tx;
            ty = ty == 0 ? 8u : ty;

#if UNITY_EDITOR
            var adv = MrPathProjectSettings.GetOrCreateSettings()?.advancedSettings;
#else
            var adv = (MrPathAdvancedSettings)null;
#endif
            var margin = Mathf.Max(0, adv != null ? adv.basemapSafetyMarginPixels : 0);

            if (!TryComputeSafeRegion(alignedOffset, gx, gy, resolution, margin, (int)tx, (int)ty, out var aligned, out var safe))
                return;

            // 计算需要拷贝的切片数量（每个切片对应一个控制纹理，RGBA×4层）
            var sliceCount = Mathf.CeilToInt(layerCount / 4f);
            sliceCount = Mathf.Min(sliceCount, rt != null ? rt.volumeDepth : 0);
            sliceCount = Mathf.Min(sliceCount, dstTextures.Length);
            if (sliceCount <= 0) return;

            try
            {
                roadMaskRT = GetOrCreateRoadMaskRT(resolution);
                if (roadMaskRT == null)
                {
                    Debug.LogWarning("[GpuTerrainPainter] RoadMaskRT unavailable.");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GpuTerrainPainter] Failed to get mask RT: {e.Message}");
                return;
            }

            Mesh maskMesh = null;
            var paintCmd = new CommandBuffer
            {
                name = "[GpuTerrainPainter] Paint_Alphamaps_Compute"
            };
            for (var i = 0; i < sliceCount; i++)
            {
                paintCmd.CopyTexture(dstTextures[i], 0, 0, rt, i, 0);
            }

            if (_roadMaskMaterial != null)
            {
                var tposRm = terrain.GetPosition();
                _roadMaskMaterial.SetVector(TerrainPosition, new Vector4(tposRm.x, tposRm.z, 0, 0));
                _roadMaskMaterial.SetVector(TerrainSize, new Vector4(data.size.x, data.size.z, 0, 0));
                _roadMaskMaterial.SetVector(AlphamapResolution, new Vector4(resolution, resolution));
                _roadMaskMaterial.SetVector(CoverageMin, new Vector4(coverageMin.x, coverageMin.y));
                _roadMaskMaterial.SetVector(CoverageMax, new Vector4(coverageMax.x, coverageMax.y));

                paintCmd.SetRenderTarget(roadMaskRT);
                paintCmd.ClearRenderTarget(true, true, Color.black);

                maskMesh = RoadMaskMeshBuilder.Build(spineData, profileData);
                if (maskMesh != null)
                    paintCmd.DrawMesh(maskMesh, Matrix4x4.identity, _roadMaskMaterial);
            }

            BindCommonParams(_paintComputeShader, terrain, data, profileData, coverageMin, coverageMax, resolution, layerCount, spineData, contourBounds);
            _paintComputeShader.SetInts(AlphamapOffsetID, aligned.startX, aligned.startY);
            _paintComputeShader.SetInt(LayerCountID, recipeGpuData != null ? recipeGpuData.ActiveLayerCount : 0);
            _paintComputeShader.SetInt(SpinePointCountID, spineData.Points.Length);
            _paintComputeShader.SetInt(ContourPointCountID, contourPointCount);

            var maskThreshold = (adv != null) ? Mathf.Clamp01(adv.gpuMaskThreshold) : 0.5f;
            _paintComputeShader.SetFloat(MaskThresholdID, maskThreshold);

            var edgeWidth = (adv != null && adv.gpuOverrideEdgeWidth)
                ? Mathf.Max(0.001f, adv.gpuEdgeWidthWorld)
                : Mathf.Max(0.001f, profileData.FalloffWidth);
            _paintComputeShader.SetFloat(EdgeWidthWorldID, edgeWidth);

            var debugMode = (adv != null) ? Mathf.Clamp(adv.gpuDebugMode, 0, 2) : 0;
            _paintComputeShader.SetInt(DebugModeID, debugMode);

            if (recipeGpuData != null && recipeGpuData.LayerParamsBuffer != null && recipeGpuData.LayerParamsBuffer.IsValid())
                paintCmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, LayerParamsID, recipeGpuData.LayerParamsBuffer);
            if (pointsBuffer != null)
                paintCmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, SpineDataID, pointsBuffer);
            if (contourBuffer != null)
                paintCmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, RoadContourID, contourBuffer);

            paintCmd.SetComputeTextureParam(_paintComputeShader, _kernelHandle, SplatWeightsID, rt);
            if (roadMaskRT != null)
                paintCmd.SetComputeTextureParam(_paintComputeShader, _kernelHandle, RoadMaskTexID, roadMaskRT);

            paintCmd.DispatchCompute(_paintComputeShader, _kernelHandle, gx, gy, 1);

#if UNITY_2019_3_OR_NEWER
            var fence = paintCmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
            Graphics.ExecuteCommandBuffer(paintCmd);
            ReleaseSafely(paintCmd);
            var cmd = new CommandBuffer
            {
                name = "[GpuTerrainPainter] Commit_Alphamaps_GPU"
            };
            cmd.WaitOnAsyncGraphicsFence(fence);
#else
            var fence = paintCmd.CreateGPUFence();
            Graphics.ExecuteCommandBuffer(paintCmd);
            ReleaseSafely(paintCmd);
            var cmd = new CommandBuffer { name = "[GpuTerrainPainter] Commit_Alphamaps_GPU" };
            cmd.WaitOnGPUFence(fence);
#endif

            for (var i = 0; i < sliceCount; i++)
            {
                cmd.CopyTexture(rt, i, 0, safe.startX, safe.startY, safe.width, safe.height, dstTextures[i], 0, 0, safe.startX, safe.startY);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            ReleaseSafely(cmd);
            if (maskMesh)
            {
                try
                {
                    Object.DestroyImmediate(maskMesh);
                }
                catch { }
                maskMesh = null;
            }
#if UNITY_EDITOR
            terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, 100000f);
            terrain.Flush();
            EditorUtility.SetDirty(data);
#else
            terrain.Flush();
#endif

            // RoadMaskRT is cached; do not release here.

            try
            {
                RebuildBasemapFromRTRegion(data, rt, layerCount, safe.startX, safe.startY, safe.width, safe.height);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GpuTerrainPainter] Basemap rebuild from RT failed: {e.Message}");
            }
        }

        public void Dispose()
        {
            if (!_roadMaskMaterial) return;
#if UNITY_EDITOR
            Object.DestroyImmediate(_roadMaskMaterial);
#else
                Object.Destroy(_roadMaskMaterial);
#endif
            _roadMaskMaterial = null;

            if (_roadMaskCache)
            {
                try { _roadMaskCache.Release(); } catch { }
                _roadMaskCache = null;
            }
        }


        // 基于区域的 Basemap 重建：从控制纹理按区域GPU读回并调用 SetAlphamaps

        // 新增：基于 RenderTexture 切片的区域 Basemap 重建（用于实时 GPU 预览）
        public static void RebuildBasemapFromRTRegion(TerrainData data, RenderTexture rt, int layers, int startX, int startY, int width, int height)
        {
            if (!data || !rt) return;
            if (width <= 0 || height <= 0) return;
            var sliceCount = Mathf.Min(Mathf.CeilToInt(layers / 4f), rt.volumeDepth);
            var weights = new float[height, width, layers];
            var texW = rt.width;
            var texH = rt.height;

            for (var slice = 0; slice < sliceCount; slice++)
            {
                var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.ARGB32);
                req.WaitForCompletion();
                if (req.hasError)
                {
                    Debug.LogWarning($"[GpuTerrainPainter] AsyncGPUReadback error on RT slice {slice}.");
                    continue;
                }
                var colors = req.GetData<Color32>(slice);
                var baseLayer = slice * 4;

                for (var y = 0; y < height; y++)
                {
                    var pyFull = startY + y;
                    if (pyFull < 0 || pyFull >= texH) continue;
                    var rowOffset = pyFull * texW;
                    for (int x = 0; x < width; x++)
                    {
                        var pxFull = startX + x;
                        if (pxFull < 0 || pxFull >= texW) continue;
                        var c = colors[rowOffset + pxFull];
                        if (baseLayer + 0 < layers) weights[y, x, baseLayer + 0] = c.r / 255f;
                        if (baseLayer + 1 < layers) weights[y, x, baseLayer + 1] = c.g / 255f;
                        if (baseLayer + 2 < layers) weights[y, x, baseLayer + 2] = c.b / 255f;
                        if (baseLayer + 3 < layers) weights[y, x, baseLayer + 3] = c.a / 255f;
                    }
                }
            }

            data.SetAlphamaps(startX, startY, weights);
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

    }
}
