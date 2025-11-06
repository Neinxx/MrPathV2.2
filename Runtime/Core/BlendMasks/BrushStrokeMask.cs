using MrPathV2.Runtime.Core.Noise;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    /// <summary>
    ///     笔触条纹遮罩：沿路径方向以条纹/抖动模拟手绘笔触。支持 pathProgress 维度调制。
    /// </summary>
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Brush Stroke Mask")]
    public class BrushStrokeMask : ProceduralMaskBase
    {
        [Header("Brush Pattern")]
        [Tooltip("横向条纹的周期（单位：重复次数/宽度），值越大越密集")]
        public float stripeFrequency = 6f;

        [Tooltip("条纹厚度（0..1），越大越宽")]
        [Range(0f, 1f)] public float stripeThickness = 0.35f;

        [Tooltip("条纹边缘抖动强度（0..1）")]
        [Range(0f, 1f)] public float jitterStrength = 0.2f;

        [Tooltip("沿路径方向的强度渐隐，0=不变，1=从头到尾完全淡出")]
        [Range(0f, 1f)] public float fadeAlongPath;

        [Tooltip("噪声平铺（用于抖动与条纹扰动）")]
        public Vector2 noiseTiling = new Vector2(2f, 1f);

        [Tooltip("噪声旋转（度）")]
        [Range(-180f, 180f)] public float noiseRotationDeg;

        public override bool SupportsGpu => false; // CPU-only 首版

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            // 基础 UV 变换：沿横向重复条纹，沿路径用于淡出
            var u = TransformPosition(horizontalPosition, worldWidth);
            var v = TransformPathPosition(pathProgress, pathLength);

            // 条纹基波：cos 形成 0..1 带宽度的条纹
            var angle = u * Mathf.PI * stripeFrequency * 2.0f;

            // 简易噪声扰动：使用 Unity 的 PerlinNoise 作为抖动（0..1）
            var rotRad = noiseRotationDeg * Mathf.Deg2Rad;
            var rot = new Vector2(Mathf.Cos(rotRad), Mathf.Sin(rotRad));
            var nu = u * noiseTiling.x * rot.x - v * noiseTiling.y * rot.y + seed * 0.123f;
            var nv = u * noiseTiling.x * rot.y + v * noiseTiling.y * rot.x + seed * 0.789f;
            var noise = NoiseLutProvider.Sample01(nu, nv); // 0..1

            // 抖动作用到条纹相位与厚度
            var jitterPhase = (noise - 0.5f) * jitterStrength * Mathf.PI; // 相位扰动
            var thickness = Mathf.Clamp01(stripeThickness + (noise - 0.5f) * jitterStrength * 0.5f);

            var wave = Mathf.Cos(angle + jitterPhase);
            // 将 cos 映射为条纹脉冲：|cos| 的反相阈值控制宽度
            var band = Mathf.InverseLerp(1f, 1f - thickness * 2f, Mathf.Abs(wave));

            // 沿路径渐隐
            var fade = Mathf.Lerp(1f, 0f, fadeAlongPath * pathProgress);

            var value = band * fade * strength;
            return ApplySmoothing(Mathf.Clamp01(value));
        }
    }
}
