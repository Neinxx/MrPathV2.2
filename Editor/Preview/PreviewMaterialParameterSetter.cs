using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
using UnityEngine;
using BlendMode = MrPathV2.Runtime.Core.BlendMode;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    ///     Handles setting material parameters for preview materials.
    ///     This class is responsible for configuring material properties based on path profiles.
    /// </summary>
    public class PreviewMaterialParameterSetter
    {
        private readonly Material _mMaterial;
        private readonly PathProfile _mProfile;

        public PreviewMaterialParameterSetter(Material material, PathProfile profile)
        {
            _mMaterial = material ?? throw new ArgumentNullException(nameof(material));
            _mProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>
        ///     Sets common preview parameters on the material.
        /// </summary>
        /// <param name="layerCount">Number of layers to render</param>
        /// <param name="alpha">Preview alpha value</param>
        public void SetCommonPreviewParameters(int layerCount, float alpha)
        {
            if (_mMaterial == null) return;

            var recipe = _mProfile.roadRecipe;
            var master = recipe?.masterOpacity ?? 1f;

            // Layer count and opacity logic
            _mMaterial.SetInt(PreviewShaderContracts.Properties.LayerCount, Mathf.Max(1, layerCount));
            var isOpaque = _mProfile.opaquePreview;
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.PreviewAlpha, isOpaque ? 1f : Mathf.Clamp01(alpha));
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.OpaquePreview, isOpaque ? 1f : 0f);

            // Mask strength and threshold (linked with opaque preview)
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.MaskStrength, master);
            var maskThreshold = isOpaque ? MrPathV2.Runtime.Core.Constants.MaskConstants.DefaultAlphaClipThreshold : 0.0f;
            if (_mMaterial.HasProperty(PreviewShaderContracts.Properties.MaskThreshold))
                _mMaterial.SetFloat(PreviewShaderContracts.Properties.MaskThreshold, maskThreshold);

            // Depth test, path sample count and other common properties
            if (_mMaterial.HasProperty(PreviewShaderContracts.Properties.ZTest))
                _mMaterial.SetInt(PreviewShaderContracts.Properties.ZTest, _mProfile.enableDepthTest ? 4 : 8);

            // 注意：不在此处重置 AcrossScale/MeshRepeat，避免覆盖外部提供的正确重复系数
        }

        /// <summary>
        ///     Sets layer parameters as arrays for multi-layer shaders.
        /// </summary>
        /// <param name="maxLayers">Maximum number of layers supported</param>
        /// <param name="layers">List of road layers</param>
        /// <returns>Number of layers processed</returns>
        public int SetLayerParametersAsArrays(int maxLayers, IReadOnlyList<RoadLayer> layers)
        {
            if (_mMaterial == null) return 0;

            var layerCount = GetEffectiveLayerCount(layers);
            var tilingsArr = new Vector4[maxLayers];
            var opacitiesArr = new float[maxLayers];
            var blendModesArr = new float[maxLayers];

            // Process each layer
            for (var i = 0; i < Mathf.Min(maxLayers, layerCount); i++)
            {
                ProcessLayerAtIndex(i, layers, tilingsArr, opacitiesArr, blendModesArr);
            }

            // Apply the arrays to the material
            ApplyLayerArraysToMaterial(tilingsArr, opacitiesArr, blendModesArr);

            return layerCount;
        }

        /// <summary>
        ///     Gets the effective layer count, ensuring at least 1 layer
        /// </summary>
        /// <param name="layers">List of road layers</param>
        /// <returns>Effective layer count</returns>
        private static int GetEffectiveLayerCount(IReadOnlyList<RoadLayer> layers)
        {
            var layerCount = layers?.Count ?? 0;
            return layerCount == 0 ? 1 : layerCount; // At least one layer
        }

        /// <summary>
        ///     Processes a single layer at the specified index
        /// </summary>
        /// <param name="index">Layer index</param>
        /// <param name="layers">List of road layers</param>
        /// <param name="tilingsArr">Tilings array to populate</param>
        /// <param name="opacitiesArr">Opacities array to populate</param>
        /// <param name="blendModesArr">Blend modes array to populate</param>
        private void ProcessLayerAtIndex(int index, IReadOnlyList<RoadLayer> layers, Vector4[] tilingsArr, float[] opacitiesArr, float[] blendModesArr)
        {
            var (tl, layerOpacity, blendMode) = GetLayerDataAtIndex(index, layers);

            // Tiling
            var tiling = tl && tl.diffuseTexture ? PreviewPipelineUtility.CalcLayerTiling(_mProfile.roadWidth, tl) : Vector2.one;
            var offset = tl ? tl.tileOffset : Vector2.zero;
            // 将偏移写入 zw，便于 shader 使用 float4(tiling.xy, offset.xy)
            tilingsArr[index] = new Vector4(tiling.x, tiling.y, offset.x, offset.y);

            // Opacity/Blend
            var master = _mProfile.roadRecipe?.masterOpacity ?? 1f;
            opacitiesArr[index] = layers == null || layers.Count == 0 ? 1f : Mathf.Clamp01(layerOpacity * master);
            blendModesArr[index] = (float)blendMode;

            // Push per-layer color (TerrainLayer.specular as tint)
            _mMaterial.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(tl));
        }

        /// <summary>
        ///     Gets layer data at the specified index
        /// </summary>
        /// <param name="index">Layer index</param>
        /// <param name="layers">List of road layers</param>
        /// <returns>Tuple containing the terrain layer, layer opacity, and blend mode</returns>
        private (TerrainLayer tl, float layerOpacity, BlendMode blendMode) GetLayerDataAtIndex(int index, IReadOnlyList<RoadLayer> layers)
        {
            TerrainLayer tl = null;
            var layerOpacity = 0f;
            var blendMode = BlendMode.Normal;

            if (layers != null && index < layers.Count)
            {
                var rl = layers[index];
                if (rl != null && rl.enabled)
                {
                    tl = rl.contentLayer;
                    layerOpacity = rl.opacity;
                    blendMode = rl.blendMode;
                }
            }

            return (tl, layerOpacity, blendMode);
        }

        /// <summary>
        ///     Applies the layer arrays to the material
        /// </summary>
        /// <param name="tilingsArr">Tilings array</param>
        /// <param name="opacitiesArr">Opacities array</param>
        /// <param name="blendModesArr">Blend modes array</param>
        private void ApplyLayerArraysToMaterial(Vector4[] tilingsArr, float[] opacitiesArr, float[] blendModesArr)
        {
            // Push array properties (for shader sampling)
            _mMaterial.SetVectorArray(PreviewShaderContracts.Properties.LayerTilingsArr, tilingsArr);
            _mMaterial.SetFloatArray(PreviewShaderContracts.Properties.LayerOpacitiesArr, opacitiesArr);
            _mMaterial.SetFloatArray(PreviewShaderContracts.Properties.LayerBlendModesArr, blendModesArr);
        }

        /// <summary>
        ///     Sets parameters for a single layer.
        /// </summary>
        /// <param name="index">Layer index</param>
        /// <param name="layer">Terrain layer</param>
        public void SetLayerParameters(int index, TerrainLayer layer)
        {
            if (_mMaterial == null) return;

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
                    _mMaterial.SetTexture($"_Layer{index}_Texture", tex);
                    _mMaterial.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
                    // 兼容使用 _Layer{index}_Texture 的 shader：同步设置 _ST（缩放/偏移）
                    var tiling = LayerTilingUtility.CalcLayerTiling(_mProfile.roadWidth, layer);
                    var offset = layer ? layer.tileOffset : Vector2.zero;
                    _mMaterial.SetTextureScale($"_Layer{index}_Texture", tiling);
                    _mMaterial.SetTextureOffset($"_Layer{index}_Texture", offset);
                }
                else
                {
                    _mMaterial.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                    _mMaterial.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
                    _mMaterial.SetTextureScale($"_Layer{index}_Texture", Vector2.one);
                    _mMaterial.SetTextureOffset($"_Layer{index}_Texture", Vector2.zero);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialParameterSetter] Failed to read TerrainLayer at index {index}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Fallback to safe defaults so preview continues rendering
                _mMaterial.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                _mMaterial.SetColor($"_Layer{index}_Color", Color.white);
                _mMaterial.SetTextureScale($"_Layer{index}_Texture", Vector2.one);
                _mMaterial.SetTextureOffset($"_Layer{index}_Texture", Vector2.zero);
            }
        }

        /// <summary>
        ///     Sets parameters specific to stylized shaders.
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        public void SetStylizedParameters(TerrainLayer layer)
        {
            if (_mMaterial == null) return;

            // Set texture and tiling parameters
            SetTextureAndTilingParameters(layer);

            // Set opacity and blend parameters
            SetOpacityAndBlendParameters();

            // Set common parameters
            SetCommonStylizedParameters();

            // Set preview-specific parameters
            SetPreviewSpecificParameters();
        }

        /// <summary>
        ///     Sets texture and tiling parameters for stylized preview
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        private void SetTextureAndTilingParameters(TerrainLayer layer)
        {
            var tex = TryGetTextureFromLayer(layer);

            if (tex != null)
            {
                _mMaterial.SetTexture(PreviewShaderContracts.Properties.LayerTex, tex);
                var tiling = CalculateLayerTiling(layer);
                var offset = layer ? layer.tileOffset : Vector2.zero;
                _mMaterial.SetVector(PreviewShaderContracts.Properties.LayerTiling, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
                // 同步设置 LayerTex 的 _ST 方便 shader 使用标准 TRANSFORM_TEX
                _mMaterial.SetTextureScale(PreviewShaderContracts.Properties.LayerTex, tiling);
                _mMaterial.SetTextureOffset(PreviewShaderContracts.Properties.LayerTex, offset);
                _mMaterial.SetColor(PreviewShaderContracts.Properties.LayerTint, GetTerrainLayerTint(layer));
            }
            else
            {
                _mMaterial.SetTexture(PreviewShaderContracts.Properties.LayerTex, Texture2D.whiteTexture);
                _mMaterial.SetVector(PreviewShaderContracts.Properties.LayerTiling, Vector4.one);
                _mMaterial.SetTextureScale(PreviewShaderContracts.Properties.LayerTex, Vector2.one);
                _mMaterial.SetTextureOffset(PreviewShaderContracts.Properties.LayerTex, Vector2.zero);
                _mMaterial.SetColor(PreviewShaderContracts.Properties.LayerTint, GetTerrainLayerTint(layer));
            }
        }

        /// <summary>
        ///     Safely tries to get a texture from a terrain layer
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        /// <returns>Texture if available, null otherwise</returns>
        private static Texture2D TryGetTextureFromLayer(TerrainLayer layer)
        {
            try
            {
                if (layer && layer.diffuseTexture != null)
                {
                    return layer.diffuseTexture;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialParameterSetter] Failed to access TerrainLayer.diffuseTexture for stylized preview: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
            return null;
        }

        /// <summary>
        ///     Calculates layer tiling values
        /// </summary>
        /// <param name="layer">Terrain layer</param>
        /// <returns>Tiling vector</returns>
        private Vector2 CalculateLayerTiling(TerrainLayer layer)
        {
            var sz = layer.tileSize;
            if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
            if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
            return LayerTilingUtility.CalcLayerTiling(_mProfile.roadWidth, layer);
        }

        /// <summary>
        ///     Sets opacity and blend parameters for stylized preview
        /// </summary>
        private void SetOpacityAndBlendParameters()
        {
            var master = _mProfile.roadRecipe?.masterOpacity ?? 1f;
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.LayerOpacity, master);
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.MaskStrength, master);
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.BlendMode, 0f);
        }

        /// <summary>
        ///     Sets common parameters for stylized preview
        /// </summary>
        private void SetCommonStylizedParameters()
        {
            _mMaterial.SetFloat(PreviewShaderContracts.Properties.LayerIndex, 0f);

            if (_mMaterial.HasProperty(PreviewShaderContracts.Properties.ZTest))
                _mMaterial.SetInt(PreviewShaderContracts.Properties.ZTest, _mProfile.enableDepthTest ? 4 : 8);
        }

        /// <summary>
        ///     Sets preview-specific parameters for stylized preview
        /// </summary>
        private void SetPreviewSpecificParameters()
        {
            // Stylized preview also supports opaque preview
            if (_mMaterial.HasProperty(PreviewShaderContracts.Properties.OpaquePreview))
                _mMaterial.SetFloat(PreviewShaderContracts.Properties.OpaquePreview, _mProfile.opaquePreview ? 1f : 0f);

            // Set mask threshold for single-layer stylized preview to ensure clear edges
            var maskThresholdStylized = _mProfile.opaquePreview ? MrPathV2.Runtime.Core.Constants.MaskConstants.DefaultAlphaClipThreshold : 0.0f;
            if (_mMaterial.HasProperty(PreviewShaderContracts.Properties.MaskThreshold))
                _mMaterial.SetFloat(PreviewShaderContracts.Properties.MaskThreshold, maskThresholdStylized);

            // 注意：不在此处重置 AcrossScale/MeshRepeat，避免覆盖外部提供的正确重复系数
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
