using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks.Composition
{
    public abstract class ModulatorSO : ScriptableObject
    {
        public virtual float ApplyCpu(float value) { return value; }
        public virtual void ApplyGpuNoise(ref GpuMaskParamsData dst) { }
        public virtual void ApplyGpuShoulder(ref GpuMaskParamsData dst) { }
        protected virtual void OnValidate()
        {
            MaskChangeEvents.RaiseChanged(null);
        }
    }
}
