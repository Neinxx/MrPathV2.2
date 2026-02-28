using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MrPathV2
{
    /// <summary>
    /// 编辑器刷新管理器，提供更鲁棒的刷新机制
    /// </summary>
    public class EditorRefreshManager : IDisposable
    {
        private readonly Dictionary<string, float> _lastRefreshTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, Action> _pendingRefreshActions = new Dictionary<string, Action>();
        private readonly float _minRefreshInterval = 0.1f; // 最小刷新间隔100ms
        private bool _disposed = false;

        public EditorRefreshManager()
        {
            EditorApplication.update += ProcessPendingRefreshes;

#if UNITY_EDITOR
            // 在编辑器退出 / 重新加载脚本时自动清理，避免在终结器中捕获未释放情况。
            if (!_registeredForDisposeEvents)
            {
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                EditorApplication.quitting += OnEditorQuitting;
                _registeredForDisposeEvents = true;
            }
#endif
        }

#if UNITY_EDITOR
        // 标记是否已注册事件，防止重复注册
        private static bool _registeredForDisposeEvents = false;

        private void OnBeforeAssemblyReload()
        {
            Dispose();
        }

        private void OnEditorQuitting()
        {
            Dispose();
        }
#endif

        /// <summary>
        /// 请求刷新操作，带有防抖动机制
        /// </summary>
        /// <param name="key">刷新操作的唯一标识</param>
        /// <param name="refreshAction">刷新操作</param>
        /// <param name="forceImmediate">是否强制立即执行</param>
        public void RequestRefresh(string key, Action refreshAction, bool forceImmediate = false)
        {
            if (_disposed || refreshAction == null)
                return;

            float currentTime = (float)EditorApplication.timeSinceStartup;

            if (forceImmediate)
            {
                // 立即执行并更新时间戳
                try
                {
                    refreshAction.Invoke();
                    _lastRefreshTimes[key] = currentTime;
                    _pendingRefreshActions.Remove(key);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Editor refresh failed for key '{key}': {ex.Message}");
                }
                return;
            }

            // 检查是否需要防抖动
            if (_lastRefreshTimes.TryGetValue(key, out float lastTime))
            {
                if (currentTime - lastTime < _minRefreshInterval)
                {
                    // 在防抖动期间，只更新待执行的操作
                    _pendingRefreshActions[key] = refreshAction;
                    return;
                }
            }

            // 可以立即执行
            try
            {
                refreshAction.Invoke();
                _lastRefreshTimes[key] = currentTime;
                _pendingRefreshActions.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Editor refresh failed for key '{key}': {ex.Message}");
            }
        }

        /// <summary>
        /// 取消待执行的刷新操作
        /// </summary>
        public void CancelRefresh(string key)
        {
            _pendingRefreshActions.Remove(key);
        }

        /// <summary>
        /// 清除所有待执行的刷新操作
        /// </summary>
        public void ClearAllPendingRefreshes()
        {
            _pendingRefreshActions.Clear();
        }

        /// <summary>
        /// 获取待执行刷新操作的数量
        /// </summary>
        public int GetPendingRefreshCount()
        {
            return _pendingRefreshActions.Count;
        }

        /// <summary>
        /// 请求刷新Inspector
        /// </summary>
        public void RequestInspectorRefresh()
        {
            RequestRefresh("inspector_refresh", () =>
            {
                EditorUtility.SetDirty(Selection.activeObject);
            });
        }

        private void ProcessPendingRefreshes()
        {
            if (_disposed || _pendingRefreshActions.Count == 0)
                return;

            float currentTime = (float)EditorApplication.timeSinceStartup;
            var keysToProcess = new List<string>();

            // 找出可以执行的待执行操作
            foreach (var kvp in _pendingRefreshActions)
            {
                string key = kvp.Key;
                if (_lastRefreshTimes.TryGetValue(key, out float lastTime))
                {
                    if (currentTime - lastTime >= _minRefreshInterval)
                    {
                        keysToProcess.Add(key);
                    }
                }
                else
                {
                    keysToProcess.Add(key);
                }
            }

            // 执行待执行的操作
            foreach (string key in keysToProcess)
            {
                if (_pendingRefreshActions.TryGetValue(key, out Action action))
                {
                    try
                    {
                        action.Invoke();
                        _lastRefreshTimes[key] = currentTime;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Pending editor refresh failed for key '{key}': {ex.Message}");
                    }
                    finally
                    {
                        _pendingRefreshActions.Remove(key);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                EditorApplication.update -= ProcessPendingRefreshes;
#if UNITY_EDITOR
                if (_registeredForDisposeEvents)
                {
                    AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
                    EditorApplication.quitting -= OnEditorQuitting;
                    _registeredForDisposeEvents = false;
                }
#endif
                _pendingRefreshActions.Clear();
                _lastRefreshTimes.Clear();
                _disposed = true;
            }
        }

        ~EditorRefreshManager()
        {
            if (!_disposed)
            {
                Debug.LogWarning("EditorRefreshManager was not properly disposed!");
                Dispose();
            }
        }
    }

    /// <summary>
    /// 编辑器刷新管理器的静态访问点
    /// </summary>
    public static class EditorRefresh
    {
        private static EditorRefreshManager _instance;

        public static EditorRefreshManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new EditorRefreshManager();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 在编辑器重新编译时清理资源
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                _instance?.Dispose();
                _instance = null;
            };
        }
    }
}