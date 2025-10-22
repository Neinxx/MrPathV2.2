using System;
using Sirenix.OdinInspector;
using UnityEngine;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks; // 确保 using 正确
// ... (其他 using) ...

namespace __temp.MrPathV2._2.Runtime.Core
{
    [Serializable]
    public class RoadLayer
    {
        [HideInInspector]
        public string name = "Layer"; 

        [HideInInspector]
        public bool enabled = true;

        // --- 统一的盒子开始了 ---

        /// <summary>
        /// 1. 这是统一盒子的第一个属性。
        ///    - [BoxGroup("LayerContent")] 将它放入一个盒子里。
        ///    - [ShowLabel = false] 隐藏盒子的标题。
        ///    - [HorizontalGroup] 嵌套在 BoxGroup 内部，用于放置混合模式。
        /// </summary>
        [BoxGroup("LayerContent", ShowLabel = false)]
        [HorizontalGroup("LayerContent/BlendSettings", Width = 0.3f)] // 30% 宽度
        [HideLabel]
        public BlendMode blendMode = BlendMode.Normal;
        
        /// <summary>
        /// 2. 这是同一行 ("BlendSettings") 的第二个属性。
        ///    - 它自动被包含在 "LayerContent" 盒子中，因为它的 HorizontalGroup
        ///      嵌套在 "LayerContent" 之下。
        /// </summary>
        [HorizontalGroup("LayerContent/BlendSettings", Width = 0.7f)] // 70% 宽度
        [LabelText("")]
        [LabelWidth(50)]
        [Range(0f, 1f)]
        public float opacity = 1f;

        /// <summary>
        /// 3. Content Layer 属性。
        ///    - 我们再次使用 [BoxGroup("LayerContent")] 来告诉 Odin
        ///      “继续在刚才那个盒子里绘制”。
        ///    - [Space(5)] 在 "Opacity" 和 "Content Layer" 之间添加了间距。
        /// </summary>
        [BoxGroup("LayerContent")]
        [Space(5)] 
        [LabelText("Content Layer")]
        [AssetsOnly]
        public TerrainLayer contentLayer;

        /// <summary>
        /// 4. Layer Mask 属性。
        ///    - 同样，使用 [BoxGroup("LayerContent")] 将其保留在同一个盒子中。
        /// </summary>
        [BoxGroup("LayerContent")]
        [LabelText("Layer Mask")]
        [AssetsOnly]
        [InlineEditor(Expanded = false)]
        public BlendMaskBase layerMask;
        
        // --- 统一的盒子结束了 ---


        /// <summary>
        /// 创建新图层时的默认值 (保持不变)
        /// </summary>
        public static RoadLayer CreateDefault(int index)
        {
            return new RoadLayer
            {
                name = $"Layer {index}",
                enabled = true,
                opacity = 1f, 
                blendMode = BlendMode.Normal,
                contentLayer = null,
                layerMask = null
            };
        }
    }
}