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

namespace __temp.MrPathV2._2.Editor.Terrain
{
    // --- GpuDataStructures (放在同一文件或单独文件) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuShoulderMaskParams {
        public float ShoulderWidthRatio;
        public float ShoulderStrength;
        public float EdgeFalloff;
        public int EnableLeftShoulder;
        public int EnableRightShoulder;
        public Vector2 Tiling;
        public Vector2 Offset;
        public float OverallScale;
        public float Smooth;
        public float Pad1;
        public float Pad2;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuNoiseMaskParams {
        public float Strength;
        public float Seed;
        public Vector2 Tiling;
        public Vector2 Offset;
        public float OverallScale;
        public float Smooth;
        public float Pad1;
    }
    // TODO: Define GpuGradientMaskParams, GpuRoadSurfaceMaskParams etc.
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuMaskParams {
        public int MaskType;
        public float Pad1, Pad2, Pad3;
        public GpuShoulderMaskParams ShoulderParams;
        public GpuNoiseMaskParams NoiseParams;
        // ... other mask structs ...
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

        /// <summary>
        /// 获取当前为 GPU 准备的活动图层数量。
        /// </summary>
        public int ActiveLayerCount => _cachedLayerParams?.Count ?? 0;

        public bool UpdateData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> layerMap)
        {
            if (recipe == null)
            {
                ReleaseBuffers();
                return _lastRecipe != null;
            }

            var newHash = CalculateRecipeDeepHash(recipe);
            if (newHash == _lastRecipeHash && _lastRecipe == recipe)
            {
                 if (!CheckMaskParametersChanged(recipe))
                 {
                      return false;
                 }
                 newHash = CalculateRecipeDeepHash(recipe);
            }

            Debug.Log("[RecipeGpuDataManager] Recipe data changed, updating GPU buffers.");
            _lastRecipe = recipe;
            _lastRecipeHash = newHash;

            _cachedLayerParams.Clear();
            _cachedTextures.Clear();
            _maskInstanceIdToHash.Clear();

            int textureIndexCounter = 0;
            var textureToIndex = new Dictionary<Texture2D, int>();

            foreach (var layer in recipe.GetLayers()) // 假设 recipe.layers 已改为 blendLayers
            {
                // 确保 BlendLayer 结构体匹配 (假设是 blendLayers)
                // 如果你的 Recipe 结构体是 'layers' (类型 RoadLayer)，请用 'recipe.layers' 和 'layer.contentLayer'
                if (!layer.enabled || layer.contentLayer == null || layer.contentLayer.diffuseTexture == null) continue;

                var diffuseTex = layer.contentLayer.diffuseTexture;
                if (!textureToIndex.TryGetValue(diffuseTex, out int texIndex))
                {
                    texIndex = textureIndexCounter++;
                    textureToIndex.Add(diffuseTex, texIndex);
                    _cachedTextures.Add(diffuseTex);
                }

                if (layerMap == null || !layerMap.TryGetValue(layer.contentLayer, out int splatIndex))
                {
                    splatIndex = -1;
                }

                var activeMask = layer.layerMask; // 使用 BlendLayer.GetActiveMask()
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
        
        private bool CheckMaskParametersChanged(StylizedRoadRecipe recipe)
        {
             if (recipe == null) return false;
             foreach(var layer in recipe.GetLayers()) // 假设是 'blendLayers'
             {
                  var activeMask = layer.layerMask;
                  if(layer.enabled && activeMask != null)
                  {
                       int maskId = activeMask.GetInstanceID();
                       int currentMaskHash = CalculateMaskHash(activeMask);
                       if(!_maskInstanceIdToHash.TryGetValue(maskId, out int previousHash) || previousHash != currentMaskHash)
                       {
                            return true;
                       }
                  }
             }
             return false;
        }

        private GpuMaskParams PackMaskParams(BlendMaskBase mask)
        {
            var gpuMask = new GpuMaskParams { MaskType = 0 };
            if (mask == null) return gpuMask;

            Vector2 tiling = mask.tiling;
            tiling.x = Mathf.Max(0.0001f, tiling.x);
            tiling.y = Mathf.Max(0.0001f, tiling.y);
            Vector2 offset = mask.offset;
            float overallScale = mask.overallScale;
            float smooth = mask.smooth;

            if (mask is ShoulderMask sm)
            {
                gpuMask.MaskType = 1;
                gpuMask.ShoulderParams = new GpuShoulderMaskParams {
                    ShoulderWidthRatio = sm.shoulderWidthRatio,
                    ShoulderStrength = sm.shoulderStrength,
                    EdgeFalloff = sm.edgeFalloff,
                    EnableLeftShoulder = sm.enableLeftShoulder ? 1 : 0,
                    EnableRightShoulder = sm.enableRightShoulder ? 1 : 0,
                    Tiling = tiling, Offset = offset, OverallScale = overallScale, Smooth = smooth
                };
            }
            else if (mask is NoiseMask nm)
            {
                 var pmb = mask as ProceduralMaskBase;
                 gpuMask.MaskType = 2;
                 gpuMask.NoiseParams = new GpuNoiseMaskParams {
                     Strength = pmb?.strength ?? 1.0f,
                     Seed = pmb?.seed ?? 0.0f,
                     Tiling = tiling, Offset = offset, OverallScale = overallScale, Smooth = smooth
                 };
            }
            // TODO: else if (mask is GradientMask gm) { ... }
            // TODO: else if (mask is RoadSurfaceMask rsm) { ... }
            // ...

            return gpuMask;
        }

        private void UpdateTextureArray()
        {
            if (_cachedTextures.Count == 0)
            {
                ReleaseTextureArray();
                return;
            }

            // Find the most common texture format and dimensions to use as the standard
            var formatCounts = new Dictionary<(int width, int height, GraphicsFormat format), int>();
            foreach (var texture in _cachedTextures)
            {
                var key = (texture.width, texture.height, texture.graphicsFormat);
                formatCounts[key] = formatCounts.GetValueOrDefault(key, 0) + 1;
            }

            var mostCommon = formatCounts.OrderByDescending(kvp => kvp.Value).First();
            int width = mostCommon.Key.width;
            int height = mostCommon.Key.height;
            GraphicsFormat format = mostCommon.Key.format;
            bool mipChain = _cachedTextures.Any(t => t.mipmapCount > 1);

            // Filter out incompatible textures and create a list of valid ones
            var validTextures = new List<Texture2D>();
            var textureIndices = new List<int>(); // Track original indices
            
            for (int i = 0; i < _cachedTextures.Count; i++)
            {
                var texture = _cachedTextures[i];
                if (texture.width == width && texture.height == height && texture.graphicsFormat == format)
                {
                    validTextures.Add(texture);
                    textureIndices.Add(i);
                }
                else
                {
                    Debug.LogWarning($"Texture '{texture.name}' has incompatible dimensions/format. Expected {width}x{height} {format}, got {texture.width}x{texture.height} {texture.graphicsFormat}. This texture will be excluded from the array.");
                }
            }

            if (validTextures.Count == 0)
            {
                Debug.LogError("No valid textures found for texture array creation. All textures have incompatible formats.");
                ReleaseTextureArray();
                return;
            }

            if (TerrainTextureArray == null || TerrainTextureArray.width != width || TerrainTextureArray.height != height || TerrainTextureArray.depth != validTextures.Count || TerrainTextureArray.graphicsFormat != format)
            {
                ReleaseTextureArray();
                TerrainTextureArray = new Texture2DArray(width, height, validTextures.Count, format, mipChain ? TextureCreationFlags.MipChain : TextureCreationFlags.None);
                TerrainTextureArray.wrapMode = TextureWrapMode.Repeat;
                TerrainTextureArray.filterMode = mipChain ? FilterMode.Trilinear : FilterMode.Bilinear;
                TerrainTextureArray.name = "TerrainLayer Texture Array";
            }

            for (int i = 0; i < validTextures.Count; i++)
            {
                int mipCount = mipChain ? validTextures[i].mipmapCount : 1;
                for (int mip = 0; mip < mipCount; mip++)
                {
                    Graphics.CopyTexture(validTextures[i], 0, mip, TerrainTextureArray, i, mip);
                }
            }

            // Log summary of texture array creation
            Debug.Log($"Created texture array with {validTextures.Count} textures ({width}x{height}, {format}). Excluded {_cachedTextures.Count - validTextures.Count} incompatible textures.");
        }


        private void UpdateComputeBuffer()
        {
            if (ActiveLayerCount > 0)
            {
                int stride = Marshal.SizeOf(typeof(GpuBlendLayerParams));
                if (LayerParamsBuffer == null || !LayerParamsBuffer.IsValid() || LayerParamsBuffer.count < ActiveLayerCount || LayerParamsBuffer.stride != stride)
                {
                    LayerParamsBuffer?.Release();
                    LayerParamsBuffer = new ComputeBuffer(Mathf.Max(1, ActiveLayerCount), stride, ComputeBufferType.Structured);
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
                foreach(var layer in recipe.GetLayers()) {
                    if (layer.enabled) {
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
        
        private int CalculateMaskHash(BlendMaskBase mask)
        {
             if (mask == null) return 0;
             try {
                  return JsonUtility.ToJson(mask).GetHashCode();
             } catch (Exception) {
                  return mask.GetInstanceID();
             }
        }

        public void Dispose() { ReleaseBuffers(); }
        private void ReleaseBuffers() { ReleaseComputeBuffer(); ReleaseTextureArray(); }
        private void ReleaseComputeBuffer() { LayerParamsBuffer?.Release(); LayerParamsBuffer = null; }
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
        }
    }
}