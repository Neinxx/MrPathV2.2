using UnityEngine;
using Sirenix.OdinInspector;

namespace MrPathV2
{
    /// <summary>
    /// 路肩遮罩：永远出现在道路两侧的遮罩类型
    /// 提供对称的边缘效果，适用于路缘石、护栏、边线等道路边缘元素
    /// </summary>
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Shoulder Mask")]
    public class ShoulderMask : BlendMaskBase
    {
        [BoxGroup("路肩设置")]
        [Tooltip("路肩宽度占道路总宽度的比例 (0-0.5)")]
        [Range(0f, 0.5f)]
        public float shoulderWidthRatio = 0.15f;
        
        [BoxGroup("路肩设置")]
        [Tooltip("路肩强度：控制路肩区域的遮罩强度")]
        [Range(0f, 1f)]
        public float shoulderStrength = 1f;
        
        [BoxGroup("路肩设置")]
        [Tooltip("边缘衰减：控制路肩向内衰减的距离比例")]
        [Range(0f, 0.3f)]
        public float edgeFalloff = 0.05f;
        
        [BoxGroup("路肩设置")]
        [Tooltip("是否启用左侧路肩")]
        public bool enableLeftShoulder = true;
        
        [BoxGroup("路肩设置")]
        [Tooltip("是否启用右侧路肩")]
        public bool enableRightShoulder = true;
        
        [BoxGroup("高级设置")]
        [Tooltip("路肩形状曲线：控制路肩区域内的强度分布")]
        public AnimationCurve shoulderProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 1f);

        public override float Evaluate(float horizontalPosition, float worldWidth)
        {
            // horizontalPosition: -1(左边界) 到 1(右边界)
            float absPosition = Mathf.Abs(horizontalPosition);
            
            // 计算路肩区域的边界
            float shoulderInnerBoundary = 1f - shoulderWidthRatio; // 路肩内边界
            float shoulderOuterBoundary = 1f; // 路肩外边界（道路边缘）
            
            float maskValue = 0f;
            
            // 检查是否在路肩区域内
            if (absPosition >= shoulderInnerBoundary)
            {
                // 确定是左侧还是右侧路肩
                bool isLeftShoulder = horizontalPosition < 0;
                bool isRightShoulder = horizontalPosition > 0;
                
                // 检查对应侧的路肩是否启用
                if ((isLeftShoulder && enableLeftShoulder) || (isRightShoulder && enableRightShoulder))
                {
                    // 计算在路肩区域内的相对位置 (0到1)
                    float shoulderWidth = shoulderOuterBoundary - shoulderInnerBoundary;
                    float relativePosition = (absPosition - shoulderInnerBoundary) / shoulderWidth;
                    
                    // 应用路肩形状曲线
                    float profileValue = shoulderProfile.Evaluate(relativePosition);
                    
                    // 应用边缘衰减
                    if (edgeFalloff > 0f)
                    {
                        float falloffDistance = edgeFalloff;
                        float distanceFromInner = relativePosition;
                        
                        if (distanceFromInner < falloffDistance)
                        {
                            // 从路肩内边界向外的衰减
                            float falloffFactor = distanceFromInner / falloffDistance;
                            profileValue *= falloffFactor;
                        }
                    }
                    
                    maskValue = profileValue * shoulderStrength;
                }
            }
            
            return ApplySmoothing(Mathf.Clamp01(maskValue));
        }
        
        /// <summary>
        /// 获取路肩的有效宽度（米）
        /// </summary>
        public float GetShoulderWidth(float worldWidth)
        {
            return worldWidth * shoulderWidthRatio;
        }
        
        /// <summary>
        /// 检查指定位置是否在路肩区域内
        /// </summary>
        public bool IsInShoulderArea(float horizontalPosition)
        {
            float absPosition = Mathf.Abs(horizontalPosition);
            float shoulderInnerBoundary = 1f - shoulderWidthRatio;
            
            if (absPosition < shoulderInnerBoundary) return false;
            
            bool isLeftShoulder = horizontalPosition < 0;
            bool isRightShoulder = horizontalPosition > 0;
            
            return (isLeftShoulder && enableLeftShoulder) || (isRightShoulder && enableRightShoulder);
        }
    }
}