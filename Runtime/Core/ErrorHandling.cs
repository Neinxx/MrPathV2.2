using UnityEngine;
using System;
using System.Collections.Generic;

namespace MrPathV2
{
    /// <summary>
    /// 错误级别枚举
    /// </summary>
    public enum ErrorLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// 错误信息结构
    /// </summary>
    [Serializable]
    public struct ErrorInfo
    {
        public ErrorLevel level;
        public string message;
        public string context;
        public UnityEngine.Object source;
        public DateTime timestamp;
        public string stackTrace;

        public ErrorInfo(ErrorLevel level, string message, string context = null, UnityEngine.Object source = null)
        {
            this.level = level;
            this.message = message;
            this.context = context;
            this.source = source;
            this.timestamp = DateTime.Now;
            this.stackTrace = level >= ErrorLevel.Error ? Environment.StackTrace : null;
        }

        public override string ToString()
        {
            var contextStr = !string.IsNullOrEmpty(context) ? $"[{context}] " : "";
            var sourceStr = source != null ? $" (Source: {source.name})" : "";
            return $"{contextStr}{message}{sourceStr}";
        }
    }

    /// <summary>
    /// 统一的错误处理和用户反馈系统
    /// 提供集中化的错误管理、日志记录和用户通知功能
    /// </summary>
    public static class ErrorHandler
    {
        private static readonly Queue<ErrorInfo> _errorHistory = new Queue<ErrorInfo>();
        private static readonly int MaxHistorySize = 100;

        /// <summary>
        /// 错误发生时的事件
        /// </summary>
        public static event Action<ErrorInfo> OnError;

        /// <summary>
        /// 记录信息级别的消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        public static void LogInfo(string message, string context = null, UnityEngine.Object source = null)
        {
            var errorInfo = new ErrorInfo(ErrorLevel.Info, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        /// 记录警告级别的消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        public static void LogWarning(string message, string context = null, UnityEngine.Object source = null)
        {
            var errorInfo = new ErrorInfo(ErrorLevel.Warning, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        /// 记录错误级别的消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        public static void LogError(string message, string context = null, UnityEngine.Object source = null)
        {
            var errorInfo = new ErrorInfo(ErrorLevel.Error, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        /// 记录严重错误级别的消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        public static void LogCritical(string message, string context = null, UnityEngine.Object source = null)
        {
            var errorInfo = new ErrorInfo(ErrorLevel.Critical, message, context, source);
            ProcessError(errorInfo);
        }

        /// <summary>
        /// 记录异常
        /// </summary>
        /// <param name="exception">异常对象</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        public static void LogException(Exception exception, string context = null, UnityEngine.Object source = null)
        {
            var message = $"Exception: {exception.Message}";
            var errorInfo = new ErrorInfo(ErrorLevel.Error, message, context, source)
            {
                stackTrace = exception.StackTrace
            };
            ProcessError(errorInfo);
        }

        /// <summary>
        /// 处理错误信息
        /// </summary>
        /// <param name="errorInfo">错误信息</param>
        private static void ProcessError(ErrorInfo errorInfo)
        {
            // 添加到历史记录
            AddToHistory(errorInfo);

            // 输出到Unity控制台
            LogToUnityConsole(errorInfo);

            // 触发事件通知
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
            // 编辑器下的特殊处理
            HandleEditorError(errorInfo);
#endif
        }

        /// <summary>
        /// 添加到错误历史记录
        /// </summary>
        /// <param name="errorInfo">错误信息</param>
        private static void AddToHistory(ErrorInfo errorInfo)
        {
            _errorHistory.Enqueue(errorInfo);
            
            // 保持历史记录大小限制
            while (_errorHistory.Count > MaxHistorySize)
            {
                _errorHistory.Dequeue();
            }
        }

        /// <summary>
        /// 输出到Unity控制台
        /// </summary>
        /// <param name="errorInfo">错误信息</param>
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
        /// 编辑器下的错误处理
        /// </summary>
        /// <param name="errorInfo">错误信息</param>
        private static void HandleEditorError(ErrorInfo errorInfo)
        {
            // 严重错误时显示对话框
            if (errorInfo.level == ErrorLevel.Critical)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "MrPath Critical Error",
                    errorInfo.message,
                    "OK"
                );
            }
        }
#endif

        /// <summary>
        /// 获取错误历史记录
        /// </summary>
        /// <returns>错误历史记录数组</returns>
        public static ErrorInfo[] GetErrorHistory()
        {
            return _errorHistory.ToArray();
        }

        /// <summary>
        /// 清除错误历史记录
        /// </summary>
        public static void ClearHistory()
        {
            _errorHistory.Clear();
        }

        /// <summary>
        /// 获取指定级别的错误数量
        /// </summary>
        /// <param name="level">错误级别</param>
        /// <returns>错误数量</returns>
        public static int GetErrorCount(ErrorLevel level)
        {
            int count = 0;
            foreach (var error in _errorHistory)
            {
                if (error.level == level)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 检查是否有指定级别或更高级别的错误
        /// </summary>
        /// <param name="minLevel">最小错误级别</param>
        /// <returns>如果有错误返回true</returns>
        public static bool HasErrors(ErrorLevel minLevel = ErrorLevel.Error)
        {
            foreach (var error in _errorHistory)
            {
                if (error.level >= minLevel)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 安全执行操作，捕获并记录异常
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        /// <returns>操作是否成功执行</returns>
        public static bool SafeExecute(Action action, string context = null, UnityEngine.Object source = null)
        {
            try
            {
                action?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                LogException(ex, context, source);
                return false;
            }
        }

        /// <summary>
        /// 安全执行带返回值的操作，捕获并记录异常
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="func">要执行的函数</param>
        /// <param name="defaultValue">异常时的默认返回值</param>
        /// <param name="context">上下文信息</param>
        /// <param name="source">源对象</param>
        /// <returns>函数返回值或默认值</returns>
        public static T SafeExecute<T>(Func<T> func, T defaultValue = default(T), string context = null, UnityEngine.Object source = null)
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
    /// 错误处理扩展方法
    /// </summary>
    public static class ErrorHandlerExtensions
    {
        /// <summary>
        /// 为UnityEngine.Object添加错误记录扩展方法
        /// </summary>
        public static void LogInfo(this UnityEngine.Object obj, string message, string context = null)
        {
            ErrorHandler.LogInfo(message, context, obj);
        }

        public static void LogWarning(this UnityEngine.Object obj, string message, string context = null)
        {
            ErrorHandler.LogWarning(message, context, obj);
        }

        public static void LogError(this UnityEngine.Object obj, string message, string context = null)
        {
            ErrorHandler.LogError(message, context, obj);
        }

        public static void LogCritical(this UnityEngine.Object obj, string message, string context = null)
        {
            ErrorHandler.LogCritical(message, context, obj);
        }

        public static void LogException(this UnityEngine.Object obj, Exception exception, string context = null)
        {
            ErrorHandler.LogException(exception, context, obj);
        }
    }
}