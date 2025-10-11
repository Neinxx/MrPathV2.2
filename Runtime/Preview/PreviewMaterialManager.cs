// PreviewMaterialManager.cs (守护者版)
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{

    public class PreviewMaterialManager : System.IDisposable
    {
        private Material _instancedMaterial;
        public Material CurrentMaterial => _instancedMaterial;

        public List<Material> GetFrameRenderMaterials()
        {
            var list = new List<Material>(1);
            if (_instancedMaterial != null)
            {
                list.Add(_instancedMaterial);
            }
            return list;
        }

        public void UpdateMaterials(PathProfile profile, Material templateSplatShaderMaterial, float previewAlpha = 0.5f)
        {
            if (profile == null  || templateSplatShaderMaterial == null)
            {
                ClearMaterial();
                return;
            }

            if (_instancedMaterial == null || _instancedMaterial.shader != templateSplatShaderMaterial.shader)
            {
                ClearMaterial();
                _instancedMaterial = new Material(templateSplatShaderMaterial)
                {
                    name = "PathSplatPreview_Instanced",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            // 从 Profile 的 roadRecipe 填充最多4层纹理与Tiling
            var recipe = profile.roadRecipe;
            for (int i = 0; i < 4; i++)
            {
                TerrainLayer terrainLayer = null;
                if (recipe != null && recipe.blendLayers != null && i < recipe.blendLayers.Count)
                {
                    terrainLayer = recipe.blendLayers[i]?.terrainLayer;
                }
                if (terrainLayer != null)
                {
                    var tex = terrainLayer.diffuseTexture;
                    if (tex == null)
                    {
                        // 当地形层未配置漫反射贴图时，用白贴图回退，避免材质空纹理导致发灰/闪烁
                        _instancedMaterial.SetTexture($"_Layer{i}_Texture", Texture2D.whiteTexture);
                        _instancedMaterial.SetVector($"_Layer{i}_Tiling", new Vector4(1, 1, 0, 0));
                    }
                    else
                    {
                        _instancedMaterial.SetTexture($"_Layer{i}_Texture", tex);
                        Vector2 tileSize = terrainLayer.tileSize;
                        if (Mathf.Approximately(tileSize.x, 0f)) tileSize.x = 1f;
                        if (Mathf.Approximately(tileSize.y, 0f)) tileSize.y = 1f;
                        _instancedMaterial.SetVector($"_Layer{i}_Tiling", new Vector4(tileSize.x, tileSize.y, 0, 0));
                    }
                }
                else
                {
                    _instancedMaterial.SetTexture($"_Layer{i}_Texture", Texture2D.whiteTexture);
                    _instancedMaterial.SetVector($"_Layer{i}_Tiling", new Vector4(1, 1, 0, 0));
                }
            }

            // 应用预览透明度（Shader 属性 _PreviewAlpha）
            _instancedMaterial.SetFloat("_PreviewAlpha", Mathf.Clamp01(previewAlpha));
            // 边缘渐隐宽度（可按需暴露到设置处）
            _instancedMaterial.SetFloat("_EdgeFadeStart", 0.7f);
            _instancedMaterial.SetFloat("_EdgeFadeEnd", 1.0f);
        }

        public void Dispose()
        {
            ClearMaterial();
        }

        private void ClearMaterial()
        {
            if (_instancedMaterial != null)
            {
                Object.DestroyImmediate(_instancedMaterial);
                _instancedMaterial = null;
            }
        }
    }
}