using UnityEngine;

namespace MrPathV2
{
    public abstract class BlendMaskBase : ScriptableObject
    {
        [Header("Mask Settings")]
        [Range(0f, 1f)]
        [Tooltip("遮罩边缘的平滑度。0=硬边缘，1=最大平滑")]
        public float smooth = 0.1f;

        /// <summary>
        /// 计算遮罩在某个位置的强度。
        /// </summary>
        /// <param name="horizontalPosition">-1 (左) 到 1 (右) 的标准化位置</param>
        /// <param name="worldWidth">该位置所在的区域所代表的真实世界宽度 (米)</param>
        /// <returns>强度值 (0 到 1)</returns>
        public abstract float Evaluate(float horizontalPosition, float worldWidth);

        /// <summary>
        /// 应用平滑处理到遮罩值
        /// </summary>
        /// <param name="maskValue">原始遮罩值</param>
        /// <returns>平滑处理后的遮罩值</returns>
        protected float ApplySmoothing(float maskValue)
        {
            if (smooth <= 0f) return maskValue;
            
            // 使用smoothstep函数创建平滑过渡
            float edge0 = smooth * 0.5f;
            float edge1 = 1f - smooth * 0.5f;
            
            if (maskValue <= edge0) return 0f;
            if (maskValue >= edge1) return 1f;
            
            // smoothstep插值
            float t = (maskValue - edge0) / (edge1 - edge0);
            return t * t * (3f - 2f * t);
        }
    }
}