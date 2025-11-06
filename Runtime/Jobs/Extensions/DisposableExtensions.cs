using System;
using UnityEngine;

namespace MrPathV2.Runtime.Jobs.Extensions
{
    /// <summary>
    ///     通用的 IDisposable 安全释放扩展，统一 try/catch 并在 Editor/Dev 环境下做轻量日志。
    /// </summary>
    public static class DisposableExtensions
    {
        public static void SafeDispose(this IDisposable disposable)
        {
            if (disposable == null) return;
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                try
                {
                    Debug.LogWarning($"释放 {disposable.GetType().Name} 时发生异常(可能已释放): {ex.Message}");
                }
                catch
                { /* ignore logging errors */
                }
#endif
            }
        }
    }
}
