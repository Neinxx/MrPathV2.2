#if ENABLE_UNITY_COLLECTIONS_CHECKS
using Unity.Collections;
using UnityEngine.Scripting;

namespace MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    ///     Configures Unity's NativeLeakDetection system to provide full stack traces for leaked Native Collections.
    ///     This allows easier identification of disposal issues during development and testing.
    ///     The static constructor runs once on domain load before any NativeCollection allocations occur.
    /// </summary>
    [Preserve]
    static class NativeLeakDetectionConfig
    {
        // Static constructor executes automatically on domain load.
        static NativeLeakDetectionConfig()
        {
            // Enable detailed leak detection with stack traces.
            // This setting has no effect in player builds where leak detection is disabled.
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        }
    }
}
#endif
