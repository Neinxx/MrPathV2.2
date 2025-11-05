using UnityEngine;

namespace __temp.MrPathV2.Runtime.Core.BlendMasks
{
    /// <summary>
    ///     纹理采样遮罩：直接采样灰度纹理，用于外部控制路肩纹理与道路轮廓。
    ///     支持世界宽度对齐的平铺与偏移。
    /// </summary>
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Texture Mask")]
    public class TextureMask : BlendMaskBase
    {
        [Tooltip("灰度纹理，R 通道或者 L 通道作为遮罩强度来源")]
        public Texture2D grayscale;

        [Tooltip("当纹理缺失时的默认值（0..1）")]
        [Range(0f,1f)] public float fallback = 1f;

        [Tooltip("是否使用双线性采样（false=点采样）")]
        public bool bilinear = true;

        public override bool SupportsGpu => false; // 先提供 CPU 版本；GPU 需采样纹理阵列

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            if (grayscale == null || grayscale.width == 0 || grayscale.height == 0)
                return ApplySmoothing(Mathf.Clamp01(fallback));
            if (!grayscale.isReadable)
                return ApplySmoothing(Mathf.Clamp01(fallback));

            var u = TransformPosition(horizontalPosition, worldWidth);
            var v = TransformPathPosition(pathProgress, pathLength);

            // 重复采样
            var u01 = Mathf.Repeat(u, 1f);
            var v01 = Mathf.Repeat(v, 1f);

            // 纹理读取：Editor/Runtime 都可从 CPU 读取，注意纹理可读性
            var px = u01 * (grayscale.width - 1);
            var py = v01 * (grayscale.height - 1);
            Color c;
            if (bilinear)
            {
                c = grayscale.GetPixelBilinear(u01, v01);
            }
            else
            {
                c = grayscale.GetPixel(Mathf.RoundToInt(px), Mathf.RoundToInt(py));
            }

            var value = c.grayscale; // 使用灰度
            return ApplySmoothing(Mathf.Clamp01(value));
        }
    }
}