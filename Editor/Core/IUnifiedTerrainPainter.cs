using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using __temp.MrPathV2.Runtime.Core;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一地形绘制器接口 - 使用CPU数据源作为标准输入
    /// 遵循Unity最佳实践：接口隔离、依赖倒置
    /// </summary>
    public interface IUnifiedTerrainPainter
    {
        /// <summary>
        /// 绘制路径到地形（使用统一的CPU数据源）
        /// </summary>
        /// <param name="terrain">目标地形</param>
        /// <param name="pathData">CPU路径数据</param>
        /// <param name="pathProfile">路径配置文件</param>
        /// <param name="isPreview">是否为预览模式</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>绘制结果</returns>
        Task<TerrainPaintResult> PaintAsync(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            bool isPreview = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 同步绘制方法（为了向后兼容）
        /// </summary>
        /// <param name="terrain">目标地形</param>
        /// <param name="pathData">CPU路径数据</param>
        /// <param name="pathProfile">路径配置文件</param>
        /// <param name="isPreview">是否为预览模式</param>
        /// <returns>绘制结果</returns>
        TerrainPaintResult Paint(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            bool isPreview = false);

        /// <summary>
        /// 检查绘制器是否支持当前系统配置
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// 绘制器类型
        /// </summary>
        PainterType Type { get; }

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// 绘制器类型枚举
    /// </summary>
    public enum PainterType
    {
        CPU = 0,
        GPU = 1
    }

    /// <summary>
    /// 地形绘制结果
    /// </summary>
    public struct TerrainPaintResult
    {
        public bool Success;
        public string ErrorMessage;
        public Texture2D PreviewTexture;
        public float ExecutionTimeMs;
        public PainterType UsedPainter;

        public static TerrainPaintResult CreateSuccess(PainterType painterType, float executionTime, Texture2D previewTexture = null)
        {
            return new TerrainPaintResult
            {
                Success = true,
                UsedPainter = painterType,
                ExecutionTimeMs = executionTime,
                PreviewTexture = previewTexture
            };
        }

        public static TerrainPaintResult CreateFailure(string errorMessage, PainterType painterType)
        {
            return new TerrainPaintResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                UsedPainter = painterType
            };
        }
    }

    /// <summary>
    /// 绘制器工厂 - 根据系统配置和性能需求选择最佳绘制器
    /// </summary>
    public static class UnifiedPainterFactory
    {
        /// <summary>
        /// 创建最佳绘制器实例
        /// </summary>
        /// <param name="preferredType">首选绘制器类型</param>
        /// <param name="terrain">目标地形</param>
        /// <param name="pathBounds">路径边界（用于性能评估）</param>
        /// <returns>绘制器实例</returns>
        public static IUnifiedTerrainPainter CreatePainter(
            PainterType preferredType = PainterType.CPU,
            UnityEngine.Terrain terrain = null,
            Bounds? pathBounds = null)
        {
            // 根据系统能力和性能需求选择绘制器
            var shouldUseGpu = ShouldUseGpuPainter(preferredType, terrain, pathBounds);
            
            if (shouldUseGpu && SystemInfo.supportsComputeShaders)
            {
                return new UnifiedGpuTerrainPainter();
            }
            else
            {
                return new UnifiedCpuTerrainPainter();
            }
        }

        /// <summary>
        /// 获取所有可用的绘制器
        /// </summary>
        /// <returns>可用绘制器列表</returns>
        public static IUnifiedTerrainPainter[] GetAvailablePainters()
        {
            var painters = new System.Collections.Generic.List<IUnifiedTerrainPainter>();
            
            // CPU绘制器总是可用
            painters.Add(new UnifiedCpuTerrainPainter());
            
            // GPU绘制器需要计算着色器支持
            if (SystemInfo.supportsComputeShaders)
            {
                painters.Add(new UnifiedGpuTerrainPainter());
            }
            
            return painters.ToArray();
        }

        private static bool ShouldUseGpuPainter(PainterType preferredType, UnityEngine.Terrain terrain, Bounds? pathBounds)
        {
            // 如果明确指定GPU，且系统支持，则使用GPU
            if (preferredType == PainterType.GPU && SystemInfo.supportsComputeShaders)
                return true;

            // 如果明确指定CPU，则使用CPU
            if (preferredType == PainterType.CPU)
                return false;

            // 自动选择：基于地形大小和路径复杂度
            if (terrain != null && pathBounds.HasValue)
            {
                var terrainData = terrain.terrainData;
                var terrainPixels = terrainData.alphamapWidth * terrainData.alphamapHeight;
                
                // 计算路径覆盖的大致像素数
                var pathArea = pathBounds.Value.size.x * pathBounds.Value.size.z;
                var terrainArea = terrainData.size.x * terrainData.size.z;
                var coverageRatio = pathArea / terrainArea;
                var estimatedPixels = (int)(terrainPixels * coverageRatio);

                // 如果覆盖像素数超过阈值，使用GPU
                const int gpuThreshold = 50000; // 可配置的阈值
                return estimatedPixels > gpuThreshold && SystemInfo.supportsComputeShaders;
            }

            // 默认使用CPU
            return false;
        }
    }
}