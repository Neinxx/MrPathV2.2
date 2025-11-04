using System;
using System.Collections.Generic;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Preview;
using UnityEngine;
using BlendMode = __temp.MrPathV2.Runtime.Core.BlendMode;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    /// Handles setting material parameters for preview materials.
    /// This class is responsible for configuring material properties based on path profiles.
    /// </summary>
    public class PreviewMaterialParameterSetter
    {
        private readonly Material m_Material;
        private readonly PathProfile m_Profile;
        
        public PreviewMaterialParameterSetter(Material material, PathProfile profile)
        {
            m_Material = material ?? throw new ArgumentNullException(nameof(material));
            m_Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }
        
        /// <summary>
        /// Sets common preview parameters on the material.
        /// </summary>
        /// <param name="layerCount">Number of layers to render</param>
        /// <param name="alpha">Preview alpha value</param>
        public void SetCommonPreviewParameters(int layerCount, float alpha)
        {
            if (m_Material == null) return;
            
            var recipe = m_Profile.roadRecipe;
            var master = recipe?.masterOpacity ?? 1f;

            // Layer count and opacity logic
            m_Material.SetInt(PreviewShaderContracts.Properties.LayerCount, Mathf.Max(1, layerCount));
            var isOpaque = m_Profile.opaquePreview;
            m_Material.SetFloat(PreviewShaderContracts.Properties.PreviewAlpha, isOpaque ? 1f : Mathf.Clamp01(alpha));
            m_Material.SetFloat(PreviewShaderContracts.Properties.OpaquePreview, isOpaque ? 1f : 0f);

            // Mask strength and threshold (linked with opaque preview)
            m_Material.SetFloat(PreviewShaderContracts.Properties.MaskStrength, master);
            var maskThreshold = isOpaque ? 0.2f : 0.0f;
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MaskThreshold)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MaskThreshold, maskThreshold);

            // Depth test, path sample count and other common properties
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.ZTest)) 
                m_Material.SetInt(PreviewShaderContracts.Properties.ZTest, m_Profile.enableDepthTest ? 4 : 8);
            m_Material.SetFloat(PreviewShaderContracts.Properties.PathSamples, 64f);

            // Uniform AcrossScale and MeshRepeat default mapping
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.AcrossScale)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.AcrossScale, 1f);
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAcross)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAcross, 1f);
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAlong)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAlong, 1f);
        }
        
        /// <summary>
        /// Sets layer parameters as arrays for multi-layer shaders.
        /// </summary>
        /// <param name="maxLayers">Maximum number of layers supported</param>
        /// <param name="layers">List of road layers</param>
        /// <returns>Number of layers processed</returns>
        public int SetLayerParametersAsArrays(int maxLayers, IReadOnlyList<RoadLayer> layers)
        {
            if (m_Material == null) return 0;
            
            var layerCount = layers?.Count ?? 0;
            if (layerCount == 0) layerCount = 1; // At least one layer

            var tilingsArr = new Vector4[maxLayers];
            var opacitiesArr = new float[maxLayers];
            var blendModesArr = new float[maxLayers];

            for (var i = 0; i < Mathf.Min(maxLayers, layerCount); i++)
            {
                TerrainLayer tl = null;
                var layerOpacity = 0f;
                var blendMode = BlendMode.Normal;

                if (layers != null && i < layers.Count)
                {
                    var rl = layers[i];
                    if (rl != null && rl.enabled)
                    {
                        tl = rl.contentLayer;
                        layerOpacity = rl.opacity;
                        blendMode = rl.blendMode;
                    }
                }

                // Tiling
                var t = (tl && tl.diffuseTexture) ? PreviewPipelineUtility.CalcLayerTiling(m_Profile.roadWidth, tl) : Vector2.one;
                tilingsArr[i] = new Vector4(t.x, t.y, 0, 0);

                // Opacity/Blend
                var master = m_Profile.roadRecipe?.masterOpacity ?? 1f;
                opacitiesArr[i] = layers == null || layers.Count == 0 ? 1f : Mathf.Clamp01(layerOpacity * master);
                blendModesArr[i] = (float)blendMode;

                // Push per-layer color (TerrainLayer.specular as tint)
                m_Material.SetColor($"_Layer{i}_Color", GetTerrainLayerTint(tl));
            }

            // Push array properties (for shader sampling)
            m_Material.SetVectorArray(PreviewShaderContracts.Properties.LayerTilingsArr, tilingsArr);
            m_Material.SetFloatArray(PreviewShaderContracts.Properties.LayerOpacitiesArr, opacitiesArr);
            m_Material.SetFloatArray(PreviewShaderContracts.Properties.LayerBlendModesArr, blendModesArr);
            
            return layerCount;
        }
        
        /// <summary>
        /// Sets parameters for a single layer.
        /// </summary>
        /// <param name="index">Layer index</param>
        /// <param name="layer">Terrain layer</param>
        public void SetLayerParameters(int index, TerrainLayer layer)
        {
            if (m_Material == null) return;

            try
            {
                Texture2D tex = null;
                // Safely access TerrainLayer.diffuseTexture; Unity may throw if the asset is invalid/destroyed
                if (layer != null)
                {
                    // Guard against Unity's fake-null: use implicit bool check first
                    if (layer && layer.diffuseTexture != null)
                    {
                        tex = layer.diffuseTexture;
                    }
                }

                if (tex != null)
                {
                    m_Material.SetTexture($"_Layer{index}_Texture", tex);
                    m_Material.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
                }
                else
                {
                    m_Material.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                    m_Material.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialParameterSetter] Failed to read TerrainLayer at index {index}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Fallback to safe defaults so preview continues rendering
                m_Material.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                m_Material.SetColor($"_Layer{index}_Color", Color.white);
            }
        }
        
        /// <summary>
        /// Sets parameters specific to stylized shaders.
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        public void SetStylizedParameters(TerrainLayer layer)
        {
            if (m_Material == null) return;

            Texture2D tex = null;
            try
            {
                if (layer && layer.diffuseTexture != null)
                {
                    tex = layer.diffuseTexture;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialParameterSetter] Failed to access TerrainLayer.diffuseTexture for stylized preview: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }

            if (tex != null)
            {
                m_Material.SetTexture(PreviewShaderContracts.Properties.LayerTex, tex);
                var sz = layer.tileSize;
                if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
                if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
                var tiling = LayerTilingUtility.CalcLayerTiling(m_Profile.roadWidth, layer);
                m_Material.SetVector(PreviewShaderContracts.Properties.LayerTiling, new Vector4(tiling.x, tiling.y, 0, 0));
                m_Material.SetColor(PreviewShaderContracts.Properties.LayerTint, GetTerrainLayerTint(layer));
            }
            else
            {
                m_Material.SetTexture(PreviewShaderContracts.Properties.LayerTex, Texture2D.whiteTexture);
                m_Material.SetVector(PreviewShaderContracts.Properties.LayerTiling, Vector4.one);
                m_Material.SetColor(PreviewShaderContracts.Properties.LayerTint, GetTerrainLayerTint(layer));
            }

            var master = m_Profile.roadRecipe?.masterOpacity ?? 1f;
            m_Material.SetFloat(PreviewShaderContracts.Properties.LayerOpacity, master);
            m_Material.SetFloat(PreviewShaderContracts.Properties.MaskStrength, master);
            m_Material.SetFloat(PreviewShaderContracts.Properties.BlendMode, 0f);
            m_Material.SetFloat(PreviewShaderContracts.Properties.PathSamples, 64f);
            m_Material.SetFloat(PreviewShaderContracts.Properties.LayerIndex, 0f);
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.ZTest)) 
                m_Material.SetInt(PreviewShaderContracts.Properties.ZTest, m_Profile.enableDepthTest ? 4 : 8);
            
            // Stylized preview also supports opaque preview
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.OpaquePreview)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.OpaquePreview, m_Profile.opaquePreview ? 1f : 0f);
            
            // Set mask threshold for single-layer stylized preview to ensure clear edges
            var maskThresholdStylized = m_Profile.opaquePreview ? 0.2f : 0.0f;
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MaskThreshold)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MaskThreshold, maskThresholdStylized);

            // Uniform AcrossScale and MeshRepeat default mapping
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.AcrossScale)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.AcrossScale, 1f);
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAcross)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAcross, 1f);
            if (m_Material.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAlong)) 
                m_Material.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAlong, 1f);
        }
        
        /// <summary>
        /// Parse Tint color from TerrainLayer (URP's DiffuseRemapMax)
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        /// <returns>Tint color</returns>
        private Color GetTerrainLayerTint(TerrainLayer layer)
        {
            try
            {
                if (!layer) return Color.white;
#if UNITY_2019_1_OR_NEWER
                var max = layer.diffuseRemapMax; // Vector4
                return new Color(max.x, max.y, max.z, 1f);
#else
                return Color.white;
#endif
            }
            catch
            {
                return Color.white;
            }
        }
    }
}