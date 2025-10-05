using System.Collections.Generic;
using UnityEngine;

// --- 此文件现在包含所有与Profile相关的数据结构 ---

#region Enums

public enum SegmentOutputMode { TerrainPainting, StandaloneMesh }
public enum BlendMaskType { ProceduralNoise, PositionalGradient, CustomTexture }

#endregion

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
/// 【新增】路径图层：定义了一个拥有独立几何形状和地形纹理混合配方的渲染层。
/// 它取代了旧的 ProfileSegment。
/// </summary>
[System.Serializable]
public class PathLayer
{
    public string name = "New Layer";

    [Header ("几何属性 (Geometry)")]
    [Tooltip ("该图层的总宽度")]
    public float width = 5f;
    [Tooltip ("该图层中心线距离路径中心线的水平偏移")]
    public float horizontalOffset = 0f;
    [Tooltip ("该图层相对于路径的垂直偏移（抬高或降低）")]
    public float verticalOffset = 0.1f;

    [Header ("外观定义 (Appearance)")]
    [Tooltip ("用于定义该图层如何与地形混合的纹理配方")]
    public LayerBlendRecipe terrainPaintingRecipe = new LayerBlendRecipe ();
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
/// 【重构后】剖面 (Profile)：核心的ScriptableObject资产。
/// 现在它由一系列有序的“路径图层”构成。
/// </summary>
[CreateAssetMenu (fileName = "NewPathProfile", menuName = "Path Tool/Path Profile")]
public class PathProfile : ScriptableObject
{
    [Header ("全局路径设置")]
    [Tooltip ("生成预览网格时顶点间的最小间距")]
    [Range (0.1f, 10f)]
    public float minVertexSpacing = 0.5f;

    [Header ("路径渲染图层 (Layers)")]
    [Tooltip ("定义路径外观的图层列表。第一个图层通常是主路面，后续图层是路肩或过渡带。")]
    public List<PathLayer> layers = new List<PathLayer> ();
}
