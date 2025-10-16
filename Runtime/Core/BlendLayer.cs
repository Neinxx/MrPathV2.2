// MrPathV2/BlendLayer.cs

using System;
using UnityEngine;
using Sirenix.OdinInspector; // 引入 Odin Inspector 命名空间

namespace MrPathV2
{
    [Serializable]
    public class BlendLayer
    {
        // Odin 会用这个组作为列表项的标题行，非常紧凑和高效
        [HorizontalGroup("Header", 75, LabelWidth = 55)]
        [BoxGroup("Header/Left", showLabel: false)]
        public bool enabled = true;
        
        [BoxGroup("Header/Left", showLabel: false)]
        [HideLabel]
        public string name = "New Layer";

        [HorizontalGroup("Header", MarginLeft = 0.05f)]
        [HideLabel]
        public BlendMode blendMode = BlendMode.Normal;

        [HorizontalGroup("Header", Width = 150)]
        [HideLabel]
        [Range(0f, 1f)] 
        public float opacity = 1f;

        // ---- 当列表项展开时，以下属性会显示出来 ----

        [AssetsOnly] // 限制只能拖入项目中的资产
        [Tooltip("关联的地形图层资产")]
        public TerrainLayer terrainLayer;

        [AssetsOnly]
        [InlineEditor(Expanded = false)] // 允许在列表项内直接编辑 Mask 资产，默认折叠
        [Tooltip("笔刷: 控制绘制形状、范围和强度的可复用遮罩资产")]
        public BlendMaskBase mask;

        // ---- 对旧字段和无用字段进行隐藏 ----

        [HideInInspector] 
        public BlendMask blendMask = new(); // 隐藏旧字段

        [HideInInspector] 
        public bool isExpanded = true; // 不再需要此字段来控制UI
    }
}