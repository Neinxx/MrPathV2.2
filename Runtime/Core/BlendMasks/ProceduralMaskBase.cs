using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    public abstract class ProceduralMaskBase : BlendMaskBase
    {
        [Range(0, 1)]
        public float strength = 1.0f;

        [Tooltip("一个随机种子，用于在其他参数相同时，也能获得不同的噪声形状")]
        public float seed;

        // Procedural masks可能需要覆盖 TransformPosition/TransformPathPosition 进行更复杂的UV映射，
        // 默认继承自 BlendMaskBase 的实现即可。
    }
}
