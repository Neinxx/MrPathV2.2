using System;
using Sirenix.OdinInspector;
using UnityEngine;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;

namespace __temp.MrPathV2._2.Runtime.Core
{
    /// <summary>
    /// 一个更简洁直观的道路图层定义，对标 Photoshop Layer：
    /// 1. contentLayer   : 实际颜色/纹理（TerrainLayer）
    /// 2. layerMask      : 控制显隐（BlendMaskBase 及其派生类）
    /// 3. blendMode      : 叠加模式（参考 Photoshop）
    /// 4. opacity        : 整体不透明度 (0~1)
    /// <para/>
    /// 后续运行时系统将直接遍历 StylizedRoadRecipe.layers 生成数组，
    /// 取代旧的 BlendLayer 结构。
    /// </summary>
    [Serializable]
    public class RoadLayer
    {
        [HorizontalGroup("Header", Width = 20)]
        [HideLabel]
        [ToggleLeft]
        public bool enabled = true;

        // [BoxGroup("Header/Left", showLabel: false)]
        // [HideLabel]
        // public string name = "New Layer";

        [BoxGroup("Content")]
        [LabelText("Content Layer")]
        [AssetsOnly]
        [Tooltip("要绘制到道路上的 TerrainLayer (颜色/纹理)")]
        public TerrainLayer contentLayer;

        [BoxGroup("Mask")]
        [LabelText("Layer Mask")]
        [AssetsOnly]
        [InlineEditor(Expanded = false)]
        public BlendMaskBase layerMask;

        [HorizontalGroup("Header", Width = 100)]
        [HideLabel]
        public BlendMode blendMode = BlendMode.Normal;

        // 调整 UI/UX：为 Opacity 提供明确标签并保证滑块与输入框在同一行右对齐
        [HorizontalGroup("Header", Width = 0)]
        [LabelText("Opacity")]
        [LabelWidth(50)]
        [Range(0f, 1f)]
        public float opacity = 1f;

        /// <summary>
        /// 供 Inspector 自定义添加新图层时调用，以设定合理默认值。
        /// </summary>
        public static RoadLayer CreateDefault(int index)
        {
            return new RoadLayer
            {
               // name = $"Layer {index}",
                enabled = true,
                opacity = 0.85f,
                blendMode = BlendMode.Normal,
                contentLayer = null,
                layerMask = null
            };
        }
    }
}