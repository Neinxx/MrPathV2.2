using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Preview;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    ///     Manages texture arrays for preview materials.
    ///     This class is responsible for creating and managing Texture2DArrays used in multi-layer preview shaders.
    /// </summary>
    public class PreviewTextureArrayManager
    {

        private TextureArrayCacheKey? _mCurrentCacheKey;
        private RenderTexture _mLayerRtArray;
        private CommandBuffer _mReusableCommandBuffer; // Reusable CommandBuffer

        /// <summary>
        ///     Attempts to create and bind a texture array from a list of textures.
        /// </summary>
        /// <param name="material">Material to bind the texture array to</param>
        /// <param name="texList">List of textures to include in the array</param>
        /// <param name="width">Width of the texture array</param>
        /// <param name="height">Height of the texture array</param>
        /// <param name="textureHashes">String representation of texture hashes</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryCreateAndBindTextureArray(Material material, List<Texture2D> texList, int width, int height, string textureHashes)
        {
            // Early return for invalid inputs
            if (!ValidateInputs(material, texList))
                return false;

            // Try to reuse cached texture array
            var newCacheKey = new TextureArrayCacheKey(texList.Count, width, height, textureHashes);
            return TryReuseCachedTextureArray(material, newCacheKey) ||
                   // Create new texture array
                   CreateAndBindNewTextureArray(material, texList, width, height, newCacheKey);

        }

        /// <summary>
        ///     Validates input parameters
        /// </summary>
        /// <param name="material">Material to validate</param>
        /// <param name="texList">Texture list to validate</param>
        /// <returns>True if inputs are valid</returns>
        private static bool ValidateInputs(Material material, List<Texture2D> texList) => material && texList is { Count: > 0 };

        /// <summary>
        ///     Tries to reuse a cached texture array if available
        /// </summary>
        /// <param name="material">Material to bind the texture to</param>
        /// <param name="newCacheKey">Cache key for the new texture array</param>
        /// <returns>True if cache was reused</returns>
        private bool TryReuseCachedTextureArray(Material material, TextureArrayCacheKey newCacheKey)
        {
            var canReuseCache = _mCurrentCacheKey.HasValue &&
                                _mCurrentCacheKey.Value.Equals(newCacheKey) &&
                                _mLayerRtArray != null &&
                                _mLayerRtArray.IsCreated();

            if (canReuseCache)
            {
                BindTextureArrayToMaterial(material, _mLayerRtArray);
            }

            return canReuseCache;
        }

        /// <summary>
        ///     Creates and binds a new texture array
        /// </summary>
        /// <param name="material">Material to bind the texture array to</param>
        /// <param name="texList">List of textures to include in the array</param>
        /// <param name="width">Width of the texture array</param>
        /// <param name="height">Height of the texture array</param>
        /// <param name="newCacheKey">Cache key for the new texture array</param>
        /// <returns>True if successful, false otherwise</returns>
        private bool CreateAndBindNewTextureArray(Material material, List<Texture2D> texList, int width, int height, TextureArrayCacheKey newCacheKey)
        {
            try
            {
                // Release old array
                ReleaseLayerTexArray();

                // Create new texture array
                _mLayerRtArray = CreateTextureArray(width, height, texList.Count);

                // Fill texture array with textures
                FillTextureArray(_mLayerRtArray, texList);

                // Update cache key
                _mCurrentCacheKey = newCacheKey;

                // Bind to material
                BindTextureArrayToMaterial(material, _mLayerRtArray);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PreviewTextureArrayManager] Failed to build Texture2DArray. Reason: {e.Message}");
                return false;
            }
        }

        /// <summary>
        ///     Creates a new RenderTexture array
        /// </summary>
        /// <param name="width">Width of the texture array</param>
        /// <param name="height">Height of the texture array</param>
        /// <param name="depth">Number of slices in the texture array</param>
        /// <returns>New RenderTexture array</returns>
        private static RenderTexture CreateTextureArray(int width, int height, int depth)
        {
            var desc = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = depth,
                enableRandomWrite = false,
                useMipMap = false,
                msaaSamples = 1
            };

            var rtArray = new RenderTexture(desc)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                name = "Preview_Layers_RTArray"
            };

            if (!rtArray.Create())
            {
                throw new Exception("Failed to create RenderTexture Tex2DArray.");
            }

            return rtArray;
        }

        /// <summary>
        ///     Fills a texture array with textures
        /// </summary>
        /// <param name="rtArray">RenderTexture array to fill</param>
        /// <param name="texList">List of textures to fill with</param>
        private void FillTextureArray(RenderTexture rtArray, List<Texture2D> texList)
        {
            var commandBuffer = GetOrCreateCommandBuffer();
            commandBuffer.Clear();

            for (var slice = 0; slice < texList.Count; slice++)
            {
                commandBuffer.SetRenderTarget(rtArray, 0, CubemapFace.Unknown, slice);
                commandBuffer.ClearRenderTarget(false, true, Color.white);
                commandBuffer.Blit(texList[slice], BuiltinRenderTextureType.CurrentActive);
            }

            Graphics.ExecuteCommandBuffer(commandBuffer);
        }

        /// <summary>
        ///     Gets or creates a command buffer for texture operations
        /// </summary>
        /// <returns>Command buffer</returns>
        private CommandBuffer GetOrCreateCommandBuffer()
        {
            _mReusableCommandBuffer ??= new CommandBuffer
            {
                name = "BuildLayerRTArray"
            };
            return _mReusableCommandBuffer;
        }

        /// <summary>
        ///     Binds a texture array to a material
        /// </summary>
        /// <param name="material">Material to bind to</param>
        /// <param name="textureArray">Texture array to bind</param>
        private static void BindTextureArrayToMaterial(Material material, RenderTexture textureArray)
        {
            if (material.HasProperty(PreviewShaderContracts.Properties.LayerTextures))
                material.SetTexture(PreviewShaderContracts.Properties.LayerTextures, textureArray);
            if (material.HasProperty(PreviewShaderContracts.Properties.UseLayerTexArray))
                material.SetFloat(PreviewShaderContracts.Properties.UseLayerTexArray, 1f);
        }

        /// <summary>
        ///     Releases the layer texture array to avoid resource leaks.
        /// </summary>
        public void ReleaseLayerTexArray()
        {
            if (_mLayerRtArray != null)
            {
                _mLayerRtArray.Release();
                Object.DestroyImmediate(_mLayerRtArray);
                _mLayerRtArray = null;
            }

            // Clear cache key
            _mCurrentCacheKey = null;
        }

        /// <summary>
        ///     Cleans up all resources, including CommandBuffer.
        /// </summary>
        public void Cleanup()
        {
            ReleaseLayerTexArray();
            if (_mReusableCommandBuffer == null) return;
            _mReusableCommandBuffer.Release();
            _mReusableCommandBuffer = null;
        }

        // Texture array cache mechanism
        private struct TextureArrayCacheKey
        {
            public readonly int LayerCount;
            public readonly int Width;
            public readonly int Height;
            public readonly string TextureHashes; // Hash combination of all textures

            public TextureArrayCacheKey(int layerCount, int width, int height, string textureHashes)
            {
                LayerCount = layerCount;
                Width = width;
                Height = height;
                TextureHashes = textureHashes;
            }

            public override bool Equals(object obj)
            {
                if (obj is not TextureArrayCacheKey other) return false;
                return LayerCount == other.LayerCount &&
                       Width == other.Width &&
                       Height == other.Height &&
                       TextureHashes == other.TextureHashes;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + LayerCount;
                    hash = hash * 31 + Width;
                    hash = hash * 31 + Height;
                    hash = hash * 31 + (TextureHashes?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
