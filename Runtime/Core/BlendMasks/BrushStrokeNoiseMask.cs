// 文件路径: MrPathV2/Blend Masks/BrushStrokeNoiseMask.cs
using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Brush Stroke Noise")]
    public class BrushStrokeNoiseMask : BlendMaskBase
    {
        [Header("笔刷密度")]
        [Tooltip("控制笔触的密集程度。值越大，笔触越密集。")]
        public float scale = 5f;

        [Header("笔刷属性")]
        [Tooltip("笔触的平均强度或'颜料厚度'。")]
        [Range(0, 1)] public float strength = 1f;

        [Tooltip("笔触强度的随机变化范围。0表示所有笔触强度相同。")]
        [Range(0, 1)] public float strengthVariation = 0.5f;
        
        [Tooltip("笔触的平均宽度。")]
        [Range(0.1f, 2f)] public float strokeWidth = 0.8f;
        
        [Tooltip("笔触宽度的随机变化范围。")]
        [Range(0, 1)] public float widthVariation = 0.7f;
        
        [Tooltip("笔触位置的随机抖动程度。0表示笔触均匀排列。")]
        [Range(0, 1)] public float jitter = 1.0f;

        public override float Evaluate(float horizontalPosition)
        {
            float p = horizontalPosition * Mathf.Max(0.0001f, scale);
            
            // 确定当前位置所在的“格子”
            float cellIndex = Mathf.Floor(p);

            float finalValue = 0f;

            // 检查当前格子和相邻的格子(-1, 0, 1)，因为一个宽笔触可能会影响到邻近区域
            for (int i = -1; i <= 1; i++)
            {
                float currentCell = cellIndex + i;

                // --- 为每个格子生成一个独一无二、但始终不变的笔触 ---
                // 1. 随机化笔触的宽度
                float randomWidth = strokeWidth * (1f + (Hash(currentCell, 10.5f) - 0.5f) * 2f * widthVariation);
                
                // 2. 随机化笔触的强度
                float randomStrength = strength * (1f + (Hash(currentCell, 20.3f) - 0.5f) * 2f * strengthVariation);
                
                // 3. 随机化笔触在格子内的位置
                float randomOffset = (Hash(currentCell, 30.1f) - 0.5f) * jitter;
                float strokeCenter = currentCell + 0.5f + randomOffset;

                // --- 计算当前点受这个笔触的影响程度 ---
                // 计算点到笔触中心的距离
                float dist = Mathf.Abs(p - strokeCenter);
                
                // 笔触的边缘应该是平滑过渡的。我们使用 SmoothStep 函数来创建一个漂亮的钟形曲线，模拟笔刷的剖面。
                // 当距离小于笔触半径时，开始计算影响值。
                float halfWidth = randomWidth / 2f;
                if (dist < halfWidth)
                {
                    // SmoothStep(edge1, edge0, x) 会在 x 从 edge0 过渡到 edge1 时，平滑地从 1 降到 0
                    float strokeValue = SmoothStep(halfWidth, 0, dist);
                    
                    // 我们取所有重叠笔触中的最大值，模拟厚涂颜料覆盖的效果
                    finalValue = Mathf.Max(finalValue, strokeValue * randomStrength);
                }
            }

            return Mathf.Clamp01(finalValue);
        }

        // 一个简单的伪随机哈希函数，加一个'salt'参数可以从同一个输入得到不同的随机结果
        private float Hash(float n, float salt)
        {
            // 使用三角函数和大的无理数来产生看似随机的结果
            return Mathf.Abs((Mathf.Sin(n * 12.9898f + salt * 53.123f) * 43758.5453f) % 1.0f);
        }
        
        // 自定义 SmoothStep 实现，Unity 的 Mathf.SmoothStep 是从 0 到 1
        private float SmoothStep(float edge1, float edge0, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3.0f - 2.0f * t);
        }
    }
}