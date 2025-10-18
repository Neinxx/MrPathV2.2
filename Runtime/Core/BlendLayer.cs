// MrPathV2/BlendLayer.cs

using System;
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    [Serializable]
    public class BlendLayer
    {
        // --- 头部属性 (Odin 的 HorizontalGroup) ---
        // UI Toolkit 将在 PropertyDrawer 中处理它们的布局
        public bool enabled = true;
        public string name = "New Layer";
        public BlendMode blendMode = BlendMode.Normal;

        [Range(0f, 1f)] // 保留标准的 [Range] 特性，UI Toolkit 的 PropertyField 会自动识别
        public float opacity = 1f;

        // --- 配置属性 (Odin 的 BoxGroup("配置")) ---
        [Tooltip("遮罩类型：决定遮罩的应用场景和行为")] // 保留标准的 [Tooltip]
        public MaskType maskType = MaskType.General;

        [Tooltip("关联的地形图层资产")]
        public TerrainLayer terrainLayer;

        // --- 遮罩设置 (Odin 的 BoxGroup("遮罩设置") 和 [ShowIf]) ---
        // [AssetsOnly] 不是标准特性，但在 PropertyDrawer 中我们可以限制
        // [InlineEditor] 逻辑将移至 PropertyDrawer
        // [ShowIf] 逻辑将移至 PropertyDrawer

        [Tooltip("笔刷: 控制绘制形状、范围和强度的可复用遮罩资产")]
        public BlendMaskBase mask;

        [Tooltip("路肩遮罩: 专门用于道路两侧的遮罩配置")]
        public ShoulderMask shoulderMask;

        [Tooltip("路面遮罩: 专门用于道路中心区域的遮罩配置")]
        public RoadSurfaceMask roadSurfaceMask;

        // --- 隐藏的旧字段 ---
        // 使用标准的 [HideInInspector] 来确保它们被序列化但不在 Inspector 中显示
        [HideInInspector]
        public BlendMask blendMask = new();

        [HideInInspector]
        public bool isExpanded = true; // UI Toolkit 的 Foldout 会自己管理展开状态


        // --- 公共方法 ---
        // 这些方法不依赖 UI，保持原样

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
                _ => mask // 默认返回通用遮罩
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
                    // 注意：这里需要安全的类型转换
                    shoulderMask = newMask as ShoulderMask;
                    break;
                case MaskType.RoadSurface:
                    roadSurfaceMask = newMask as RoadSurfaceMask;
                    break;
            }
        }

        /// <summary>
        /// 当遮罩类型改变时的回调
        /// 这个方法现在将由 PropertyDrawer 在检测到 UI 变化时调用
        /// </summary>
        public void OnMaskTypeChanged(SerializedProperty nameProperty)
        {
            // 更新图层名称以反映遮罩类型
            string currentName = nameProperty.stringValue;
            if (string.IsNullOrEmpty(currentName) || currentName.StartsWith("New Layer") ||
                currentName.Contains("路肩") || currentName.Contains("路面") || currentName.Contains("通用"))
            {
                nameProperty.stringValue = maskType switch
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
            // 假设 GetDisplayName() 和 GetDescription() 是 MaskType 的扩展方法
            // 如果不是，你需要提供它们的实现
            // return $"{maskType.GetDisplayName()}: {maskType.GetDescription()}";

            // 临时的实现，如果上面的方法不存在
            return $"{maskType}";
        }


    }
}