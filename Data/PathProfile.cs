using System.Collections.Generic;
using UnityEngine;

// --- 此文件现在包含所有与Profile相关的数据结构 ---

#region Enums

public enum SegmentOutputMode { TerrainPainting, StandaloneMesh }
public enum BlendMaskType { ProceduralNoise, PositionalGradient, CustomTexture }

#endregion

#region Data Structures

/// <summary>
/// 混合遮罩：定义了图层的绘制区域。
/// </summary>
[System.Serializable]
public class BlendMask
{
    public BlendMaskType maskType = BlendMaskType.PositionalGradient;

    [Header ("噪声设置 (Procedural Noise)")]
    public float noiseScale = 10f;
    public float noiseStrength = 1f;

    [Header ("位置渐变 (Positional Gradient)")]
    public AnimationCurve gradient = AnimationCurve.Linear (-1, 1, 1, 1); // 默认从左到右完全覆盖

    [Header ("自定义贴图 (Custom Texture)")]
    public Texture2D customTexture;
}

/// <summary>
/// 混合图层：最小的纹理单元。
/// </summary>
[System.Serializable]
public class BlendLayer
{
    [Tooltip ("关联的地形图层资产")]
    public TerrainLayer terrainLayer;

    [Tooltip ("控制该图层分布的遮罩")]
    public BlendMask blendMask = new BlendMask ();
}

/// <summary>
/// 图层混合配方：包含一个有序的混合图层列表。
/// </summary>
[System.Serializable]
public class LayerBlendRecipe
{
    public List<BlendLayer> blendLayers = new List<BlendLayer> ();
}

/// <summary>
/// 分段 (Segment)：Profile的组成部分。
/// </summary>
[System.Serializable]
public class ProfileSegment
{
    public string name = "New Segment";
    public SegmentOutputMode outputMode = SegmentOutputMode.TerrainPainting;

    [Header ("几何属性")]
    public float width = 5f;
    public float horizontalOffset = 0f;
    public float verticalOffset = 0.1f;

    [Header ("外观定义")]
    // 【修改】使用新的图层混合配方
    public LayerBlendRecipe terrainPaintingRecipe = new LayerBlendRecipe ();
    public Material standaloneMeshMaterial;
}

#endregion

/// <summary>
/// 剖面 (Profile)：核心的ScriptableObject资产。
/// </summary>
[CreateAssetMenu (fileName = "NewPathProfile", menuName = "Path Tool/Path Profile")]
public class PathProfile : ScriptableObject
{
    [Header ("全局路径设置")]
    [Tooltip ("生成预览网格时顶点间的最小间距")]
    [Range (0.1f, 10f)]
    public float minVertexSpacing = 0.5f;

    [Header ("路径分段")]
    public List<ProfileSegment> segments = new List<ProfileSegment> ();
}
