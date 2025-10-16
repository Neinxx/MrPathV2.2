using Sirenix.OdinInspector;
using UnityEngine;

namespace MrPathV2
{
    public abstract class BlendMaskBase : ScriptableObject
    {
        /// <summary>
        /// 计算遮罩在某个位置的强度。
        /// </summary>
        /// <param name="horizontalPosition">-1 (左) 到 1 (右) 的标准化位置</param>
        /// <param name="worldWidth">该位置所在的区域所代表的真实世界宽度 (米)</param>
        /// <returns>强度值 (0 到 1)</returns>
        public abstract float Evaluate(float horizontalPosition, float worldWidth);
    }
}