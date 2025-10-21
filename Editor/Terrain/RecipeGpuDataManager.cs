
using System.Collections.Generic;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
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


    public class RecipeGpuDataManager : IDisposable
    {
        public ComputeBuffer LayerParamsBuffer { get; private set; }
        public Texture2DArray TerrainTextureArray { get; private set; }
        // public Texture2D MaskAtlasTexture { get; private set; }

        private readonly List<GpuBlendLayerParams> _cachedLayerParams = new();
        private readonly List<Texture2D> _cachedTextures = new();
        private StylizedRoadRecipe _lastRecipe;
        private int _lastRecipeHash = -1;
        private readonly Dictionary<int, int> _maskInstanceIdToHash = new Dictionary<int, int>();

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

            var textureIndexCounter = 0;
            var textureToIndex = new Dictionary<Texture2D, int>();

            foreach (var layer in recipe.GetLayers()) // 假设 recipe.layers 已改为 blendLayers
            {
                // 确保 BlendLayer 结构体匹配 (假设是 blendLayers)
                // 如果你的 Recipe 结构体是 'layers' (类型 RoadLayer)，请用 'recipe.layers' 和 'layer.contentLayer'
                if (!layer.enabled || layer.contentLayer == null || layer.contentLayer.diffuseTexture == null) continue;

                var diffuseTex = layer.contentLayer.diffuseTexture;
                if (!textureToIndex.TryGetValue(diffuseTex, out var texIndex))
                {
                    texIndex = textureIndexCounter++;
                    textureToIndex.Add(diffuseTex, texIndex);
                    _cachedTextures.Add(diffuseTex);
                }

                if (layerMap == null || !layerMap.TryGetValue(layer.contentLayer, out var splatIndex))
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
                       var maskId = activeMask.GetInstanceID();
                       var currentMaskHash = CalculateMaskHash(activeMask);
                       if(!_maskInstanceIdToHash.TryGetValue(maskId, out var previousHash) || previousHash != currentMaskHash)
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

            var tiling = mask.tiling;
            tiling.x = Mathf.Max(0.0001f, tiling.x);
            tiling.y = Mathf.Max(0.0001f, tiling.y);
            var offset = mask.offset;
            var overallScale = mask.overallScale;
            var smooth = mask.smooth;

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
            else if (mask is NoiseMask)
            {
                 var pmb = (ProceduralMaskBase)mask;
                 gpuMask.MaskType = 2;
                 gpuMask.NoiseParams = new GpuNoiseMaskParams();
                 gpuMask.NoiseParams.Strength = pmb.strength;
                 gpuMask.NoiseParams.Seed = pmb.seed;
                 gpuMask.NoiseParams.Tiling = tiling;
                 gpuMask.NoiseParams.Offset = offset;
                 gpuMask.NoiseParams.OverallScale = overallScale;
                 gpuMask.NoiseParams.Smooth = smooth;
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

            var width = _cachedTextures[0].width;
            var height = _cachedTextures[0].height;
            var format = _cachedTextures[0].graphicsFormat;
            var mipChain = _cachedTextures[0].mipmapCount > 1;

            if (!TerrainTextureArray || TerrainTextureArray.width != width || TerrainTextureArray.height != height || TerrainTextureArray.depth != _cachedTextures.Count || TerrainTextureArray.graphicsFormat != format)
            {
                ReleaseTextureArray();
                TerrainTextureArray = new Texture2DArray(width, height, _cachedTextures.Count, format, mipChain ? TextureCreationFlags.MipChain : TextureCreationFlags.None)
                    {
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = mipChain ? FilterMode.Trilinear : FilterMode.Bilinear,
                        name = "TerrainLayer Texture Array"
                    };
            }

            for (var i = 0; i < _cachedTextures.Count; i++)
            {
                 if (_cachedTextures[i].width != width || _cachedTextures[i].height != height || _cachedTextures[i].graphicsFormat != format)
                 {
                      Debug.LogWarning($"Texture '{_cachedTextures[i].name}' has incompatible dimensions/format. Expected {width}x{height} {format}. Skipping.");
                      continue;
                 }
                 var mipCount = mipChain ? _cachedTextures[i].mipmapCount : 1;
                 for (var mip = 0; mip < mipCount; mip++)
                 {
                     Graphics.CopyTexture(_cachedTextures[i], 0, mip, TerrainTextureArray, i, mip);
                 }
            }
        }


        private void UpdateComputeBuffer()
        {
            if (ActiveLayerCount > 0)
            {
                var stride = Marshal.SizeOf(typeof(GpuBlendLayerParams));
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
                var hash = 17;
                hash = hash * 23 + recipe.GetInstanceID();
                hash = hash * 23 + recipe.masterOpacity.GetHashCode();
                hash = hash * 23 + recipe.GetLayers().Count; // 假设是 'blendLayers'
                foreach(var layer in recipe.GetLayers())
                {
                    if (!layer.enabled) continue;
                    hash = hash * 23 + (layer.contentLayer?.GetInstanceID() ?? 0);
                    var activeMask = layer.layerMask;
                    hash = hash * 23 + (activeMask?.GetInstanceID() ?? 0);
                    hash = hash * 23 + layer.opacity.GetHashCode();
                    hash = hash * 23 + layer.blendMode.GetHashCode();
                    if (activeMask != null) hash = hash * 23 + CalculateMaskHash(activeMask);
                }
                return hash;
            }
        }
        
        private static int CalculateMaskHash(BlendMaskBase mask)
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