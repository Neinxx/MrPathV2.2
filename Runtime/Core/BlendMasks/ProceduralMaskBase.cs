

using Sirenix.OdinInspector;
using UnityEngine;

namespace MrPathV2
{
    public abstract class ProceduralMaskBase : BlendMaskBase
    {
        [BoxGroup("Noise Settings")]
        [Range(0, 1)]
        public float strength = 1.0f;

        [BoxGroup("Noise Settings")]
        [SuffixLabel("meters")] 
        [LabelText("World Size")]  
        [MinValue(0.1)]
       
        public Vector2 tiling = new Vector2(2, 2); 

        [BoxGroup("Noise Settings")]
        public Vector2 offset = Vector2.zero;

        [BoxGroup("Noise Settings")]
        [Tooltip("一个随机种子，用于在其他参数相同时，也能获得不同的噪声形状")]
        public float seed = 0.0f;

        
        protected float TransformPosition(float horizontalPosition, float worldWidth)
        {
            // 1. 将输入的 -1 to 1 范围映射到 0 to 1
            float u = (horizontalPosition + 1f) * 0.5f;

            // 2. 【核心逻辑】根据世界尺寸计算正确的重复次数
            //    例如：世界宽度10米，噪声尺寸2米，则需要重复 10 / 2 = 5 次
            float repeatCount = worldWidth / this.tiling.x;

            // 3. 将重复次数应用到 UV 坐标上，并加上偏移
            return (u * repeatCount) + this.offset.x;
        }
    }
}