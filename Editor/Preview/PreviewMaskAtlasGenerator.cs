using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
using UnityEngine;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    ///     Handles generation and management of mask atlas textures for preview materials.
    ///     This class is responsible for creating textures that store per-layer mask weights.
    /// </summary>
    public class PreviewMaskAtlasGenerator
    {
        private readonly Material m_Material;
        private readonly float m_PathLength;
        private readonly PathProfile m_Profile;

        public PreviewMaskAtlasGenerator(Material material, PathProfile profile, float pathLength)
        {
            m_Material = material ?? throw new ArgumentNullException(nameof(material));
            m_Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            m_PathLength = pathLength;
        }

        /// <summary>
        ///     Generates or updates the mask atlas texture that stores per-layer mask weights.
        ///     This replaces the legacy 1D RGBA LUT system and supports an arbitrary number of layers.
        /// </summary>
        /// <param name="existingAtlas">Existing atlas texture to update, if any</param>
        /// <returns>Generated or updated mask atlas texture</returns>
        public Texture2D GenerateMaskAtlas(Texture2D existingAtlas)
        {
            if (m_Material == null) return null;

            var recipe = m_Profile.roadRecipe;
            var layers = recipe?.GetLayers();

            // Early return if no layers
            if (layers == null || layers.Count == 0)
            {
                HandleNoLayersCase();
                return null;
            }

            // Collect layer information
            var layerInfos = CollectLayerInfos(layers);

            // If no valid layers, handle appropriately
            if (layerInfos.Count == 0)
            {
                HandleNoLayersCase();
                return null;
            }

            // Build the mask atlas
            var maskAtlas = BuildAtlasFromLayerInfos(existingAtlas, layerInfos);

            // Apply the mask atlas to the material
            ApplyMaskAtlasToMaterial(maskAtlas);

            return maskAtlas;
        }

        /// <summary>
        ///     Collects information about all enabled layers
        /// </summary>
        /// <param name="layers">List of road layers</param>
        /// <returns>List of preview layer information</returns>
        private List<PreviewPipelineUtility.PreviewLayerInfo> CollectLayerInfos(IReadOnlyList<RoadLayer> layers)
        {
            var layerInfos = new List<PreviewPipelineUtility.PreviewLayerInfo>(layers.Count);
            var worldWidth = Mathf.Max(0.1f, m_Profile.roadWidth);
            var recipe = m_Profile.roadRecipe;

            foreach (var roadLayer in layers)
            {
                if (roadLayer is not { enabled: true }) continue;

                var layerInfo = CreateLayerInfo(roadLayer, worldWidth, recipe);
                if (layerInfo != null)
                {
                    layerInfos.Add(layerInfo.Value);
                }
            }

            return layerInfos;
        }

        /// <summary>
        ///     Creates preview layer information for a single road layer
        /// </summary>
        /// <param name="roadLayer">The road layer</param>
        /// <param name="worldWidth">World width of the road</param>
        /// <param name="recipe">The road recipe</param>
        /// <returns>Preview layer information, or null if the layer is invalid</returns>
        private PreviewPipelineUtility.PreviewLayerInfo? CreateLayerInfo(RoadLayer roadLayer, float worldWidth, StylizedRoadRecipe recipe)
        {
            var tLayer = roadLayer.contentLayer;
            var tex = TryGetTextureFromLayer(tLayer);

            // Skip layers without textures
            if (tex == null) return null;

            var tiling = PreviewPipelineUtility.CalcLayerTiling(worldWidth, tLayer);
            var tint = GetTerrainLayerTint(tLayer);
            var opacity = Mathf.Clamp01(roadLayer.opacity * recipe.masterOpacity);

            return new PreviewPipelineUtility.PreviewLayerInfo(
                tex,
                tiling,
                Vector2.zero,
                tint,
                opacity,
                roadLayer.blendMode,
                roadLayer.layerMask);
        }

        /// <summary>
        ///     Safely tries to get a texture from a terrain layer
        /// </summary>
        /// <param name="tLayer">The terrain layer</param>
        /// <returns>The texture, or null if not available</returns>
        private static Texture2D TryGetTextureFromLayer(TerrainLayer tLayer)
        {
            try
            {
                if (tLayer && tLayer.diffuseTexture != null)
                {
                    return tLayer.diffuseTexture;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaskAtlasGenerator] Failed to access TerrainLayer.diffuseTexture while building mask atlas: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
            return null;
        }

        /// <summary>
        ///     Builds the mask atlas from layer information
        /// </summary>
        /// <param name="existingAtlas">Existing atlas texture to update, if any</param>
        /// <param name="layerInfos">List of layer information</param>
        /// <returns>The generated or updated mask atlas</returns>
        private Texture2D BuildAtlasFromLayerInfos(Texture2D existingAtlas, List<PreviewPipelineUtility.PreviewLayerInfo> layerInfos)
        {
            var worldWidth = Mathf.Max(0.1f, m_Profile.roadWidth);
            // Use the real path length pushed externally; if unknown, use conservative default
            var effectivePathLength = m_PathLength > 0f ? m_PathLength : 100f;

            // 从材质读取阈值，驱动 Atlas 构建期的阈值塑形
            var maskThreshold = 0f;
            if (m_Material != null && m_Material.HasProperty(PreviewShaderContracts.Properties.MaskThreshold))
            {
                maskThreshold = m_Material.GetFloat(PreviewShaderContracts.Properties.MaskThreshold);
            }

            return PreviewPipelineUtility.BuildMaskAtlas(existingAtlas, layerInfos, worldWidth, effectivePathLength, 256, maskThreshold);
        }

        /// <summary>
        ///     Applies the mask atlas to the material
        /// </summary>
        /// <param name="maskAtlas">The mask atlas texture</param>
        private void ApplyMaskAtlasToMaterial(Texture2D maskAtlas)
        {
            if (!m_Material.HasProperty(PreviewShaderContracts.Properties.MaskAtlas)) return;

            // WYSIWYG: 当未生成Atlas或无层时，使用白色纹理以代表“全通”（权重=1），避免被黑色遮罩清零
            var safeAtlas = maskAtlas ? maskAtlas : Texture2D.whiteTexture;
            m_Material.SetTexture(PreviewShaderContracts.Properties.MaskAtlas, safeAtlas);
            m_Material.SetFloat(PreviewShaderContracts.Properties.AtlasInvHeight, safeAtlas && safeAtlas.height > 0 ? 1f / safeAtlas.height : 1f);
            m_Material.SetFloat(PreviewShaderContracts.Properties.PathSamples, 64f);
            // Stylized shader expects layer index uniform (always 0 for single-layer preview)
            m_Material.SetFloat(PreviewShaderContracts.Properties.LayerIndex, 0f);
        }

        private void HandleNoLayersCase()
        {
            if (!m_Material.HasProperty(PreviewShaderContracts.Properties.MaskAtlas)) return;
            // 无层时，白色1x1纹理代表“完全可见”的遮罩
            m_Material.SetTexture(PreviewShaderContracts.Properties.MaskAtlas, Texture2D.whiteTexture);
            m_Material.SetFloat(PreviewShaderContracts.Properties.AtlasInvHeight, 1f);
        }

        /// <summary>
        ///     Parse Tint color from TerrainLayer (URP's DiffuseRemapMax)
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        /// <returns>Tint color</returns>
        private static Color GetTerrainLayerTint(TerrainLayer layer)
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
