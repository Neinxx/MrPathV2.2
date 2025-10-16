
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
                SetLayer(i, layer);
            }

            _instance.SetFloat("_PreviewAlpha", Mathf.Clamp01(alpha));
            _instance.SetFloat("_EdgeFadeStart", 0.7f);
            _instance.SetFloat("_EdgeFadeEnd", 1f);
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
                _instance.SetVector("_LayerTiling", new Vector4(sz.x, sz.y, 0, 0));
            }
            else
            {
                _instance.SetTexture("_LayerTex", Texture2D.whiteTexture);
                _instance.SetVector("_LayerTiling", Vector4.one);
            }

            _instance.SetFloat("_LayerOpacity", 1f);
            _instance.SetFloat("_BlendMode", 0f);
        }

        private void SetLayer(int index, TerrainLayer layer)
        {
            if (layer?.diffuseTexture != null)
            {
                _instance.SetTexture($"_Layer{index}_Texture", layer.diffuseTexture);
                var sz = layer.tileSize;
                if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
                if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
                _instance.SetVector($"_Layer{index}_Tiling", new Vector4(sz.x, sz.y, 0, 0));
                _instance.SetColor($"_Layer{index}_Color", layer.specular);
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

        private void Clear()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            _dirty = true;
        }

        public void Dispose() => Clear();

        #endregion
    }
}