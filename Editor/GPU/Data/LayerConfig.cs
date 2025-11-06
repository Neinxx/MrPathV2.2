using System;
using UnityEngine;

namespace MrPathV2.Editor.GPU.Data
{
    /// <summary>
    ///     图层配置数据结构
    ///     用于定义地形图层的绘制参数
    /// </summary>
    [Serializable]
    public class LayerConfig
    {
        [Header("图层设置")]
        public int LayerIndex;
        public float Strength = 1.0f;
        public BlendMode BlendMode = BlendMode.Normal;

        [Header("高级设置")]
        public Vector4 MaskParams = Vector4.zero;
        public bool UseCustomBlending;

        public LayerConfig() { }

        public LayerConfig(int layerIndex, float strength, BlendMode blendMode)
        {
            LayerIndex = layerIndex;
            Strength = strength;
            BlendMode = blendMode;
        }

        public override int GetHashCode() => HashCode.Combine(LayerIndex, Strength, BlendMode, MaskParams);

        public override bool Equals(object obj)
        {
            if (obj is LayerConfig other)
            {
                return LayerIndex == other.LayerIndex &&
                       Mathf.Approximately(Strength, other.Strength) &&
                       BlendMode == other.BlendMode &&
                       MaskParams == other.MaskParams;
            }
            return false;
        }
    }

    /// <summary>
    ///     混合模式枚举
    /// </summary>
    public enum BlendMode
    {
        Normal = 0,
        Replace = 1,
        Add = 2,
        Multiply = 3,
        Overlay = 4,
        Screen = 5,
        SoftLight = 6
    }
}
