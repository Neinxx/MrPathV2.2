
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
using UnityEngine;
#if UNITY_EDITOR
using EditorGpuPreviewCache = MrPathV2.Editor.Terrain.GpuPreviewCache;
#endif

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    /// Handles GPU preview binding for preview materials.
    /// This class is responsible for binding GPU-generated preview textures to materials.
    /// </summary>
    public class PreviewGpuBinder
    {
#if UNITY_EDITOR
        private int m_LastBoundSplatRtId = 0;
        private int m_LastBoundTerrainId = 0;
        private UnityEngine.Terrain m_TargetTerrain;

        /// <summary>
        /// Sets the target terrain for GPU preview binding.
        /// </summary>
        /// <param name="terrain">Target terrain</param>
        public void SetTargetTerrain(UnityEngine.Terrain terrain)
        {
            m_TargetTerrain = terrain;
        }

        /// <summary>
        /// Attempts to bind GPU preview textures to the material.
        /// </summary>
        /// <param name="material">Material to bind textures to</param>
        /// <param name="enableGpuPreview">Whether GPU preview is enabled</param>
        /// <param name="recipe">Road recipe</param>
        public void TryBindGpuPreview(Material material, bool enableGpuPreview, StylizedRoadRecipe recipe)
        {
            if (material == null) return;

            // Early return if GPU preview is not enabled or not available
            if (!ShouldUseGpuPreview(enableGpuPreview, recipe))
            {
                UnbindGpuPreview(material);
                return;
            }

            // Try to get cached render texture
            if (!EditorGpuPreviewCache.TryGet(m_TargetTerrain, out var rt) || !rt)
            {
                UnbindGpuPreview(material);
                return;
            }

            // Check if we need to bind (avoid repeated settings)
            if (IsAlreadyBound(rt))
            {
                return;
            }

            // Bind GPU-generated weight texture
            BindGpuPreviewTextures(material, rt);

            // Push terrain parameters for shader to sample world coordinates
            PushTerrainParameters(material);

            // Update binding cache
            UpdateBindingCache(rt);
        }

        /// <summary>
        /// Determines if GPU preview should be used
        /// </summary>
        /// <param name="enableGpuPreview">Whether GPU preview is globally enabled</param>
        /// <param name="recipe">Road recipe</param>
        /// <returns>True if GPU preview should be used</returns>
        private bool ShouldUseGpuPreview(bool enableGpuPreview, StylizedRoadRecipe recipe)
        {
            return enableGpuPreview && m_TargetTerrain && recipe;
        }

        /// <summary>
        /// Unbinds GPU preview textures and falls back to MaskAtlas
        /// </summary>
        /// <param name="material">Material to unbind textures from</param>
        private void UnbindGpuPreview(Material material)
        {
            if (material.HasProperty(PreviewShaderContracts.Properties.UseSplatWeights))
                material.SetInt(PreviewShaderContracts.Properties.UseSplatWeights, 0);
            if (material.HasProperty(PreviewShaderContracts.Properties.SplatWeights))
                material.SetTexture(PreviewShaderContracts.Properties.SplatWeights, null);
            m_LastBoundSplatRtId = 0;
            m_LastBoundTerrainId = 0;
        }

        /// <summary>
        /// Checks if the render texture is already bound
        /// </summary>
        /// <param name="rt">Render texture to check</param>
        /// <returns>True if already bound</returns>
        private bool IsAlreadyBound(RenderTexture rt)
        {
            var rtId = rt.GetInstanceID();
            var terrainId = m_TargetTerrain.GetInstanceID();
            return rtId == m_LastBoundSplatRtId && terrainId == m_LastBoundTerrainId;
        }

        /// <summary>
        /// Binds GPU preview textures to the material
        /// </summary>
        /// <param name="material">Material to bind textures to</param>
        /// <param name="rt">Render texture to bind</param>
        private void BindGpuPreviewTextures(Material material, RenderTexture rt)
        {
            if (material.HasProperty(PreviewShaderContracts.Properties.SplatWeights))
                material.SetTexture(PreviewShaderContracts.Properties.SplatWeights, rt);
            if (material.HasProperty(PreviewShaderContracts.Properties.UseSplatWeights))
                material.SetInt(PreviewShaderContracts.Properties.UseSplatWeights, 1);
        }

        /// <summary>
        /// Pushes terrain parameters for shader to sample world coordinates
        /// </summary>
        /// <param name="material">Material to push parameters to</param>
        private void PushTerrainParameters(Material material)
        {
            var td = m_TargetTerrain.terrainData;
            var pos = m_TargetTerrain.GetPosition();
            var size = td.size;

            if (material.HasProperty(PreviewShaderContracts.Properties.TerrainPosition))
                material.SetVector(PreviewShaderContracts.Properties.TerrainPosition, new Vector4(pos.x, pos.z, 0f, 0f));
            if (material.HasProperty(PreviewShaderContracts.Properties.TerrainSize))
                material.SetVector(PreviewShaderContracts.Properties.TerrainSize, new Vector4(size.x, size.z, 0f, 0f));
            if (material.HasProperty(PreviewShaderContracts.Properties.AlphamapResolution))
            {
                var res = td.alphamapResolution;
                material.SetVector(PreviewShaderContracts.Properties.AlphamapResolution, new Vector4(res, res, 0f, 0f));
            }
        }

        /// <summary>
        /// Updates the binding cache with the current render texture and terrain IDs
        /// </summary>
        /// <param name="rt">Current render texture</param>
        private void UpdateBindingCache(RenderTexture rt)
        {
            m_LastBoundSplatRtId = rt.GetInstanceID();
            m_LastBoundTerrainId = m_TargetTerrain.GetInstanceID();
        }
#endif
    }
}
