using Sirenix.OdinInspector;
using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core.BlendMasks
{
    /// <summary>
    /// 所有遮罩类型的基类，提供通用的 UV 变换、平滑及整体缩放等功能。
    /// </summary>
    public abstract class BlendMaskBase : ScriptableObject
    {
        #region Inspector Fields

        [BoxGroup("遮罩设置")]
        [Range(0f, 1f)]
        [Tooltip("遮罩边缘的平滑度。0 = 硬边缘，1 = 最大平滑")]
        public float smooth = 0.1f;

        // ---- 通用 UV 参数 ----
        [BoxGroup("UV Settings")]
        [LabelText("Tiling (X = Width, Y = Length) [m]")]
        [Tooltip("控制遮罩在道路横向(宽度)与纵向(长度)方向上的平铺尺寸，单位：米。允许负值以实现镜像/翻转效果。")]
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

        #endregion

        #region Utility Functions

        /// <summary>
        /// 将水平位置 (-1..1) 映射到 0..1 的 U，并应用世界宽度、平铺 (tiling.x) 与 offset.x。
        /// 支持负 tiling 以产生镜像效果。
        /// </summary>
        protected float TransformPosition(float horizontalPosition, float worldWidth)
        {
            float u = (horizontalPosition + 1f) * 0.5f; // -1..1 => 0..1
            float denomX = Mathf.Abs(tiling.x) < 1e-4f ? 1e-4f * Mathf.Sign(tiling.x == 0 ? 1f : tiling.x) : tiling.x;
            float repeatCount = worldWidth / denomX; // 保留符号可实现翻转
            return u * repeatCount + offset.x;
        }

        /// <summary>
        /// 向后兼容：包含 pathLength 参数的重载，占位以保持旧代码兼容。
        /// </summary>
        protected float TransformPosition(float horizontalPosition, float worldWidth, float pathLength)
        {
            return TransformPosition(horizontalPosition, worldWidth);
        }

        /// <summary>
        /// 计算沿路径方向 (0..1) 的 V，并应用路径长度、平铺 (tiling.y) 与 offset.y。
        /// 支持负 tiling 以产生镜像效果。
        /// </summary>
        protected float TransformPathPosition(float pathProgress, float pathLength)
        {
            float denomY = Mathf.Abs(tiling.y) < 1e-4f ? 1e-4f * Mathf.Sign(tiling.y == 0 ? 1f : tiling.y) : tiling.y;
            float repeatCount = pathLength / denomY; // 保留符号可实现翻转
            return pathProgress * repeatCount + offset.y;
        }

        /// <summary>
        /// 对遮罩值应用整体缩放（强度）。
        /// </summary>
        protected float ApplyScale(float value) => value * overallScale;

        #endregion

        #region Evaluate API

        /// <summary>
        /// 计算遮罩在某个位置的强度（推荐接口，包含 pathProgress 维度）。
        /// </summary>
        /// <param name="horizontalPosition">-1 (左) 到 1 (右) 的标准化横向位置</param>
        /// <param name="pathProgress">沿路径方向的归一化进度（0 = 起点，1 = 终点）</param>
        /// <param name="worldWidth">当前位置的道路宽度 (米)</param>
        /// <param name="pathLength">路径总长度 (米)</param>
        /// <returns>遮罩强度 (0..1)</returns>
        public abstract float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength);

        /// <summary>
        /// 兼容旧接口：未传入 pathProgress 时默认使用 0.5（路径中点）。
        /// </summary>
        public virtual float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            return Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);
        }

        #endregion

        #region Smoothing Helper

        /// <summary>
        /// 对输入值先乘以 overallScale，然后按 smooth 参数做边缘平滑，最终返回 0..1 区间的结果。
        /// </summary>
        protected float ApplySmoothing(float maskValue)
        {
            // 1. 先应用整体缩放
            maskValue *= overallScale;

            // 2. 若无需平滑，直接返回裁剪后的值
            if (smooth <= 0f) return Mathf.Clamp01(maskValue);

            // 3. 使用 smoothstep 进行边缘平滑
            float edge0 = smooth * 0.5f;
            float edge1 = 1f - smooth * 0.5f;

            if (maskValue <= edge0) return 0f;
            if (maskValue >= edge1) return 1f;

            float t = (maskValue - edge0) / (edge1 - edge0); // 0..1
            float smoothed = t * t * (3f - 2f * t);
            return Mathf.Clamp01(smoothed);
        }

        #endregion
    }
}