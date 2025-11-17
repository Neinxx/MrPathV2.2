using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    public abstract class RegionSelectorSO : ScriptableObject
    {
        public abstract float Weight(float horizontalPosition, float worldWidth);
        public virtual void FillGpu(ref GpuMaskParamsData dst) { }
    }
}
