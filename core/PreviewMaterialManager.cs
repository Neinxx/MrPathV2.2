// PreviewMaterialManager.cs (守护者版)
using System.Collections.Generic;
using UnityEngine;
using MrPathV2;

public class PreviewMaterialManager : System.IDisposable
{
    private Material _instancedMaterial;

    public List<Material> GetFrameRenderMaterials()
    {
        var list = new List<Material>(1);
        if (_instancedMaterial != null)
        {
            list.Add(_instancedMaterial);
        }
        return list;
    }

    public void UpdateMaterials(PathProfile profile, Material templateSplatShaderMaterial)
    {
        if (profile == null || profile.layers == null || templateSplatShaderMaterial == null)
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

        var layers = profile.layers;
        for (int i = 0; i < 4; i++)
        {
            if (i < layers.Count && layers[i].terrainPaintingRecipe?.blendLayers.Count > 0)
            {
                var terrainLayer = layers[i].terrainPaintingRecipe.blendLayers[0].terrainLayer;
                if (terrainLayer != null)
                {
                    _instancedMaterial.SetTexture($"_Layer{i}_Texture", terrainLayer.diffuseTexture);

                    // ✨✨✨【守护神通】✨✨✨
                    // 在传递 Tiling 数据前，进行安全检查
                    Vector2 tileSize = terrainLayer.tileSize;
                    if (Mathf.Approximately(tileSize.x, 0f)) tileSize.x = 1f; // 若x为0，设为1
                    if (Mathf.Approximately(tileSize.y, 0f)) tileSize.y = 1f; // 若y为0，设为1

                    _instancedMaterial.SetVector($"_Layer{i}_Tiling", new Vector4(tileSize.x, tileSize.y, 0, 0));
                }
            }
            else
            {
                _instancedMaterial.SetTexture($"_Layer{i}_Texture", Texture2D.whiteTexture);
            }
        }
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