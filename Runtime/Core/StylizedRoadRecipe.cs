using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 美术定义的风格化道路配方：可自由组合项目中的 TerrainLayer，并通过 BlendMask 控制横向分布。

    /// </summary>
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        [Title("混合图层 (Passes)")]
        [InfoBox("此配方通过层叠混合来定义道路的最终材质。每个图层由一个地形层(颜色)和一个遮罩(形状)组成，它们自下而上进行混合。")]
        [ListDrawerSettings(
            DraggableItems = true,          // 允许拖拽排序
            ShowFoldout = true,             // 显示每个列表项的折叠箭头
            ShowItemCount = true,           // 显示列表项数量
            DefaultExpandedState = true,                // 默认展开整个列表
            CustomAddFunction = "AddNewLayer" // 指定一个自定义函数来处理“添加”按钮的点击事件
        )]
        public List<BlendLayer> blendLayers = new List<BlendLayer>();

        /// <summary>
        /// 当点击 "+" 按钮时，Odin 会调用这个函数。
        /// 这允许我们为新图层设置合理的默认值。
        /// </summary>
        private void AddNewLayer()
        {
            blendLayers.Add(new BlendLayer
            {
                name = $"Layer {blendLayers.Count}",
                enabled = true,
                opacity = 0.85f,
                blendMode = BlendMode.Normal
            });
        }

        [Tooltip("道路的总宽度（米）。")]
        public float width = 5f;

        [Tooltip("道路边缘与地形混合的过渡距离（米）。")]
        public float falloff = 1.5f;

        [Tooltip("配方整体透明度 (0-1)，用于统一控制所有图层的不透明度。")]
        [Range(0f,1f)]
        public float masterOpacity = 1f;
    }
}