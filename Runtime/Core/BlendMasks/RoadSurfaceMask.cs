using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 路面遮罩：主要作用于道路中心区域的遮罩类型
    /// 提供道路主体表面的纹理控制，适用于沥青、混凝土、石板等路面材质
    /// </summary>
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Road Surface Mask")]
    public class RoadSurfaceMask : BlendMaskBase
    {
        [Header("路面设置")]
        [Tooltip("路面覆盖宽度占道路总宽度的比例 (0-1)")]
        [Range(0f, 1f)]
        public float surfaceWidthRatio = 0.8f;

        [Tooltip("路面强度：控制路面区域的遮罩强度")]
        [Range(0f, 1f)]
        public float surfaceStrength = 1f;

        [Tooltip("边缘过渡：控制路面边缘的柔和过渡距离")]
        [Range(0f, 0.5f)]
        public float edgeTransition = 0.1f;

        [Tooltip("中心偏移：调整路面中心位置 (-1到1)")]
        [Range(-1f, 1f)]
        public float centerOffset = 0f;
        
        [Header("高级设置")]
        [Tooltip("路面形状曲线：控制路面区域内的强度分布")]
        public AnimationCurve surfaceProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 1f);

        [Tooltip("边缘衰减类型")]
        public EdgeFalloffType falloffType = EdgeFalloffType.SmoothStep;

        [Tooltip("自定义边缘衰减曲线，仅当 FalloffType = Custom 时有效")] 
        public AnimationCurve customFalloffCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public enum EdgeFalloffType
        {
            Linear,
            SmoothStep,
            Exponential,
            Custom
        }

        public override float Evaluate(float horizontalPosition, float worldWidth)
        {
            // 应用中心偏移
            float adjustedPosition = horizontalPosition - centerOffset;
            
            // 计算到中心的距离
            float distanceFromCenter = Mathf.Abs(adjustedPosition);
            
            // 计算路面的有效半径
            float surfaceHalfWidth = surfaceWidthRatio * 0.5f;
            
            float maskValue = 0f;
            
            // 检查是否在路面区域内
            if (distanceFromCenter <= surfaceHalfWidth)
            {
                // 计算在路面区域内的相对位置 (0到1，0为中心，1为边缘)
                float relativePosition = distanceFromCenter / surfaceHalfWidth;
                
                // 应用路面形状曲线
                float profileValue = surfaceProfile.Evaluate(1f - relativePosition); // 反转，使中心为1
                
                // 应用边缘过渡
                if (edgeTransition > 0f && relativePosition > (1f - edgeTransition))
                {
                    float transitionStart = 1f - edgeTransition;
                    float transitionProgress = (relativePosition - transitionStart) / edgeTransition;
                    
                    float falloffFactor = CalculateFalloff(1f - transitionProgress);
                    profileValue *= falloffFactor;
                }
                
                maskValue = profileValue * surfaceStrength;
            }
            
            return ApplySmoothing(Mathf.Clamp01(maskValue));
        }
        
        /// <summary>
        /// 计算边缘衰减系数
        /// </summary>
        private float CalculateFalloff(float t)
        {
            switch (falloffType)
            {
                case EdgeFalloffType.Linear:
                    return t;
                    
                case EdgeFalloffType.SmoothStep:
                    return t * t * (3f - 2f * t);
                    
                case EdgeFalloffType.Exponential:
                    return Mathf.Pow(t, 2f);
                    
                case EdgeFalloffType.Custom:
                    return customFalloffCurve.Evaluate(t);
                    
                default:
                    return t;
            }
        }
        
        /// <summary>
        /// 获取路面的有效宽度（米）
        /// </summary>
        public float GetSurfaceWidth(float worldWidth)
        {
            return worldWidth * surfaceWidthRatio;
        }
        
        /// <summary>
        /// 获取路面中心的世界位置偏移（米）
        /// </summary>
        public float GetCenterOffset(float worldWidth)
        {
            return centerOffset * worldWidth * 0.5f;
        }
        
        /// <summary>
        /// 检查指定位置是否在路面区域内
        /// </summary>
        public bool IsInSurfaceArea(float horizontalPosition)
        {
            float adjustedPosition = horizontalPosition - centerOffset;
            float distanceFromCenter = Mathf.Abs(adjustedPosition);
            float surfaceHalfWidth = surfaceWidthRatio * 0.5f;
            
            return distanceFromCenter <= surfaceHalfWidth;
        }
        
        /// <summary>
        /// 获取路面区域的边界位置
        /// </summary>
        public Vector2 GetSurfaceBounds()
        {
            float halfWidth = surfaceWidthRatio * 0.5f;
            return new Vector2(centerOffset - halfWidth, centerOffset + halfWidth);
        }
    }
}