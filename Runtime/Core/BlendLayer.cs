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

        [BoxGroup("配置")]
        [Tooltip("遮罩类型：决定遮罩的应用场景和行为")]
        [OnValueChanged("OnMaskTypeChanged")]
        public MaskType maskType = MaskType.General;

        [BoxGroup("配置")]
        [AssetsOnly] // 限制只能拖入项目中的资产
        [Tooltip("关联的地形图层资产")]
        public TerrainLayer terrainLayer;

        [BoxGroup("遮罩设置")]
        [AssetsOnly]
        [InlineEditor(Expanded = false)] // 允许在列表项内直接编辑 Mask 资产，默认折叠
        [Tooltip("笔刷: 控制绘制形状、范围和强度的可复用遮罩资产")]
        [ShowIf("@maskType == MaskType.General")]
        public BlendMaskBase mask;

        [BoxGroup("遮罩设置")]
        [AssetsOnly]
        [InlineEditor(Expanded = false)]
        [Tooltip("路肩遮罩: 专门用于道路两侧的遮罩配置")]
        [ShowIf("@maskType == MaskType.Shoulder")]
        public ShoulderMask shoulderMask;

        [BoxGroup("遮罩设置")]
        [AssetsOnly]
        [InlineEditor(Expanded = false)]
        [Tooltip("路面遮罩: 专门用于道路中心区域的遮罩配置")]
        [ShowIf("@maskType == MaskType.RoadSurface")]
        public RoadSurfaceMask roadSurfaceMask;

        // ---- 对旧字段和无用字段进行隐藏 ----

        [HideInInspector] 
        public BlendMask blendMask = new(); // 隐藏旧字段

        [HideInInspector] 
        public bool isExpanded = true; // 不再需要此字段来控制UI

        /// <summary>
        /// 获取当前活动的遮罩对象
        /// </summary>
        public BlendMaskBase GetActiveMask()
        {
            return maskType switch
            {
                MaskType.General => mask,
                MaskType.Shoulder => shoulderMask,
                MaskType.RoadSurface => roadSurfaceMask,
                _ => mask
            };
        }

        /// <summary>
        /// 设置活动的遮罩对象
        /// </summary>
        public void SetActiveMask(BlendMaskBase newMask)
        {
            switch (maskType)
            {
                case MaskType.General:
                    mask = newMask;
                    break;
                case MaskType.Shoulder:
                    shoulderMask = newMask as ShoulderMask;
                    break;
                case MaskType.RoadSurface:
                    roadSurfaceMask = newMask as RoadSurfaceMask;
                    break;
            }
        }

        /// <summary>
        /// 当遮罩类型改变时的回调
        /// </summary>
        private void OnMaskTypeChanged()
        {
            // 更新图层名称以反映遮罩类型
            if (string.IsNullOrEmpty(name) || name.StartsWith("New Layer") || 
                name.Contains("路肩") || name.Contains("路面") || name.Contains("通用"))
            {
                name = maskType switch
                {
                    MaskType.Shoulder => "路肩图层",
                    MaskType.RoadSurface => "路面图层", 
                    MaskType.General => "通用图层",
                    _ => "New Layer"
                };
            }
        }

        /// <summary>
        /// 检查当前图层是否有有效的遮罩配置
        /// </summary>
        public bool HasValidMask()
        {
            return GetActiveMask() != null;
        }

        /// <summary>
        /// 获取遮罩类型的显示信息
        /// </summary>
        public string GetMaskTypeInfo()
        {
            return $"{maskType.GetDisplayName()}: {maskType.GetDescription()}";
        }
    }
}