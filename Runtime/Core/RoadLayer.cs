using System;
using MrPathV2.Runtime.Core.BlendMasks;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    [Serializable]
    public class RoadLayer
    {
        // 名称与启用状态
        public string name = "Layer";
        public bool enabled = true;
        public bool maskEnabled = true;

        // 混合模式与不透明度（纯数据）
        public BlendMode blendMode = BlendMode.Normal;
        public float opacity = 1f;

        // 内容层与遮罩（纯数据）
        public TerrainLayer contentLayer;
        public BlendMaskBase layerMask;

        /// <summary>
        ///     创建新图层时的默认值。
        /// </summary>
        public static RoadLayer CreateDefault(int index) => new RoadLayer
        {
            name = $"Layer {index}",
            enabled = true,
            maskEnabled = true,
            opacity = 1f,
            blendMode = BlendMode.Normal,
            contentLayer = null,
            layerMask = null
        };
    }
}
