
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{

    /// <summary>
    /// Wraps a single instanced material used by preview mesh rendering and keeps it up-to-date with the current profile/template.
    /// Supports both PathPreviewSplat and StylizedRoadPreview shaders.
    /// </summary>
    public sealed class PreviewMaterialManager : IDisposable
    {
        private enum ShaderFlavor { Splat, Stylized, Unknown }

        private Material _instance;
        private ShaderFlavor _flavor = ShaderFlavor.Unknown;
        private int _lastHash = -1;
        private bool _dirty = true;

        // Cached combined mask LUT (RGBA channels for up to 4 layers)
        private Texture2D _maskLUT;

        private readonly List<Material> _cachedList = new(1);

        public Material Current => _instance;

        public List<Material> GetRenderMaterials()
        {
            if (_dirty)
            {
                _cachedList.Clear();
                if (_instance != null) _cachedList.Add(_instance);
                _dirty = false;
            }
            return _cachedList;
        }

        public void Update(PathProfile profile, Material template, float previewAlpha)
        {
            if (profile == null || template == null)
            {
                Clear();
                return;
            }

            var newHash = CalculateHash(profile, template, previewAlpha);
            if (newHash == _lastHash && _instance != null) return;
            _lastHash = newHash;

            if (_instance == null || _instance.shader != template.shader)
            {
                Clear();
                _instance = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
                _flavor = DetectFlavor(_instance.shader);
            }

            switch (_flavor)
            {
                case ShaderFlavor.Splat:
                    ApplySplat(profile, previewAlpha);
                    break;
                case ShaderFlavor.Stylized:
                    ApplyStylized(profile);
                    break;
            }

            _dirty = true;
        }

        #region Apply helpers

        private void ApplySplat(PathProfile profile, float alpha)
        {
            var recipe = profile.roadRecipe;
            for (var i = 0; i < 4; i++)
            {
                TerrainLayer layer = null;
                if (recipe?.blendLayers != null && i < recipe.blendLayers.Count)
                    layer = recipe.blendLayers[i]?.terrainLayer;
                SetLayer(i, layer, profile.roadWidth);
            }

            float master = profile.roadRecipe?.masterOpacity ?? 1f;
            _instance.SetFloat("_PreviewAlpha", Mathf.Clamp01(alpha * master));
            _instance.SetFloat("_EdgeFadeStart", 0.7f);
            _instance.SetFloat("_EdgeFadeEnd", 1f);

            // Calculate across-scale (1/tilingX) so shader can map uv.x back to 0..1 within road width
            float acrossScale = 1f;
            if (recipe?.blendLayers != null && recipe.blendLayers.Count > 0)
            {
                Vector2 firstTiling = LayerTilingUtility.CalcLayerTiling(profile.roadWidth, recipe.blendLayers[0]?.terrainLayer);
                if (!float.IsInfinity(firstTiling.x) && firstTiling.x > 1e-4f)
                    acrossScale = 1f / firstTiling.x;
            }
            _instance.SetFloat("_AcrossScale", acrossScale);

            // Generate and bind LUT so shader can sample accurate weights
            SetupMaskLUT(profile);
        }

        /// <summary>
        /// Generates or updates the 1-px height RGBA LUT that stores the per-layer mask weights
        /// (up to 4 layers). This mimics the CPU preview and job logic so that the GPU preview
        /// matches what will be painted onto terrain.
        /// </summary>
        private void SetupMaskLUT(PathProfile profile)
        {
            var recipe = profile.roadRecipe;
            if (recipe == null || recipe.blendLayers == null)
            {
                _instance.SetTexture("_MaskLUT", Texture2D.whiteTexture);
                return;
            }

            // Gather layer infos (maximum of 4 layers for preview shader)
            List<PreviewPipelineUtility.PreviewLayerInfo> layerInfos = new List<PreviewPipelineUtility.PreviewLayerInfo>(4);
            float worldWidth = Mathf.Max(0.1f, profile.roadWidth);

            for (int i = 0; i < recipe.blendLayers.Count && layerInfos.Count < 4; i++)
            {
                var blendLayer = recipe.blendLayers[i];
                if (blendLayer == null || !blendLayer.enabled) continue;

                TerrainLayer tLayer = blendLayer.terrainLayer;
                // 纹理必须存在才能被 GPU 正确采样
                Texture2D tex = tLayer?.diffuseTexture as Texture2D;
                if (tex == null) continue;

                Vector2 tiling = PreviewPipelineUtility.CalcLayerTiling(worldWidth, tLayer);

                var info = new PreviewPipelineUtility.PreviewLayerInfo(
                    tex,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(blendLayer.opacity * profile.roadRecipe.masterOpacity),
                    blendLayer.blendMode,
                    blendLayer.mask);

                layerInfos.Add(info);
            }

            // 如果没有有效层，直接使用白纹理以避免着色器异常
            if (layerInfos.Count == 0)
            {
                _instance.SetTexture("_MaskLUT", Texture2D.whiteTexture);
                return;
            }

            _maskLUT = PreviewPipelineUtility.BuildMaskLUT(_maskLUT, layerInfos, worldWidth);
            _instance.SetTexture("_MaskLUT", _maskLUT);
        }

        private void ApplyStylized(PathProfile profile)
        {
            var layer = profile.roadRecipe?.blendLayers?[0]?.terrainLayer;
            if (layer?.diffuseTexture != null)
            {
                _instance.SetTexture("_LayerTex", layer.diffuseTexture);
                var sz = layer.tileSize;
                if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
                if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
                Vector2 tiling = LayerTilingUtility.CalcLayerTiling(profile.roadWidth, layer);
                _instance.SetVector("_LayerTiling", new Vector4(tiling.x, tiling.y, 0, 0));
                _instance.SetColor("_LayerTint", layer.specular); // assuming specular used as tint currently
            }
            else
            {
                _instance.SetTexture("_LayerTex", Texture2D.whiteTexture);
                _instance.SetVector("_LayerTiling", Vector4.one);
            }

            float master = profile.roadRecipe?.masterOpacity ?? 1f;
            _instance.SetFloat("_LayerOpacity", master);
            _instance.SetFloat("_BlendMode", 0f);
        }

        private void SetLayer(int index, TerrainLayer layer, float worldWidth)
        {
            if (layer?.diffuseTexture != null)
            {
                _instance.SetTexture($"_Layer{index}_Texture", layer.diffuseTexture);
                Vector2 tiling = LayerTilingUtility.CalcLayerTiling(worldWidth, layer);
                _instance.SetVector($"_Layer{index}_Tiling", new Vector4(tiling.x, tiling.y, 0, 0));
                _instance.SetColor($"_Layer{index}_Color", Color.white); // 使用白色保持与地形贴图一致
            }
            else
            {
                _instance.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                _instance.SetVector($"_Layer{index}_Tiling", Vector4.one);
                _instance.SetColor($"_Layer{index}_Color", Color.white);
            }
        }

        #endregion

        #region Utilities

        private static ShaderFlavor DetectFlavor(Shader shader)
        {
            if (shader == null) return ShaderFlavor.Unknown;
            var name = shader.name;
            return name.Contains("StylizedRoadPreview") ? ShaderFlavor.Stylized :
                   name.Contains("PathPreviewSplat") ? ShaderFlavor.Splat : ShaderFlavor.Unknown;
        }

        private static int CalculateHash(PathProfile profile, Material template, float alpha)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (profile?.GetHashCode() ?? 0);
                hash = hash * 31 + (template?.GetHashCode() ?? 0);
                hash = hash * 31 + alpha.GetHashCode();
                hash = hash * 31 + (profile?.roadRecipe?.GetHashCode() ?? 0);
                return hash;
            }
        }
        public void Dispose() => Clear();
        private void Clear()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            if (_maskLUT != null)
            {
                UnityEngine.Object.DestroyImmediate(_maskLUT);
                _maskLUT = null;
            }
            _dirty = true;
        }



        #endregion
    }
}