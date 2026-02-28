
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{

    /// <summary>
    /// Wraps a single instanced material used by preview mesh rendering and keeps it up-to-date with the current profile/template.
    /// Supports both PathPreviewSplat and StylizedRoadBlend shaders.
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
            int layerCount = recipe?.blendLayers?.Count ?? 0;
            
            // 检测是否使用多层着色器
            bool isMultiLayerShader = _instance.shader.name.Contains("PathPreviewSplatMulti");
            int maxLayers = isMultiLayerShader ? 16 : 4;
            
            // 设置所有层（最多16层），确保与StylizedRoadRecipe配方一致
            for (var i = 0; i < maxLayers; i++)
            {
                TerrainLayer layer = null;
                float layerOpacity = 0f;
                BlendMode blendMode = BlendMode.Normal;
                
                if (recipe?.blendLayers != null && i < recipe.blendLayers.Count)
                {
                    var blendLayer = recipe.blendLayers[i];
                    if (blendLayer != null && blendLayer.enabled)
                    {
                        layer = blendLayer.terrainLayer;
                        layerOpacity = blendLayer.opacity;
                        blendMode = blendLayer.blendMode;
                    }
                }
                
                SetLayer(i, layer, profile.roadWidth);
                
                // 设置每层的不透明度和混合模式
                _instance.SetFloat($"_Layer{i}_Opacity", layerOpacity);
                _instance.SetFloat($"_Layer{i}_BlendMode", (float)blendMode);
            }

            // 设置层数
            _instance.SetInt("_LayerCount", layerCount);

            float master = profile.roadRecipe?.masterOpacity ?? 1f;
            _instance.SetFloat("_PreviewAlpha", Mathf.Clamp01(alpha * master));
            _instance.SetFloat("_MasterOpacity", master);
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

            // 如果是多层着色器，设置控制纹理
            if (isMultiLayerShader)
            {
                SetupControlTextures(profile);
            }

            // Generate and bind LUT so shader can sample accurate weights
            SetupMaskLUT(profile);
        }

        /// <summary>
        /// Generates or updates the 1-px height RGBA LUT that stores the per-layer mask weights
        /// (up to 4 layers for legacy shader, unlimited for multi-layer shader). This mimics the CPU preview and job logic so that the GPU preview
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

            // 检测是否使用多层着色器
            bool isMultiLayerShader = _instance.shader.name.Contains("PathPreviewSplatMulti");
            int maxLayers = isMultiLayerShader ? recipe.blendLayers.Count : 4;

            // Gather layer infos
            List<PreviewPipelineUtility.PreviewLayerInfo> layerInfos = new List<PreviewPipelineUtility.PreviewLayerInfo>(maxLayers);
            float worldWidth = Mathf.Max(0.1f, profile.roadWidth);

            for (int i = 0; i < recipe.blendLayers.Count && layerInfos.Count < maxLayers; i++)
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
                    blendLayer.GetActiveMask());

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

        /// <summary>
        /// 为多层着色器设置控制纹理（模拟Unity地形的Control贴图）
        /// </summary>
        private void SetupControlTextures(PathProfile profile)
        {
            var recipe = profile.roadRecipe;
            if (recipe?.blendLayers == null)
            {
                // 设置默认控制纹理
                _instance.SetTexture("_Control0", Texture2D.redTexture);
                _instance.SetTexture("_Control1", Texture2D.blackTexture);
                _instance.SetTexture("_Control2", Texture2D.blackTexture);
                _instance.SetTexture("_Control3", Texture2D.blackTexture);
                return;
            }

            // 创建临时控制纹理来模拟地形权重
            // 这里简化处理，实际应该根据路径的UV坐标和混合遮罩生成权重
            var control0 = CreateControlTexture(recipe.blendLayers, 0, 4);
            var control1 = CreateControlTexture(recipe.blendLayers, 4, 8);
            var control2 = CreateControlTexture(recipe.blendLayers, 8, 12);
            var control3 = CreateControlTexture(recipe.blendLayers, 12, 16);

            _instance.SetTexture("_Control0", control0 ?? Texture2D.redTexture);
            _instance.SetTexture("_Control1", control1 ?? Texture2D.blackTexture);
            _instance.SetTexture("_Control2", control2 ?? Texture2D.blackTexture);
            _instance.SetTexture("_Control3", control3 ?? Texture2D.blackTexture);
        }

        /// <summary>
        /// 创建控制纹理，每个RGBA通道对应一层
        /// </summary>
        private Texture2D CreateControlTexture(System.Collections.Generic.List<BlendLayer> layers, int startIndex, int endIndex)
        {
            bool hasAnyLayer = false;
            for (int i = startIndex; i < endIndex && i < layers.Count; i++)
            {
                if (layers[i] != null && layers[i].enabled && layers[i].terrainLayer?.diffuseTexture != null)
                {
                    hasAnyLayer = true;
                    break;
                }
            }

            if (!hasAnyLayer) return null;

            // 创建简单的1x1控制纹理
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            Color color = Color.black;

            // 设置每个通道的权重（简化处理，实际应该基于遮罩计算）
            for (int i = startIndex; i < endIndex && i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer != null && layer.enabled && layer.terrainLayer?.diffuseTexture != null)
                {
                    float weight = layer.opacity;
                    int channel = i - startIndex;
                    switch (channel)
                    {
                        case 0: color.r = weight; break;
                        case 1: color.g = weight; break;
                        case 2: color.b = weight; break;
                        case 3: color.a = weight; break;
                    }
                }
            }

            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
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
            return name.Contains("StylizedRoadBlend") ? ShaderFlavor.Stylized :
                   (name.Contains("PathPreviewSplat") || name.Contains("PathPreviewSplatMulti")) ? ShaderFlavor.Splat : ShaderFlavor.Unknown;
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