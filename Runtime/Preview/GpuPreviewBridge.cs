#if !UNITY_EDITOR
using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Preview
{
    // Runtime stub: always returns false, avoids editor assembly reference in runtime build
    internal static class GpuPreviewBridge
    {
        public static bool TryGet(Terrain terrain, out RenderTexture rt)
        {
            rt = null;
            return false;
        }
    }
}
#endif
