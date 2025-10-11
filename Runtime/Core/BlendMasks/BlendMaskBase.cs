using UnityEngine;

namespace MrPathV2
{
    // 所有笔刷都必须能计算出在某个横向位置(-1 to 1)的强度
    public abstract class BlendMaskBase : ScriptableObject
    {
        public abstract float Evaluate(float horizontalPosition);
    }
}