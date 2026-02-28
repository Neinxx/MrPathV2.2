using System;

namespace MrPathV2
{
    /// <summary>
    /// 遮罩类型枚举：定义不同的遮罩应用场景
    /// </summary>
    [Serializable]
    public enum MaskType
    {
        /// <summary>
        /// 通用遮罩：传统的遮罩类型，可用于任何位置
        /// </summary>
        General = 0,
        
        /// <summary>
        /// 路肩遮罩：专门用于道路两侧的遮罩，如路缘石、护栏等
        /// </summary>
        Shoulder = 1,
        
        /// <summary>
        /// 路面遮罩：专门用于道路中心区域的遮罩，如沥青、混凝土等路面材质
        /// </summary>
        RoadSurface = 2
    }
    
    /// <summary>
    /// 遮罩类型扩展方法
    /// </summary>
    public static class MaskTypeExtensions
    {
        /// <summary>
        /// 获取遮罩类型的显示名称
        /// </summary>
        public static string GetDisplayName(this MaskType maskType)
        {
            return maskType switch
            {
                MaskType.General => "通用遮罩",
                MaskType.Shoulder => "路肩遮罩",
                MaskType.RoadSurface => "路面遮罩",
                _ => "未知类型"
            };
        }
        
        /// <summary>
        /// 获取遮罩类型的描述
        /// </summary>
        public static string GetDescription(this MaskType maskType)
        {
            return maskType switch
            {
                MaskType.General => "传统的遮罩类型，可用于任何位置和场景",
                MaskType.Shoulder => "专门用于道路两侧，如路缘石、护栏、边线等元素",
                MaskType.RoadSurface => "专门用于道路中心区域，如沥青、混凝土等路面材质",
                _ => "未定义的遮罩类型"
            };
        }
        
        /// <summary>
        /// 检查遮罩类型是否为位置特定类型
        /// </summary>
        public static bool IsPositionSpecific(this MaskType maskType)
        {
            return maskType == MaskType.Shoulder || maskType == MaskType.RoadSurface;
        }
    }
}