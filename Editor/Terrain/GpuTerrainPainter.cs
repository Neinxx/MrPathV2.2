
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
using Object = UnityEngine.Object;
#endif
using MrPathV2.Editor.Settings;

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
        private static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
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
            // 预拷贝已在串行命令缓冲中执行，避免重复拷贝开销

            // 2) 构建与绑定输入
            ComputeBuffer pointsBuffer = null, tangentsBuffer = null, normalsBuffer = null;
            try
            {
                token.ThrowIfCancellationRequested();

                (pointsBuffer, tangentsBuffer, normalsBuffer) = SetupSpineBuffers(spineData);

                // --- 计算对齐后的偏移和调度组 ---
                uint tx = 8, ty = 8, tz = 1;
                try
                {
                    _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out tx, out ty, out tz);
                }
                catch { tx = 8; ty = 8; tz = 1; }
                tx = tx == 0 ? 8u : tx;
                ty = ty == 0 ? 8u : ty;

                // 计算偏移对齐（向下取整到线程组大小的倍数）
                var remX = coverageMin.x % (int)tx;
                var remY = coverageMin.y % (int)ty;
                if (remX < 0) remX += (int)tx;
                if (remY < 0) remY += (int)ty;
                var alignedOffset = new int2(coverageMin.x - remX, coverageMin.y - remY);

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
                    pointsBuffer, tangentsBuffer, normalsBuffer,
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
                tangentsBuffer?.Release();
                normalsBuffer?.Release();
#if UNITY_EDITOR
                if (!(EditorGpuPreviewCache.TryGet(terrain, out var cachedRt) && cachedRt == tempAlphaMaps))
                {
                    tempAlphaMaps?.Release();
                }
#else
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
                Debug.LogWarning($"[GpuTerrainPainter] GraphicsFormat '{format}' not RW-compatible, falling back to R8G8B8A8_UNorm.");
                format = GraphicsFormat.R8G8B8A8_UNorm;
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
            BindCommonParams(_paintComputeShader, terrain, td, profileData, coverageMin, coverageMax, resolution, layers, spineData);

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
            cs.SetInt(DebugModeID, 0); // Disable debug mode for production
            cs.SetInt(SpinePointCountID, spineData.Points.Length);

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
            Debug.Log("[GpuTerrainPainter] Dispatch groups: " + gx + " x " + gy + " (threads " + tx + "x" + ty + ")");
            return (gx, gy);
        }

        private void DispatchPaint(int groupsX, int groupsY)
        {
            _paintComputeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
        }

        private async Task ReadbackAndApplyAsync(RenderTexture rt, TerrainData data, UnityEngine.Terrain terrain, int layerCount, int res, int2 coverageMin, int2 coverageMax, CancellationToken ct)
        {
            // 改为纯 GPU 提交：使用 GraphicsFence + CommandBuffer.CopyTexture 按覆盖区拷贝到地形控制纹理
            try
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield(); // 让前面的 Dispatch 在队列中安置好

                if (data == null || terrain == null) return;
                var dstTextures = data.alphamapTextures;
                if (dstTextures == null || dstTextures.Length == 0) return;

                var width = res;
                var height = res;

                // 覆盖区像素边界钳制
                var startX = Mathf.Clamp(coverageMin.x, 0, width - 1);
                var startY = Mathf.Clamp(coverageMin.y, 0, height - 1);
                var endX = Mathf.Clamp(coverageMax.x, 0, width - 1);
                var endY = Mathf.Clamp(coverageMax.y, 0, height - 1);
                var subW = endX - startX + 1;
                var subH = endY - startY + 1;
                if (subW <= 0 || subH <= 0) return;

                // 计算需要拷贝的切片数量（每个切片对应一个控制纹理，RGBA×4层）
                var sliceCount = Mathf.CeilToInt(layerCount / 4f);
                sliceCount = Mathf.Min(sliceCount, rt != null ? rt.volumeDepth : 0);
                sliceCount = Mathf.Min(sliceCount, dstTextures.Length);
                if (sliceCount <= 0) return;

                // 在全局队列插入一个 Fence，确保后续拷贝发生在 Compute 完成之后
                var fence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);

                var cmd = new CommandBuffer { name = "[GpuTerrainPainter] Commit_Alphamaps_GPU" };
                // 等待上面的 Fence（Compute 阶段）
                cmd.WaitOnAsyncGraphicsFence(fence);

                // 按覆盖区域进行分片拷贝
                for (var i = 0; i < sliceCount; i++)
                {
                    // 从 2DArray 的第 i 个切片拷贝到第 i 个控制纹理的指定区域
                    cmd.CopyTexture(rt, i, 0, startX, startY, subW, subH, dstTextures[i], 0, 0, startX, startY);
                }

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

#if UNITY_EDITOR
            // 预览：提高 basemapDistance，避免远距离回退到旧的基底贴图
            terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, 100000f);
            terrain.Flush();
            EditorUtility.SetDirty(data);
#else
            terrain.Flush();
#endif
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[GpuTerrainPainter] GPU commit cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GpuTerrainPainter] GPU commit failed: {ex.Message}");
            }
        }

        private void CommitSerialGpuPipeline(
            UnityEngine.Terrain terrain,
            TerrainData data,
            PathJobsUtility.ProfileData profileData,
            PathJobsUtility.SpineData spineData,
            RecipeGpuDataManager recipeGpuData,
            ComputeBuffer pointsBuffer,
            ComputeBuffer tangentsBuffer,
            ComputeBuffer normalsBuffer,
            RenderTexture rt,
            int2 coverageMin,
            int2 coverageMax,
            int2 alignedOffset,
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

            var width = resolution;
            var height = resolution;


            // 覆盖区像素边界钳制（用于 CoverageMin/Max 的安全窗口）
            var startX = Mathf.Clamp(coverageMin.x, 0, width - 1);
            var startY = Mathf.Clamp(coverageMin.y, 0, height - 1);
            var endX = Mathf.Clamp(coverageMax.x, 0, width - 1);
            var endY = Mathf.Clamp(coverageMax.y, 0, height - 1);
            var subW = endX - startX + 1;
            var subH = endY - startY + 1;
            if (subW <= 0 || subH <= 0) return;

            // 计算线程组大小，得到调度的对齐矩形（Compute 实际触达像素范围）
            uint tx = 8, ty = 8, tz = 1;
            try { _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out tx, out ty, out tz); } catch { tx = 8; ty = 8; tz = 1; }
            tx = tx == 0 ? 8u : tx;
            ty = ty == 0 ? 8u : ty;
            var dispatchPixelsX = gx * (int)tx;
            var dispatchPixelsY = gy * (int)ty;

            // 对齐后的预拷贝/回写矩形（包含线程组对齐带来的填充像素）
            var alignedStartX = Mathf.Clamp(alignedOffset.x, 0, width - 1);
            var alignedStartY = Mathf.Clamp(alignedOffset.y, 0, height - 1);
            var alignedEndX = Mathf.Clamp(alignedOffset.x + dispatchPixelsX - 1, 0, width - 1);
            var alignedEndY = Mathf.Clamp(alignedOffset.y + dispatchPixelsY - 1, 0, height - 1);
            var alignedSubW = alignedEndX - alignedStartX + 1;
            var alignedSubH = alignedEndY - alignedStartY + 1;
            if (alignedSubW <= 0 || alignedSubH <= 0) return;

            // 安全边距（来自高级设置），用于预拷贝/回写和 Basemap 重建
#if UNITY_EDITOR
            var adv = MrPathProjectSettings.GetOrCreateSettings()?.advancedSettings;
#else
            var adv = (MrPathAdvancedSettings)null;
#endif
            var margin = Mathf.Max(0, adv != null ? adv.basemapSafetyMarginPixels : 0);
            var safeStartX = Mathf.Clamp(alignedStartX - margin, 0, width - 1);
            var safeStartY = Mathf.Clamp(alignedStartY - margin, 0, height - 1);
            var safeEndX = Mathf.Clamp(alignedEndX + margin, 0, width - 1);
            var safeEndY = Mathf.Clamp(alignedEndY + margin, 0, height - 1);
            var safeW = safeEndX - safeStartX + 1;
            var safeH = safeEndY - safeStartY + 1;
            if (safeW <= 0 || safeH <= 0) return;

            // 绑定常规模型参数（对齐矩形作为覆盖窗口）
            BindCommonParams(_paintComputeShader, terrain, data, profileData, new int2(alignedStartX, alignedStartY), new int2(alignedEndX, alignedEndY), resolution, layerCount, spineData);

            var cmd = new CommandBuffer { name = "[GpuTerrainPainter] Serial_GPU_Pipeline" };

            // 在命令缓冲中绑定 Compute 所需的 Buffer/Texture（CB 上下文专用）
            if (pointsBuffer != null) cmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, SpinePointsID, pointsBuffer);
            if (tangentsBuffer != null) cmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, SpineTangentsID, tangentsBuffer);
            if (normalsBuffer != null) cmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, SpineNormalsID, normalsBuffer);
            if (pointsBuffer != null) cmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, SpineDataID, pointsBuffer); // 兼容旧变量名
            cmd.SetComputeIntParam(_paintComputeShader, SpinePointCountID, spineData.Points.Length);

            cmd.SetComputeIntParam(_paintComputeShader, NumActiveLayersID, recipeGpuData.ActiveLayerCount);
            cmd.SetComputeIntParam(_paintComputeShader, LayerCountID, recipeGpuData.ActiveLayerCount);
            if (recipeGpuData.LayerParamsBuffer != null)
                cmd.SetComputeBufferParam(_paintComputeShader, _kernelHandle, LayerParamsID, recipeGpuData.LayerParamsBuffer);
            if (recipeGpuData.TerrainTextureArray != null)
            {
                cmd.SetComputeTextureParam(_paintComputeShader, _kernelHandle, TerrainTexturesID, recipeGpuData.TerrainTextureArray);
            }
            cmd.SetComputeTextureParam(_paintComputeShader, _kernelHandle, SplatWeightsID, rt);

            // 计算需要拷贝/回写的切片数量
            var sliceCount = Mathf.Min(dstTextures.Length, rt.volumeDepth);

            // 1) 区域预拷贝：Terrain 控制纹理 → 工作 RT 切片（对齐矩形）
            for (var i = 0; i < sliceCount; i++)
            {
                cmd.CopyTexture(dstTextures[i], 0, 0, safeStartX, safeStartY, safeW, safeH, rt, i, 0, safeStartX, safeStartY);
            }

            // 2) 调度 Compute（在同一命令缓冲中保证与拷贝严格顺序）
            cmd.DispatchCompute(_paintComputeShader, _kernelHandle, gx, gy, 1);

            // 3) Fence：显式标记 Compute 阶段，用于老驱动/平台提供更强的时序保障
            var fence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
            cmd.WaitOnAsyncGraphicsFence(fence);


            // 4) 区域回写：工作 RT 切片 → Terrain 控制纹理（对齐矩形）
            for (var i = 0; i < sliceCount; i++)
            {
                cmd.CopyTexture(rt, i, 0, safeStartX, safeStartY, safeW, safeH, dstTextures[i], 0, 0, safeStartX, safeStartY);
            }
 
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            // 在 GPU 回写之后，重建 Basemap（按对齐区域）
            try
            {
                RebuildBasemapFromTexturesRegion(data, dstTextures, layerCount, alignedStartX, alignedStartY, alignedSubW, alignedSubH);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GpuTerrainPainter] Basemap rebuild failed: {e.Message}");
            }

#if UNITY_EDITOR
            // 预览：提高 basemapDistance，避免远距离回退到旧的基底贴图
            terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, 100000f);
            terrain.Flush();
            EditorUtility.SetDirty(data);
#else
            terrain.Flush();
#endif
        }

        public void Dispose() { }
 
        // 基于区域的 Basemap 重建：从控制纹理按区域GPU读回并调用 SetAlphamaps
        private static void RebuildBasemapFromTexturesRegion(TerrainData data, Texture2D[] dstTextures, int layers, int startX, int startY, int width, int height)
        {
            if (data == null || dstTextures == null || dstTextures.Length == 0) return;
            if (width <= 0 || height <= 0) return;
 
            var sliceCount = Mathf.CeilToInt(layers / 4f);
            sliceCount = Mathf.Min(sliceCount, dstTextures.Length);
            var weights = new float[height, width, layers];
 
            for (var slice = 0; slice < sliceCount; slice++)
            {
                var tex = dstTextures[slice];
                if (tex == null) continue;
 
                // 执行 GPU 读回（读取整张纹理，再按区域裁剪）
                var req = AsyncGPUReadback.Request(tex, 0);
                req.WaitForCompletion();
                if (req.hasError)
                {
                    Debug.LogWarning($"[GpuTerrainPainter] AsyncGPUReadback error on slice {slice}.");
                    continue;
                }
                var colors = req.GetData<Color32>();
                var baseLayer = slice * 4;
                var texW = tex.width;
                var texH = tex.height;

                for (int y = 0; y < height; y++)
                {
                    var pyFull = startY + y;
                    if (pyFull < 0 || pyFull >= texH) continue;
                    for (int x = 0; x < width; x++)
                    {
                        var pxFull = startX + x;
                        if (pxFull < 0 || pxFull >= texW) continue;
                        var idx = pyFull * texW + pxFull;
                        var c = colors[idx];
                        if (baseLayer + 0 < layers) weights[y, x, baseLayer + 0] = c.r / 255f;
                        if (baseLayer + 1 < layers) weights[y, x, baseLayer + 1] = c.g / 255f;
                        if (baseLayer + 2 < layers) weights[y, x, baseLayer + 2] = c.b / 255f;
                        if (baseLayer + 3 < layers) weights[y, x, baseLayer + 3] = c.a / 255f;
                    }
                }
            }
 
            // 调用 SetAlphamaps 以触发 Basemap 重建（仅限该区域）
            data.SetAlphamaps(startX, startY, weights);
        }

        // 新增：基于 RenderTexture 切片的区域 Basemap 重建（用于实时 GPU 预览）
        public static void RebuildBasemapFromRTRegion(TerrainData data, RenderTexture rt, int layers, int startX, int startY, int width, int height)
        {
            if (data == null || rt == null) return;
            if (width <= 0 || height <= 0) return;
            var sliceCount = Mathf.Min(Mathf.CeilToInt(layers / 4f), rt.volumeDepth);
            var weights = new float[height, width, layers];
            var texW = rt.width;
            var texH = rt.height;
 
            for (var slice = 0; slice < sliceCount; slice++)
            {
                var req = AsyncGPUReadback.Request(rt, slice, TextureFormat.ARGB32);
                req.WaitForCompletion();
                if (req.hasError)
                {
                    Debug.LogWarning($"[GpuTerrainPainter] AsyncGPUReadback error on RT slice {slice}.");
                    continue;
                }
                var colors = req.GetData<Color32>();
                var baseLayer = slice * 4;
 
                for (int y = 0; y < height; y++)
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
