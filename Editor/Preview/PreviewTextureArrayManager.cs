using System;
using System.Collections.Generic;
using __temp.MrPathV2.Runtime.Preview;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    /// Manages texture arrays for preview materials.
    /// This class is responsible for creating and managing Texture2DArrays used in multi-layer preview shaders.
    /// </summary>
    public class PreviewTextureArrayManager
    {
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
                if (!(obj is TextureArrayCacheKey)) return false;
                var other = (TextureArrayCacheKey)obj;
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

        private TextureArrayCacheKey? m_CurrentCacheKey;
        private RenderTexture m_LayerRtArray;
        private CommandBuffer m_ReusableCommandBuffer; // Reusable CommandBuffer
        
        /// <summary>
        /// Attempts to create and bind a texture array from a list of textures.
        /// </summary>
        /// <param name="material">Material to bind the texture array to</param>
        /// <param name="texList">List of textures to include in the array</param>
        /// <param name="width">Width of the texture array</param>
        /// <param name="height">Height of the texture array</param>
        /// <param name="textureHashes">String representation of texture hashes</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryCreateAndBindTextureArray(Material material, List<Texture2D> texList, int width, int height, string textureHashes)
        {
            if (material == null || texList == null || texList.Count == 0) 
                return false;

            try
            {
                // Check if cache is available
                var newCacheKey = new TextureArrayCacheKey(texList.Count, width, height, textureHashes);
                var canReuseCache = m_CurrentCacheKey.HasValue && 
                                    m_CurrentCacheKey.Value.Equals(newCacheKey) && 
                                    m_LayerRtArray != null && 
                                    m_LayerRtArray.IsCreated();

                // If cache is available, use it directly
                if (canReuseCache)
                {
                    if (material.HasProperty(PreviewShaderContracts.Properties.LayerTextures)) 
                        material.SetTexture(PreviewShaderContracts.Properties.LayerTextures, m_LayerRtArray);
                    if (material.HasProperty(PreviewShaderContracts.Properties.UseLayerTexArray)) 
                        material.SetFloat(PreviewShaderContracts.Properties.UseLayerTexArray, 1f);
                    return true;
                }
                
                // Release old array
                ReleaseLayerTexArray();

                // Use RenderTexture Tex2DArray as a unified target, GPU Blit decode/scale
                var desc = new RenderTextureDescriptor(width, height)
                {
                    graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = texList.Count,
                    enableRandomWrite = false,
                    useMipMap = false,
                    msaaSamples = 1
                };
                m_LayerRtArray = new RenderTexture(desc)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat,
                    name = "Preview_Layers_RTArray"
                };
                if (!m_LayerRtArray.Create())
                {
                    throw new Exception("Failed to create RenderTexture Tex2DArray.");
                }

                // Reuse CommandBuffer to avoid frequent creation
                if (m_ReusableCommandBuffer == null)
                {
                    m_ReusableCommandBuffer = new CommandBuffer
                    {
                        name = "BuildLayerRTArray"
                    };
                }
                else
                {
                    m_ReusableCommandBuffer.Clear();
                }

                for (var slice = 0; slice < texList.Count; slice++)
                {
                    m_ReusableCommandBuffer.SetRenderTarget(m_LayerRtArray, 0, CubemapFace.Unknown, slice);
                    m_ReusableCommandBuffer.ClearRenderTarget(false, true, Color.white);
                    m_ReusableCommandBuffer.Blit(texList[slice], BuiltinRenderTextureType.CurrentActive);
                }
                Graphics.ExecuteCommandBuffer(m_ReusableCommandBuffer);

                // Update cache key
                m_CurrentCacheKey = newCacheKey;

                if (material.HasProperty(PreviewShaderContracts.Properties.LayerTextures)) 
                    material.SetTexture(PreviewShaderContracts.Properties.LayerTextures, m_LayerRtArray);
                if (material.HasProperty(PreviewShaderContracts.Properties.UseLayerTexArray)) 
                    material.SetFloat(PreviewShaderContracts.Properties.UseLayerTexArray, 1f);
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PreviewTextureArrayManager] Failed to build Texture2DArray. Reason: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Releases the layer texture array to avoid resource leaks.
        /// </summary>
        public void ReleaseLayerTexArray()
        {
            if (m_LayerRtArray != null)
            {
                m_LayerRtArray.Release();
                Object.DestroyImmediate(m_LayerRtArray);
                m_LayerRtArray = null;
            }
            
            // Clear cache key
            m_CurrentCacheKey = null;
        }
        
        /// <summary>
        /// Cleans up all resources, including CommandBuffer.
        /// </summary>
        public void Cleanup()
        {
            ReleaseLayerTexArray();
            if (m_ReusableCommandBuffer != null)
            {
                m_ReusableCommandBuffer.Release();
                m_ReusableCommandBuffer = null;
            }
        }
    }
}
