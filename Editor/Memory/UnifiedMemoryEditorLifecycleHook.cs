#if UNITY_EDITOR
using MrPathV2.Runtime.Memory;
using UnityEditor;

namespace MrPathV2.Editor.Memory
{
    /// <summary>
    ///     Ensures UnifiedMemoryManager is disposed during editor domain reloads and on editor quit.
    /// </summary>
    public static class UnifiedMemoryEditorLifecycleHook
    {
        [InitializeOnLoadMethod]
        private static void Init()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                UnifiedMemory.Instance.Dispose();
            }
            catch
            {
                // best-effort cleanup; ignore errors
            }
        }

        private static void OnEditorQuitting()
        {
            try
            {
                UnifiedMemory.Instance.Dispose();
            }
            catch
            {
                // best-effort cleanup; ignore errors
            }
        }
    }
}
#endif
