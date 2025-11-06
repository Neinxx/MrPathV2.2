using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Performance
{
    /// <summary>
    ///     多层性能管理器：监控和优化大量层时的内存使用和渲染性能
    /// </summary>
    public static class MultiLayerPerformanceManager
    {
        private const int MaxRecommendedLayers = 16;
        private const int WarningLayerCount = 32;
        private static readonly Dictionary<int, PerformanceMetrics> PerformanceCache = new Dictionary<int, PerformanceMetrics>();

        /// <summary>
        ///     评估配方的性能影响
        /// </summary>
        private static PerformanceMetrics EvaluateRecipePerformance(StylizedRoadRecipe recipe)
        {
            if (recipe == null) return default;

            var activeLayerCount = 0;
            foreach (var layer in recipe.GetLayers())
            {
                if (layer != null && layer.enabled && layer.layerMask != null && layer.contentLayer != null)
                    activeLayerCount++;
            }

            var metrics = new PerformanceMetrics
            {
                LayerCount = activeLayerCount,
                ControlTextureCount = Mathf.CeilToInt(activeLayerCount / 4f),
                MemoryUsage = EstimateMemoryUsage(activeLayerCount),
                RenderTime = EstimateRenderTime(activeLayerCount)
            };

            return metrics;
        }

        /// <summary>
        ///     估算内存使用量（字节）
        /// </summary>
        private static long EstimateMemoryUsage(int layerCount)
        {
            // 每个Control贴图: 1024x1024 RGBA32 = 4MB
            var controlTextures = Mathf.CeilToInt(layerCount / 4f);
            long controlTextureMemory = controlTextures * 4 * 1024 * 1024;

            // 每个层贴图估算: 512x512 RGB24 = 768KB
            long layerTextureMemory = layerCount * 768 * 1024;

            // 预览RT和临时缓冲区
            long previewMemory = 2 * 1024 * 1024; // 2MB

            return controlTextureMemory + layerTextureMemory + previewMemory;
        }

        /// <summary>
        ///     估算渲染时间（毫秒）
        /// </summary>
        private static float EstimateRenderTime(int layerCount) =>
            // 基础渲染时间 + 每层额外时间
            1.0f + layerCount * 0.5f;

        /// <summary>
        ///     获取性能建议
        /// </summary>
        private static string GetPerformanceRecommendation(PerformanceMetrics metrics)
        {
            if (metrics.LayerCount <= 4)
                return "✅ 性能良好：使用标准4层模式";
            if (metrics.LayerCount <= MaxRecommendedLayers)
                return "⚡ 多层模式：性能良好，建议在移动设备上测试";
            if (metrics.LayerCount <= WarningLayerCount)
                return "⚠️ 大量层数：建议优化或合并部分层以提升性能";
            return "🔥 极大层数：强烈建议重新设计配方以避免性能问题";
        }

        /// <summary>
        ///     在Inspector中显示性能信息
        /// </summary>
        public static void DrawPerformanceInfo(StylizedRoadRecipe recipe)
        {
            var metrics = EvaluateRecipePerformance(recipe);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("性能分析", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"活跃层数: {metrics.LayerCount}");
                EditorGUILayout.LabelField($"Control贴图数: {metrics.ControlTextureCount}");
                EditorGUILayout.LabelField($"估算内存: {FormatBytes(metrics.MemoryUsage)}");
                EditorGUILayout.LabelField($"估算渲染时间: {metrics.RenderTime:F1}ms");

                var recommendation = GetPerformanceRecommendation(metrics);
                var messageType = metrics.LayerCount > WarningLayerCount ? MessageType.Error :
                    metrics.LayerCount > MaxRecommendedLayers ? MessageType.Warning :
                    MessageType.Info;

                EditorGUILayout.HelpBox(recommendation, messageType);
            }
        }

        /// <summary>
        ///     格式化字节数为可读字符串
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024f:F1} KB",
                < 1024 * 1024 * 1024 => $"{bytes / (1024f * 1024f):F1} MB",
                _ => $"{bytes / (1024f * 1024f * 1024f):F1} GB"
            };
        }

        /// <summary>
        ///     清理性能缓存
        /// </summary>
        public static void ClearCache()
        {
            PerformanceCache.Clear();
        }

        private struct PerformanceMetrics
        {
            public int LayerCount;
            public long MemoryUsage;
            public float RenderTime;
            public int ControlTextureCount;
        }
    }
}
