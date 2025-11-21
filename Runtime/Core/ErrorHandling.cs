using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
// 用于 .ToArray()
using Object = UnityEngine.Object;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     错误级别枚举
    /// </summary>
    public enum ErrorLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    ///     错误信息结构
    /// </summary>
    [Serializable]
    public struct ErrorInfo
    {
        public ErrorLevel level;
        public string message;
        public string context;
        public Object source;
        public DateTime Timestamp;
        public string stackTrace;

        public ErrorInfo(ErrorLevel level, string message, string context = null, Object source = null)
        {
            this.level = level;
            this.message = message;
            this.context = context;
            this.source = source;
            Timestamp = DateTime.Now;
            // 优化：只在真正需要时才获取慢速的堆栈跟踪
            stackTrace = level >= ErrorLevel.Error ? Environment.StackTrace : null;
        }

        public override string ToString()
        {
            var contextStr = !string.IsNullOrEmpty(context) ? $"[{context}] " : "";
            var sourceStr = source != null ? $" (Source: {source.name})" : "";
            return $"{contextStr}{message}{sourceStr}";
        }
    }

    /// <summary>
    ///     统一的错误处理和用户反馈系统
    ///     提供集中化的错误管理、日志记录和用户通知功能
    /// </summary>
    public static class ErrorHandler
    {
        private const int MaxHistorySize = 100;
        private static readonly Queue<ErrorInfo> ErrorHistory = new Queue<ErrorInfo>();

        /// <summary>
        ///     [新增] 用于 O(1) 计数的数组。
        /// </summary>
        private static readonly int[] ErrorCounts = new int[Enum.GetValues(typeof(ErrorLevel)).Length];

        /// <summary>
        ///     [新增] 用于线程安全的锁对象。
        /// </summary>
        private static readonly object HistoryLock = new object();

        // --- 新增：最佳实践 ---

        /// <summary>
        ///     [新增] 错误处理的总开关。
        ///     如果设为 false，所有日志调用都将被立即忽略。
        /// </summary>
        public static bool IsEnabled { get; set; } = true;

        /// <summary>
        ///     [新增] 要记录的最低错误级别。
        ///     默认为 Info（记录所有内容）。
        ///     如果设为 Error，则 Info 和 Warning 将被忽略。
        /// </summary>
        public static ErrorLevel MinLogLevel { get; set; } = ErrorLevel.Info;

        // -------------------------

        /// <summary>
        ///     错误发生时的事件
        /// </summary>
        public static event Action<ErrorInfo> OnError;

        /// <summary>
        ///     记录信息级别的消息
        /// </summary>
        public static void LogInfo(string message, string context = null, Object source = null)
        {
            // 优化：在创建任何对象或执行任何操作之前，先检查开关和级别
            if (!IsEnabled || ErrorLevel.Info < MinLogLevel) return;

            var errorInfo = new ErrorInfo(ErrorLevel.Info, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        ///     记录警告级别的消息
        /// </summary>
        public static void LogWarning(string message, string context = null, Object source = null)
        {
            if (!IsEnabled || ErrorLevel.Warning < MinLogLevel) return;

            var errorInfo = new ErrorInfo(ErrorLevel.Warning, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        ///     记录错误级别的消息
        /// </summary>
        public static void LogError(string message, string context = null, Object source = null)
        {
            if (!IsEnabled || ErrorLevel.Error < MinLogLevel) return;

            var errorInfo = new ErrorInfo(ErrorLevel.Error, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        ///     记录严重错误级别的消息
        /// </summary>
        public static void LogCritical(string message, string context = null, Object source = null)
        {
            if (!IsEnabled || ErrorLevel.Critical < MinLogLevel) return;

            var errorInfo = new ErrorInfo(ErrorLevel.Critical, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        ///     记录异常
        /// </summary>
        public static void LogException(Exception exception, string context = null, Object source = null)
        {
            // 异常总是至少为 Error 级别
            if (!IsEnabled || ErrorLevel.Error < MinLogLevel) return;

            var message = $"Exception: {exception.Message}";
            var errorInfo = new ErrorInfo(ErrorLevel.Error, message, context, source)
            {
                // 优化：使用异常中已有的堆栈跟踪，而不是 Environment.StackTrace
                stackTrace = exception.StackTrace
            };
            ProcessError(errorInfo);
        }

        /// <summary>
        ///     处理错误信息
        /// </summary>
        private static void ProcessError(ErrorInfo errorInfo)
        {
            // 注意：开关检查已移至公共 Log... 方法中，以获得更好的性能

            // 添加到历史记录 (内部已加锁)
            AddToHistory(errorInfo);

            // 输出到Unity控制台 (Debug.Log 本身是线程安全的)
            LogToUnityConsole(errorInfo);

            // 触发事件通知 (在锁之外触发，防止死锁)
            try
            {
                OnError?.Invoke(errorInfo);
            }
            catch (Exception ex)
            {
                // 防止事件处理器中的异常导致无限循环
                Debug.LogError($"[ErrorHandler] Exception in error event handler: {ex.Message}");
            }

#if UNITY_EDITOR
            // 编辑器下的特殊处理 (内部已处理线程安全)
            HandleEditorError(errorInfo);
#endif
        }

        /// <summary>
        ///     添加到错误历史记录
        /// </summary>
        private static void AddToHistory(ErrorInfo errorInfo)
        {
            // 优化：添加线程安全锁
            lock (HistoryLock)
            {
                ErrorHistory.Enqueue(errorInfo);
                ErrorCounts[(int)errorInfo.level]++; // 优化：O(1) 计数

                // 保持历史记录大小限制
                while (ErrorHistory.Count > MaxHistorySize)
                {
                    var removedInfo = ErrorHistory.Dequeue();
                    ErrorCounts[(int)removedInfo.level]--; // 优化：O(1) 计数
                }
            }
        }

        // ReSharper disable Unity.PerformanceAnalysis
        /// <summary>
        ///     输出到Unity控制台
        /// </summary>
        private static void LogToUnityConsole(ErrorInfo errorInfo)
        {
            var message = errorInfo.ToString();

            switch (errorInfo.level)
            {
                case ErrorLevel.Info:
                    Debug.Log(message, errorInfo.source);
                    break;
                case ErrorLevel.Warning:
                    Debug.LogWarning(message, errorInfo.source);
                    break;
                case ErrorLevel.Error:
                case ErrorLevel.Critical:
                    Debug.LogError(message, errorInfo.source);
                    break;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        ///     编辑器下的错误处理
        /// </summary>
        private static void HandleEditorError(ErrorInfo errorInfo)
        {
            if (errorInfo.level == ErrorLevel.Critical)
            {
                // [修正] 使用 EditorApplication.delayCall
                // 确保在下一次编辑器 update 时在主线程上安全地调用弹窗
                EditorApplication.delayCall += () =>
                {
                    EditorUtility.DisplayDialog(
                        "MrPath Critical Error",
                        errorInfo.message,
                        "OK"
                    );
                };
            }
        }
#endif

        /// <summary>
        ///     获取错误历史记录
        /// </summary>
        public static ErrorInfo[] GetErrorHistory()
        {
            // 优化：添加线程安全锁
            lock (HistoryLock)
            {
                return ErrorHistory.ToArray();
            }
        }

        /// <summary>
        ///     清除错误历史记录
        /// </summary>
        public static void ClearHistory()
        {
            // 优化：添加线程安全锁
            lock (HistoryLock)
            {
                ErrorHistory.Clear();
                // 优化：重置 O(1) 计数器
                Array.Clear(ErrorCounts, 0, ErrorCounts.Length);
            }
        }

        /// <summary>
        ///     获取指定级别的错误数量 (O(1) 操作)
        /// </summary>
        public static int GetErrorCount(ErrorLevel level)
        {
            // 优化：使用 O(1) 计数器并添加锁
            lock (HistoryLock)
            {
                return ErrorCounts[(int)level];
            }
        }

        /// <summary>
        ///     检查是否有指定级别或更高级别的错误 (近 O(1) 操作)
        /// </summary>
        public static bool HasErrors(ErrorLevel minLevel = ErrorLevel.Error)
        {
            // 优化：迭代计数器数组（非常快），而不是整个历史队列
            lock (HistoryLock)
            {
                // 从最小级别开始检查到最高级别
                for (var i = (int)minLevel; i < ErrorCounts.Length; i++)
                {
                    if (ErrorCounts[i] > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        ///     安全执行操作，捕获并记录异常
        /// </summary>
        public static bool SafeExecute(Action action, string context = null, Object source = null)
        {
            try
            {
                // 即使 IsEnabled = false，SafeExecute 也应该执行
                // 它的主要职责是“安全执行”，而不是“日志记录”
                action?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                // LogException 内部会检查 IsEnabled 和 MinLogLevel
                LogException(ex, context, source);
                return false;
            }
        }

        /// <summary>
        ///     安全执行带返回值的操作，捕获并记录异常
        /// </summary>
        public static T SafeExecute<T>(Func<T> func, T defaultValue = default, string context = null, Object source = null)
        {
            try
            {
                return func != null ? func() : defaultValue;
            }
            catch (Exception ex)
            {
                LogException(ex, context, source);
                return defaultValue;
            }
        }
    }

    /// <summary>
    ///     错误处理扩展方法 (保持不变)
    /// </summary>
    public static class ErrorHandlerExtensions
    {
        public static void LogInfo(this Object obj, string message, string context = null)
        {
            ErrorHandler.LogInfo(message, context, obj);
        }

        public static void LogWarning(this Object obj, string message, string context = null)
        {
            ErrorHandler.LogWarning(message, context, obj);
        }

        public static void LogError(this Object obj, string message, string context = null)
        {
            ErrorHandler.LogError(message, context, obj);
        }

        public static void LogCritical(this Object obj, string message, string context = null)
        {
            ErrorHandler.LogCritical(message, context, obj);
        }

        public static void LogException(this Object obj, Exception exception, string context = null)
        {
            ErrorHandler.LogException(exception, context, obj);
        }
    }
}
