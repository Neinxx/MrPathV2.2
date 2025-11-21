using System;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    public static class MaskChangeEvents
    {
        public static event Action<BlendMaskBase> Changed;

        public static void RaiseChanged(BlendMaskBase mask)
        {
            Changed?.Invoke(mask);
        }
    }
}
