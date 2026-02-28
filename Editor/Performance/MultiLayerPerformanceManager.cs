using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MrPathV2
{
    /// <summary>
    /// 多层性能管理器：监控和优化大量层时的内存使用和渲染性能
    /// </summary>
    public static class MultiLayerPerformanceManager
    {
        private static readonly Dictionary<int, PerformanceMetrics> _performanceCache = new Dictionary<int, PerformanceMetrics>();
        private const int MAX_RECOMMENDED_LAYERS = 16;
        private const int WARNING_LAYER_COUNT = 32;
        
        public struct PerformanceMetrics
        {
            public int layerCount;
            public long memoryUsage;
            public float renderTime;
            public int controlTextureCount;
        }
        
        /// <summary>
        /// 评估配方的性能影响
        /// </summary>
        public static PerformanceMetrics EvaluateRecipePerformance(StylizedRoadRecipe recipe)
        {
            if (recipe == null) return default;
            
            int activeLayerCount = 0;
            foreach (var layer in recipe.blendLayers)
            {
                if (layer.enabled && layer.mask != null && layer.terrainLayer != null)
                    activeLayerCount++;
            }
            
            var metrics = new PerformanceMetrics
            {
                layerCount = activeLayerCount,
                controlTextureCount = Mathf.CeilToInt(activeLayerCount / 4f),
                memoryUsage = EstimateMemoryUsage(activeLayerCount),
                renderTime = EstimateRenderTime(activeLayerCount)
            };
            
            return metrics;
        }
        
        /// <summary>
        /// 估算内存使用量（字节）
        /// </summary>
        private static long EstimateMemoryUsage(int layerCount)
        {
            // 每个Control贴图: 1024x1024 RGBA32 = 4MB
            int controlTextures = Mathf.CeilToInt(layerCount / 4f);
            long controlTextureMemory = controlTextures * 4 * 1024 * 1024;
            
            // 每个层贴图估算: 512x512 RGB24 = 768KB
            long layerTextureMemory = layerCount * 768 * 1024;
            
            // 预览RT和临时缓冲区
            long previewMemory = 2 * 1024 * 1024; // 2MB
            
            return controlTextureMemory + layerTextureMemory + previewMemory;
        }
        
        /// <summary>
        /// 估算渲染时间（毫秒）
        /// </summary>
        private static float EstimateRenderTime(int layerCount)
        {
            // 基础渲染时间 + 每层额外时间
            return 1.0f + (layerCount * 0.5f);
        }
        
        /// <summary>
        /// 获取性能建议
        /// </summary>
        public static string GetPerformanceRecommendation(PerformanceMetrics metrics)
        {
            if (metrics.layerCount <= 4)
                return "✅ 性能良好：使用标准4层模式";
            else if (metrics.layerCount <= MAX_RECOMMENDED_LAYERS)
                return "⚡ 多层模式：性能良好，建议在移动设备上测试";
            else if (metrics.layerCount <= WARNING_LAYER_COUNT)
                return "⚠️ 大量层数：建议优化或合并部分层以提升性能";
            else
                return "🔥 极大层数：强烈建议重新设计配方以避免性能问题";
        }
        
        /// <summary>
        /// 在Inspector中显示性能信息
        /// </summary>
        public static void DrawPerformanceInfo(StylizedRoadRecipe recipe)
        {
            var metrics = EvaluateRecipePerformance(recipe);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("性能分析", EditorStyles.boldLabel);
            
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"活跃层数: {metrics.layerCount}");
                EditorGUILayout.LabelField($"Control贴图数: {metrics.controlTextureCount}");
                EditorGUILayout.LabelField($"估算内存: {FormatBytes(metrics.memoryUsage)}");
                EditorGUILayout.LabelField($"估算渲染时间: {metrics.renderTime:F1}ms");
                
                string recommendation = GetPerformanceRecommendation(metrics);
                MessageType messageType = metrics.layerCount > WARNING_LAYER_COUNT ? MessageType.Error :
                                        metrics.layerCount > MAX_RECOMMENDED_LAYERS ? MessageType.Warning :
                                        MessageType.Info;
                
                EditorGUILayout.HelpBox(recommendation, messageType);
            }
        }
        
        /// <summary>
        /// 格式化字节数为可读字符串
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
        }
        
        /// <summary>
        /// 清理性能缓存
        /// </summary>
        public static void ClearCache()
        {
            _performanceCache.Clear();
        }
    }
}