// 请用此完整代码替换你的 PreviewMaterialManager.cs

using System.Collections.Generic;
using UnityEngine;
using MrPathV2;

public class PreviewMaterialManager : System.IDisposable
{
    private readonly List<Material> _instancedMaterials = new();
    private readonly List<Material> _frameRenderMaterials = new();

    public void UpdateMaterials(PathProfile profile, Material template)
    {
        if (profile == null || profile.layers == null || template == null)
        {
            ClearAllMaterials();
            return;
        }
        if (profile.layers.Count != _frameRenderMaterials.Count)
        {
            RebuildMaterialList(profile.layers, template);
            return;
        }
        for (int i = 0; i < profile.layers.Count; i++)
        {
            UpdateMaterialAt(i, profile.layers[i], template);
        }
    }

    // 【【【 核心修正 I：斩草除根 】】】
    /// <summary>
    /// 获取当前帧的渲染材质列表（只读）。
    /// 返回内部列表的直接引用，以避免在每帧都产生垃圾。
    /// </summary>
    public List<Material> GetFrameRenderMaterials()
    {
        return _frameRenderMaterials; // 直接返回正本，不再复制
    }

    public void Dispose()
    {
        ClearAllMaterials();
    }

    #region Internal Logic (No Changes)
    private void UpdateMaterialAt(int index, PathLayer layer, Material template)
    {
        if (layer == null || template == null || index < 0) { RemoveMaterialAt(index); return; }
        Texture targetDiffuse = GetLayerDiffuse(layer);
        Material currentMat = GetMaterialAt(index);
        bool needRebuild = currentMat == null || !_instancedMaterials.Contains(currentMat) || currentMat.mainTexture != targetDiffuse;
        if (needRebuild)
        {
            if (currentMat != null) { RemoveMaterialAt(index); }
            if (targetDiffuse != null)
            {
                Material newMat = CreateLayerMaterial(layer, template, targetDiffuse);
                InsertMaterialAt(index, newMat);
            }
        }
    }
    private Texture GetLayerDiffuse(PathLayer layer)
    {
        if (layer?.terrainPaintingRecipe?.blendLayers == null || layer.terrainPaintingRecipe.blendLayers.Count == 0)
            return null;
        return layer.terrainPaintingRecipe.blendLayers[0]?.terrainLayer?.diffuseTexture;
    }
    private void RebuildMaterialList(List<PathLayer> layers, Material template)
    {
        ClearAllMaterials();
        foreach (PathLayer layer in layers)
        {
            Texture diffuse = GetLayerDiffuse(layer);
            if (diffuse != null && template != null)
            {
                Material mat = CreateLayerMaterial(layer, template, diffuse);
                _instancedMaterials.Add(mat);
                _frameRenderMaterials.Add(mat);
            }
            else
            {
                _frameRenderMaterials.Add(null);
            }
        }
    }
    private Material CreateLayerMaterial(PathLayer layer, Material template, Texture diffuse)
    {
        Material newMat = new Material(template)
        {
            mainTexture = diffuse,
            name = $"PreviewMat_{layer.name}_{diffuse.name}",
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };
        return newMat;
    }
    private void InsertMaterialAt(int index, Material mat)
    {
        if (mat == null) return;
        if (index < _frameRenderMaterials.Count) _frameRenderMaterials[index] = mat;
        else _frameRenderMaterials.Add(mat);
        if (!_instancedMaterials.Contains(mat)) _instancedMaterials.Add(mat);
    }
    private void RemoveMaterialAt(int index)
    {
        if (index < 0 || index >= _frameRenderMaterials.Count) return;
        Material matToRemove = _frameRenderMaterials[index];
        if (matToRemove != null && _instancedMaterials.Contains(matToRemove))
        {
            Object.DestroyImmediate(matToRemove);
            _instancedMaterials.Remove(matToRemove);
        }
        _frameRenderMaterials[index] = null;
    }
    private Material GetMaterialAt(int index) => index >= 0 && index < _frameRenderMaterials.Count ? _frameRenderMaterials[index] : null;
    private void ClearAllMaterials()
    {
        foreach (Material mat in _instancedMaterials)
            if (mat != null) Object.DestroyImmediate(mat);
        _instancedMaterials.Clear();
        _frameRenderMaterials.Clear();
    }
    #endregion
}