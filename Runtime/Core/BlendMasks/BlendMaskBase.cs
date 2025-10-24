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
        [LabelText("Tiling (X=横向重复, Y=纵向重复)")]
        [Tooltip("以重复次数为语义：横向/纵向的重复次数；允许负值实现镜像/翻转。")]
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
        /// 将水平位置 (-1..1) 映射为以左边缘为原点的 0..1，并按重复次数 tiling.x 与 offset.x 计算 U。
        /// 支持负 tiling 实现镜像/翻转；缩放与平铺的枢轴为路面网格的左下角。
        /// </summary>
        protected float TransformPosition(float horizontalPosition, float worldWidth)
        {
            // 以重复次数为语义：-1..1 映射到 0..1（左到右），按 tiling.x 次重复
            float u01 = (horizontalPosition + 1f) * 0.5f;
            float repeatX = Mathf.Abs(tiling.x) < 1e-4f 
                ? 1e-4f * Mathf.Sign(tiling.x == 0 ? 1f : tiling.x) 
                : tiling.x;
            return u01 * repeatX + offset.x;
        }

        /// <summary>
        /// 向后兼容：包含 pathLength 参数的重载，占位以保持旧代码兼容。
        /// </summary>
        protected float TransformPosition(float horizontalPosition, float worldWidth, float pathLength)
        {
            return TransformPosition(horizontalPosition, worldWidth);
        }

        /// <summary>
        /// 计算沿路径方向 (0..1) 的 V，并按重复次数 tiling.y 与 offset.y。
        /// 支持负 tiling 以产生镜像效果；不再乘以路径长度（米）。
        /// </summary>
        protected float TransformPathPosition(float pathProgress, float pathLength)
        {
            // 以重复次数为语义：0..1 路径进度按 tiling.y 次重复，不再乘以路径长度
            float repeatY = Mathf.Abs(tiling.y) < 1e-4f ? 1e-4f * Mathf.Sign(tiling.y == 0 ? 1f : tiling.y) : tiling.y;
            return pathProgress * repeatY + offset.y;
        }

        /// <summary>
        /// 对遮罩值应用整体缩放（强度）。
        /// </summary>
        protected float ApplyScale(float value) => value * overallScale;

        /// <summary>
        /// 应用整体缩放与边缘平滑，返回 0..1 区间值。
        /// </summary>
        protected float ApplySmoothing(float maskValue)
        {
            // 先应用整体缩放
            maskValue *= overallScale;

            // 无需平滑时直接裁剪
            if (smooth <= 0f) return Mathf.Clamp01(maskValue);

            // 使用 smoothstep 风格的边缘软化
            float edge0 = smooth * 0.5f;
            float edge1 = 1f - smooth * 0.5f;

            if (maskValue <= edge0) return 0f;
            if (maskValue >= edge1) return 1f;

            float t = (maskValue - edge0) / (edge1 - edge0); // 0..1
            float smoothed = t * t * (3f - 2f * t);
            return Mathf.Clamp01(smoothed);
        }

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

        // --- GPU 参数打包：统一接口 ---
        /// <summary>
        /// 将当前遮罩的参数打包到通用 GPU DTO（Editor/Runtime 再各自转换为实际 GPU 结构体）。
        /// 默认实现输出一个“无遮罩”占位，派生类需覆盖并写入各自字段。
        /// </summary>
        public virtual void FillGpuParams(ref __temp.MrPathV2._2.Runtime.Core.GpuMaskParamsData dst)
        {
            dst.MaskType = 0;            // MASK_TYPE_NONE
            dst.Strength = 1f;
            // 通用 UV 参数留给具体遮罩写入（tiling/offset/overallScale/smooth）
            // 派生类会填充 dst.NoiseParams 或 dst.ShoulderParams 等
        }
    }
}