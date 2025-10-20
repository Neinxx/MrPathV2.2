using Sirenix.OdinInspector;
using UnityEngine;

namespace MrPathV2
{
    public abstract class BlendMaskBase : ScriptableObject
    {
        [BoxGroup("遮罩设置")]
        [Range(0f, 1f)]
        [Tooltip("遮罩边缘的平滑度。0=硬边缘，1=最大平滑")]
        public float smooth = 0.1f;

        // 新增：所有遮罩通用的平铺、偏移和整体缩放设置
        [BoxGroup("UV Settings")]
        [LabelText("Tiling (X=Width, Y=Length) [m]")]
        [Tooltip("控制遮罩在道路横向(宽度)和纵向(长度)方向上的平铺倍率，单位：米")] 
        [MinValue(0.1f)]
        public Vector2 tiling = new Vector2(2f, 2f);

        [BoxGroup("UV Settings")]
        [LabelText("Offset")]
        [Tooltip("UV 偏移，允许整体平移遮罩")] 
        public Vector2 offset = Vector2.zero;

        [BoxGroup("UV Settings")]
        [LabelText("Overall Scale")]
        [Tooltip("整体缩放因子，最终遮罩值将乘以该系数")] 
        [Range(0f, 2f)]
        public float overallScale = 1f;

        // -------------------------------- Helper Utilities --------------------------------
        /// <summary>
        /// 将水平位置 (-1..1) 映射到 0..1 的 UV，并应用世界宽度、平铺(tiling.x)和offset.x。
        /// </summary>
        /// <summary>
         /// 将水平位置 (-1..1) 映射到 0..1 的 UV，并应用世界宽度、平铺(tiling.x)和offset.x。
         /// </summary>
         protected float TransformPosition(float horizontalPosition, float worldWidth)
         {
             float u = (horizontalPosition + 1f) * 0.5f; // -1..1 => 0..1
             float repeatCount = worldWidth / Mathf.Max(0.0001f, tiling.x);
             return u * repeatCount + offset.x;
         }

         /// <summary>
         /// 向后兼容：带 pathLength 参数的版本，内部调用不含 pathLength 的实现。
         /// </summary>
         protected float TransformPosition(float horizontalPosition, float worldWidth, float pathLength)
         {
             return TransformPosition(horizontalPosition, worldWidth);
         }

        /// <summary>
        /// 计算沿路径方向(0..1)的UV，并应用道路长度、平铺(tiling.y)和offset.y。
        /// </summary>
        protected float TransformPathPosition(float pathProgress, float pathLength)
        {
            float repeatCount = pathLength / Mathf.Max(0.0001f, tiling.y);
            return pathProgress * repeatCount + offset.y;
        }

        /// <summary>
        /// 对遮罩值应用整体缩放（强度）。
        /// </summary>
        protected float ApplyScale(float value) => value * overallScale;

        /// <summary>
        /// 计算遮罩在某个位置的强度。
        /// </summary>
        /// <param name="horizontalPosition">-1 (左) 到 1 (右) 的标准化位置</param>
        /// <param name="worldWidth">该位置所在的区域所代表的真实世界宽度 (米)</param>
        /// <param name="pathLength">道路的总长度 (米)，用于沿路径方向的噪声计算</param>
        /// <returns>强度值 (0 到 1)</returns>
        public abstract float Evaluate(float horizontalPosition, float worldWidth, float pathLength);

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