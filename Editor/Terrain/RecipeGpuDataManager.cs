// 文件: Editor/Terrain/RecipeGpuDataManager.cs
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Runtime.InteropServices;
using System;
using MrPathV2; // For Exception
using UnityEngine.Rendering;

namespace __temp.MrPathV2._2.Editor.Terrain
{
    // --- GpuDataStructures (与 HLSL BlendMaskLibrary.hlsl 保持一致) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuNoiseMaskParams
    {
        public float Strength; // 对应 HLSL: float Strength
        public float Seed;     // 对应 HLSL: float Seed
        public Vector2 Tiling; // 对应 HLSL: float2 Tiling（米制平铺）
        public Vector2 Offset; // 对应 HLSL: float2 Offset
        public float OverallScale; // 额外整体缩放（与 CPU 一致）
        public float Smooth;       // 平滑/软化系数
        public Vector2 NoiseScale; // 细节缩放（与 CPU 一致）
        public float RotationRad;  // 旋转角（弧度）
        public int Octaves;        // fBm 八度数
        public float Lacunarity;   // 频率倍增系数
        public float Gain;         // 幅度衰减系数
        public int AlgorithmId;    // 噪声算法调度 ID（0=Perlin，1=Simple）
        public float Pad1;         // 对齐填充，保证 16B 对齐
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuShoulderMaskParams
    {
        public float Width; // 比例值，最终在 HLSL 中乘以 roadWidth
        public float Softness; // 软化系数
        public float Strength; // 强度
        public float Pad; // 对齐填充
    }

    // TODO: 如需扩展更多遮罩，在此追加结构并同步 HLSL
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuMaskParams
    {
        public int Type; // 与 HLSL MASK_TYPE_* 对齐

        public float Strength; // 顶层强度系数

        // 结构体顺序必须与 HLSL 相同：Noise 在前，Shoulder 在后
        public GpuNoiseMaskParams Noise;
        public GpuShoulderMaskParams Shoulder;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuBlendLayerParams
    {
        public int BlendMode;
        public float Opacity;
        public int TextureIndex;
        public int TerrainLayerSplatIndex;
        public Vector4 TilingOffset;
        public Color TintColor;
        public GpuMaskParams MaskParams;
    }
    // ----------------------------------------------


    public class RecipeGpuDataManager : System.IDisposable
    {
        public ComputeBuffer LayerParamsBuffer { get; private set; }
        public Texture2DArray TerrainTextureArray { get; private set; }
        // public Texture2D MaskAtlasTexture { get; private set; }

        private List<GpuBlendLayerParams> _cachedLayerParams = new();
        private List<Texture2D> _cachedTextures = new();
        private StylizedRoadRecipe _lastRecipe;
        private int _lastRecipeHash = -1;
        private Dictionary<int, int> _maskInstanceIdToHash = new Dictionary<int, int>();
        private RenderTexture _stagingRt; // 复用的中转 RT
        private int _lastLayerMapHash = -1; // 新增：跟踪上次的层映射哈希

        /// <summary>
        /// 获取当前为 GPU 准备的活动图层数量。
        /// </summary>
        public int ActiveLayerCount => _cachedLayerParams?.Count ?? 0;

        private bool CheckMaskParametersChanged(StylizedRoadRecipe recipe)
        {
            if (recipe == null) return false;
            foreach (var layer in recipe.GetLayers()) // 假设是 'blendLayers'
            {
                var activeMask = layer.layerMask;
                if (layer.enabled && activeMask != null)
                {
                    int maskId = activeMask.GetInstanceID();
                    int currentMaskHash = CalculateMaskHash(activeMask);
                    if (!_maskInstanceIdToHash.TryGetValue(maskId, out int previousHash) ||
                        previousHash != currentMaskHash)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private GpuMaskParams PackMaskParams(BlendMaskBase mask)
        {
            var gpuMask = new GpuMaskParams { Type = 0, Strength = 1.0f };
            if (mask == null) return gpuMask;

            // 通过统一接口收集遮罩参数
            var dto = new __temp.MrPathV2._2.Runtime.Core.GpuMaskParamsData();
            mask.FillGpuParams(ref dto);

            // 映射到 Editor 侧 GPU 结构（与 HLSL 完全一致）
            gpuMask.Type = dto.MaskType;
            gpuMask.Strength = dto.Strength;

            // Noise -> 全量参数映射（与 CPU / MaskAtlas 保持一致）
            gpuMask.Noise = new GpuNoiseMaskParams
            {
                Strength   = dto.NoiseParams.Strength,
                Seed       = dto.NoiseParams.Seed,
                Tiling     = dto.NoiseParams.Tiling,
                Offset     = dto.NoiseParams.Offset,
                OverallScale = dto.NoiseParams.OverallScale,
                Smooth     = dto.NoiseParams.Smooth,
                NoiseScale = dto.NoiseParams.NoiseScale,
                RotationRad = dto.NoiseParams.RotationRad,
                Octaves    = dto.NoiseParams.Octaves,
                Lacunarity = dto.NoiseParams.Lacunarity,
                Gain       = dto.NoiseParams.Gain,
                AlgorithmId = dto.NoiseParams.AlgorithmId,
                Pad1       = 0f
            };

            // Shoulder -> 比例/软化/强度
            gpuMask.Shoulder = new GpuShoulderMaskParams
            {
                Width = Mathf.Max(0.0001f, dto.ShoulderParams.ShoulderWidthRatio), // 比例值，HLSL 中乘 roadWidth
                Softness = Mathf.Max(0.0001f, dto.ShoulderParams.EdgeFalloff), // 作为软化控制
                Strength = dto.ShoulderParams.ShoulderStrength,
                Pad = 0f
            };

            return gpuMask;
        }

        private void UpdateTextureArray()
        {
            // Count formats and choose the most common size/format among cached textures
            var formatCounts = new Dictionary<(int width, int height, GraphicsFormat format), int>();
            foreach (var texture in _cachedTextures)
            {
                var key = (texture.width, texture.height, texture.graphicsFormat);
                formatCounts[key] = formatCounts.GetValueOrDefault(key, 0) + 1;
            }

            var mostCommon = formatCounts.OrderByDescending(kvp => kvp.Value).First();
            int width = mostCommon.Key.width;
            int height = mostCommon.Key.height;
            GraphicsFormat srcCommonFormat = mostCommon.Key.format;
            bool mipChainRequested = _cachedTextures.Any(t => t.mipmapCount > 1);

            // Filter out incompatible textures and create a list of valid ones (same size & format)
            var validTextures = new List<Texture2D>();
            var textureIndices = new List<int>(); // Track original indices
            for (int i = 0; i < _cachedTextures.Count; i++)
            {
                var texture = _cachedTextures[i];
                if (texture.width == width && texture.height == height && texture.graphicsFormat == srcCommonFormat)
                {
                    validTextures.Add(texture);
                    textureIndices.Add(i);
                }
                else
                {
                    Debug.LogWarning(
                        $"Texture '{texture.name}' has incompatible dimensions/format. Expected {width}x{height} {srcCommonFormat}, got {texture.width}x{texture.height} {texture.graphicsFormat}. This texture will be excluded from the array.");
                }
            }

            if (validTextures.Count == 0)
            {
                Debug.LogError(
                    "No valid textures found for texture array creation. All textures have incompatible formats.");
                ReleaseTextureArray();
                return;
            }

            // Decide target array format: prefer original when renderable; otherwise fall back to uncompressed RGBA8
            bool srcIsSRGB = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(srcCommonFormat);
            bool srcIsCompressed =
                UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(srcCommonFormat);
            bool srcRenderable = SystemInfo.IsFormatSupported(srcCommonFormat, FormatUsage.Render);
            GraphicsFormat targetFormat = srcCommonFormat;
            if (srcIsCompressed || !srcRenderable)
            {
                targetFormat = srcIsSRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            }

            // Staging RT can only be created for renderable formats; use mips only when we can stage
            bool stagingRenderable = SystemInfo.IsFormatSupported(targetFormat, FormatUsage.Render);
            bool useMip = mipChainRequested && stagingRenderable;

            // (Re)create the Texture2DArray with target format
            if (TerrainTextureArray == null || TerrainTextureArray.width != width ||
                TerrainTextureArray.height != height || TerrainTextureArray.depth != validTextures.Count ||
                TerrainTextureArray.graphicsFormat != targetFormat)
            {
                ReleaseTextureArray();
                TerrainTextureArray = new Texture2DArray(width, height, validTextures.Count, targetFormat,
                    useMip ? TextureCreationFlags.MipChain : TextureCreationFlags.None);
                TerrainTextureArray.wrapMode = TextureWrapMode.Repeat;
                TerrainTextureArray.filterMode = useMip ? FilterMode.Trilinear : FilterMode.Bilinear;
                TerrainTextureArray.name = "TerrainLayer Texture Array";
            }

            // Prepare staging RT if renderable; otherwise we will fall back to ConvertTexture base level only
            if (stagingRenderable)
            {
                EnsureStagingRt(width, height, targetFormat, useMip);
            }

            var cmd = new CommandBuffer { name = "RecipeGpuDataManager TextureArray Build" };
            var stagingId = stagingRenderable ? new RenderTargetIdentifier(_stagingRt) : default;

            for (int i = 0; i < validTextures.Count; i++)
            {
                var srcTex = validTextures[i];
                int mipCount = useMip ? srcTex.mipmapCount : 1;

                bool isCrunch = false;
#if UNITY_EDITOR
                isCrunch = IsCrunchCompressed(srcTex);
#endif
                bool sameFormatAsTarget = srcTex.graphicsFormat == targetFormat;

                if (!stagingRenderable)
                {
                    // Last-resort path: convert base level into uncompressed array slice (mips skipped)
                    Graphics.ConvertTexture(srcTex, 0, TerrainTextureArray, i);
                    if (mipCount > 1)
                    {
                        Debug.LogWarning(
                            $"[RecipeGpuDataManager] '{srcTex.name}' target format {targetFormat} cannot use staging RT; converted base level only. Mipmaps skipped.");
                    }

                    continue;
                }

                // Use staging blit whenever conversion is needed (Crunch or format mismatch)
                bool needConvert = isCrunch || !sameFormatAsTarget;
                if (needConvert)
                {
                    cmd.Blit(srcTex, stagingId);
                    if (useMip) cmd.GenerateMips(stagingId);
                    for (int mip = 0; mip < mipCount; mip++)
                    {
                        cmd.CopyTexture(_stagingRt, 0, mip, TerrainTextureArray, i, mip);
                    }
                }
                else
                {
                    // Direct copy when formats match and not Crunch
                    for (int mip = 0; mip < mipCount; mip++)
                    {
                        cmd.CopyTexture(srcTex, 0, mip, TerrainTextureArray, i, mip);
                    }
                }
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            Debug.Log(
                $"Created texture array with {validTextures.Count} textures ({width}x{height}, {targetFormat}). Excluded {_cachedTextures.Count - validTextures.Count} incompatible textures.");
        }


        private void UpdateComputeBuffer()
        {
            if (ActiveLayerCount > 0)
            {
                int stride = Marshal.SizeOf(typeof(GpuBlendLayerParams));
                if (LayerParamsBuffer == null || !LayerParamsBuffer.IsValid() ||
                    LayerParamsBuffer.count < ActiveLayerCount || LayerParamsBuffer.stride != stride)
                {
                    LayerParamsBuffer?.Release();
                    LayerParamsBuffer = new ComputeBuffer(Mathf.Max(1, ActiveLayerCount), stride,
                        ComputeBufferType.Structured);
                }

                LayerParamsBuffer.SetData(_cachedLayerParams, 0, 0, ActiveLayerCount);
            }
            else
            {
                ReleaseComputeBuffer();
            }
        }

        private int CalculateRecipeDeepHash(StylizedRoadRecipe recipe)
        {
            if (recipe == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + recipe.GetInstanceID();
                hash = hash * 23 + recipe.masterOpacity.GetHashCode();
                hash = hash * 23 + recipe.GetLayers().Count; // 假设是 'blendLayers'
                foreach (var layer in recipe.GetLayers())
                {
                    if (layer.enabled)
                    {
                        hash = hash * 23 + (layer.contentLayer?.GetInstanceID() ?? 0);
                        var activeMask = layer.layerMask;
                        hash = hash * 23 + (activeMask?.GetInstanceID() ?? 0);
                        hash = hash * 23 + layer.opacity.GetHashCode();
                        hash = hash * 23 + layer.blendMode.GetHashCode();
                        if (activeMask != null) hash = hash * 23 + CalculateMaskHash(activeMask);
                    }
                }

                return hash;
            }
        }

        // 新增：为 layerMap 计算一个稳定哈希，用于变更检测
        private static int HashLayerMap(Dictionary<TerrainLayer, int> layerMap)
        {
            if (layerMap == null || layerMap.Count == 0) return 0;
            unchecked
            {
                int h = 17;
                foreach (var kv in layerMap)
                {
                    int keyId = kv.Key ? kv.Key.GetInstanceID() : 0;
                    h = h * 23 + keyId;
                    h = h * 23 + kv.Value;
                }

                return h;
            }
        }

        private int CalculateMaskHash(BlendMaskBase mask)
        {
            if (mask == null) return 0;
            try
            {
                return JsonUtility.ToJson(mask).GetHashCode();
            }
            catch (Exception)
            {
                return mask.GetInstanceID();
            }
        }

        public void Dispose()
        {
            ReleaseBuffers();
        }

        private void ReleaseBuffers()
        {
            ReleaseComputeBuffer();
            ReleaseTextureArray();
        }

        private void ReleaseComputeBuffer()
        {
            LayerParamsBuffer?.Release();
            LayerParamsBuffer = null;
        }

        private void ReleaseTextureArray()
        {
            if (TerrainTextureArray != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(TerrainTextureArray);
#else
                 UnityEngine.Object.Destroy(TerrainTextureArray);
#endif
                TerrainTextureArray = null;
            }

            if (_stagingRt != null)
            {
                _stagingRt.Release();
                UnityEngine.Object.DestroyImmediate(_stagingRt);
                _stagingRt = null;
            }
        }
#if UNITY_EDITOR
        private static bool IsCrunchCompressed(Texture2D tex)
        {
            if (tex == null) return false;
            try
            {
                var path = UnityEditor.AssetDatabase.GetAssetPath(tex);
                var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                if (importer != null && importer.crunchedCompression) return true;
            }
            catch
            {
            }

            var fmt = tex.format;
            return fmt == TextureFormat.DXT1Crunched
                   || fmt == TextureFormat.DXT5Crunched
                   || fmt == TextureFormat.ETC_RGB4Crunched
                   || fmt == TextureFormat.ETC2_RGBA8Crunched;
        }
#endif

        private void CopyTextureSliceCrunchSafe(Texture2D src, int sliceIndex, int mipCount, GraphicsFormat format)
        {
            bool useMip = mipCount > 1;
            var desc = new RenderTextureDescriptor(src.width, src.height, format, 0)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
                useMipMap = useMip,
                autoGenerateMips = false
            };
            var rt = RenderTexture.GetTemporary(desc);
            try
            {
                Graphics.Blit(src, rt);
                if (useMip)
                {
                    rt.GenerateMips();
                }

                for (int mip = 0; mip < mipCount; mip++)
                {
                    Graphics.CopyTexture(rt, 0, mip, TerrainTextureArray, sliceIndex, mip);
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void EnsureStagingRt(int width, int height, GraphicsFormat format, bool useMip)
        {
            if (_stagingRt != null)
            {
                if (_stagingRt.width == width && _stagingRt.height == height && _stagingRt.graphicsFormat == format &&
                    _stagingRt.useMipMap == useMip)
                {
                    return; // 已匹配，复用
                }

                _stagingRt.Release();
                UnityEngine.Object.DestroyImmediate(_stagingRt);
            }

            _stagingRt = new RenderTexture(width, height, 0, format)
            {
                useMipMap = useMip,
                autoGenerateMips = false,
                name = "RecipeGPU_StagingRT"
            };
            _stagingRt.Create();
        }





        public bool UpdateData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> layerMap)
        {
            if (recipe == null)
            {
                ReleaseBuffers();
                return _lastRecipe != null;
            }

            int newHash = CalculateRecipeDeepHash(recipe);
            int layerMapHash = HashLayerMap(layerMap);

            bool recipeUnchanged = (newHash == _lastRecipeHash && _lastRecipe == recipe);
            if (recipeUnchanged && !CheckMaskParametersChanged(recipe))
            {
                if (_cachedLayerParams.Count > 0)
                {
                    // 仅刷新 splat 索引映射，不重建纹理数组
                    for (int i = 0, j = 0; i < recipe.GetLayers().Count; i++)
                    {
                        var layer = recipe.GetLayers()[i];
                        if (!layer.enabled || layer.contentLayer == null || layer.contentLayer.diffuseTexture == null)
                            continue;
                        int splatIndex = -1;
                        if (layerMap != null && layerMap.TryGetValue(layer.contentLayer, out var si))
                            splatIndex = si;
                        var p = _cachedLayerParams[j];
                        p.TerrainLayerSplatIndex = splatIndex;
                        _cachedLayerParams[j] = p;
                        j++;
                    }

                    _lastLayerMapHash = layerMapHash;
                    if (LayerParamsBuffer == null || !LayerParamsBuffer.IsValid())
                    {
                        UpdateComputeBuffer();
                    }
                    else
                    {
                        LayerParamsBuffer.SetData(_cachedLayerParams, 0, 0, ActiveLayerCount);
                    }

                    return true;
                }
            }

            Debug.Log("[RecipeGpuDataManager] Recipe or mask changed, rebuilding GPU buffers.");
            _lastRecipe = recipe;
            _lastRecipeHash = newHash;
            _lastLayerMapHash = layerMapHash;

            _cachedLayerParams.Clear();
            _cachedTextures.Clear();
            _maskInstanceIdToHash.Clear();

            int textureIndexCounter = 0;
            var textureToIndex = new Dictionary<Texture2D, int>();

            foreach (var layer in recipe.GetLayers())
            {
                if (!layer.enabled || layer.contentLayer == null || layer.contentLayer.diffuseTexture == null) continue;

                var diffuseTex = layer.contentLayer.diffuseTexture;
                if (!textureToIndex.TryGetValue(diffuseTex, out int texIndex))
                {
                    texIndex = textureIndexCounter++;
                    textureToIndex.Add(diffuseTex, texIndex);
                    _cachedTextures.Add(diffuseTex);
                }

                int splatIndex = -1;
                if (layerMap != null && layerMap.TryGetValue(layer.contentLayer, out var si))
                    splatIndex = si;

                var activeMask = layer.layerMask;
                var packedMask = PackMaskParams(activeMask);
                if (activeMask != null)
                {
                    _maskInstanceIdToHash[activeMask.GetInstanceID()] = CalculateMaskHash(activeMask);
                }

                var gpuParams = new GpuBlendLayerParams
                {
                    BlendMode = (int)layer.blendMode,
                    Opacity = layer.opacity * recipe.masterOpacity,
                    TextureIndex = texIndex,
                    TerrainLayerSplatIndex = splatIndex,
                    TilingOffset = new Vector4(
                        layer.contentLayer.tileSize.x != 0 ? layer.contentLayer.tileSize.x : 1f,
                        layer.contentLayer.tileSize.y != 0 ? layer.contentLayer.tileSize.y : 1f,
                        layer.contentLayer.tileOffset.x,
                        layer.contentLayer.tileOffset.y),
                    TintColor = Color.white,
                    MaskParams = packedMask
                };
                _cachedLayerParams.Add(gpuParams);
            }

            UpdateTextureArray();
            UpdateComputeBuffer();

            return true;
        }
    }

}
