
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{
    #region 枚举定义

    public enum CurveType { Bezier, CatmullRom }
    public enum BlendMaskType { ProceduralNoise, PositionalGradient, CustomTexture }

    #endregion

    #region 数据结构

    [Serializable]
    public class BlendMask
    {
        [Tooltip("遮罩的计算方式")]
        public BlendMaskType maskType = BlendMaskType.PositionalGradient;

        [Header("噪声设置 (Procedural Noise)")]
        [Tooltip("噪声纹理的缩放比例")]
        [Min(0.1f)] public float noiseScale = 10f;
        [Tooltip("噪声对遮罩的影响强度")]
        [Range(0f, 1f)] public float noiseStrength = 1f;

        [Header("位置渐变 (Positional Gradient)")]
        [Tooltip("沿路径宽度方向的分布曲线（X轴：-1左边界 ~ 1右边界，Y轴：0~1透明度）")]
        public AnimationCurve gradient = AnimationCurve.Linear(-1, 1, 1, 1);

        [Header("自定义贴图 (Custom Texture)")]
        [Tooltip("作为遮罩的自定义纹理（Alpha通道控制透明度）")]
        public Texture2D customTexture;
    }

    [Serializable]
    public class BlendLayer
    {
        [Tooltip("关联的地形图层资产")]
        [RequiredField] public TerrainLayer terrainLayer;
        [Tooltip("控制该纹理在路径上的分布范围")]
        public BlendMask blendMask = new();
    }

    [Serializable]
    public class LayerBlendRecipe
    {
        [Tooltip("按顺序叠加的纹理图层列表")]
        public List<BlendLayer> blendLayers = new();
    }

    [Serializable]
    public class PathLayer
    {
        public string name = "New Layer";
        [Header("几何属性")]
        [Min(0.1f)] public float width = 5f;
        public float horizontalOffset = 0f;
        public float verticalOffset = 0.1f;
        [Header("外观定义")]
        public LayerBlendRecipe terrainPaintingRecipe = new();

        [HideInInspector] public bool isExpanded = true;
    }

    #endregion
}