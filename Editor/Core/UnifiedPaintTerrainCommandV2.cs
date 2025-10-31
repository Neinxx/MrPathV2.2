using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一地形绘制命令V2 - 使用CPU数据源的新架构
    /// 替换旧的PaintTerrainCommand，提供更简洁优雅的API
    /// </summary>
    public class UnifiedPaintTerrainCommandV2 : IDisposable
    {
        private readonly PathCreator _pathCreator;
        private readonly IHeightProvider _heightProvider;
        private readonly List<IUnifiedTerrainPainter> _activePainters;
        private bool _disposed = false;

        #region Construction

        public UnifiedPaintTerrainCommandV2(PathCreator pathCreator, IHeightProvider heightProvider)
        {
            _pathCreator = pathCreator ?? throw new ArgumentNullException(nameof(pathCreator));
            _heightProvider = heightProvider;
            _activePainters = new List<IUnifiedTerrainPainter>();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 异步执行地形绘制
        /// </summary>
        public async Task<UnifiedPaintResult> ExecuteAsync(
            List<UnityEngine.Terrain> terrains,
            PathProfile pathProfile,
            PainterType preferredPainterType = PainterType.GPU,
            bool isPreview = false,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnifiedPaintTerrainCommandV2));

            ValidateInputs(terrains, pathProfile);

            var results = new List<TerrainPaintResult>();
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 1. 准备路径数据
                var pathData = await PreparePathDataAsync(cancellationToken);
                if (pathData == null)
                {
                    return UnifiedPaintResult.CreateFailure("路径数据准备失败");
                }

                // 2. 并行处理所有地形
                var tasks = terrains.Select(terrain => 
                    ProcessSingleTerrainAsync(terrain, pathData, pathProfile, preferredPainterType, isPreview, cancellationToken)
                ).ToArray();

                var terrainResults = await Task.WhenAll(tasks);
                results.AddRange(terrainResults);

                // 3. 标记高度提供器为脏（如果不是预览模式）
                if (!isPreview)
                {
                    _heightProvider?.MarkAsDirty();
                }

                totalStopwatch.Stop();

                // 4. 汇总结果
                var successCount = results.Count(r => r.Success);
                var totalExecutionTime = (float)totalStopwatch.Elapsed.TotalMilliseconds;

                return new UnifiedPaintResult
                {
                    IsSuccess = successCount > 0,
                    ProcessedTerrainCount = terrains.Count,
                    SuccessfulTerrainCount = successCount,
                    TotalExecutionTimeMs = totalExecutionTime,
                    TerrainResults = results.ToArray(),
                    ErrorMessage = successCount == 0 ? "所有地形绘制失败" : null
                };
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("[UnifiedPaintTerrainCommandV2] 操作被取消");
                return UnifiedPaintResult.CreateFailure("操作被取消");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommandV2] 执行失败: {ex.Message}");
                return UnifiedPaintResult.CreateFailure($"执行失败: {ex.Message}");
            }
            finally
            {
                totalStopwatch.Stop();
            }
        }

        /// <summary>
        /// 同步执行地形绘制
        /// </summary>
        public UnifiedPaintResult Execute(
            List<UnityEngine.Terrain> terrains,
            PathProfile pathProfile,
            PainterType preferredPainterType = PainterType.GPU,
            bool isPreview = false)
        {
            return ExecuteAsync(terrains, pathProfile, preferredPainterType, isPreview).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // 清理所有活跃的绘制器
                foreach (var painter in _activePainters)
                {
                    painter?.Dispose();
                }
                _activePainters.Clear();

                _disposed = true;
            }
        }

        #endregion

        #region Private Implementation

        private async Task<PathData> PreparePathDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 从PathCreator获取路径数据
                if (_pathCreator?.pathData == null)
                {
                    UnityEngine.Debug.LogError("[UnifiedPaintTerrainCommandV2] PathCreator或pathData为空");
                    return null;
                }

                // 确保路径数据有效
                var pathData = _pathCreator.pathData;
                if (pathData.positions.Count < 2)
                {
                    UnityEngine.Debug.LogError("[UnifiedPaintTerrainCommandV2] 路径至少需要2个点");
                    return null;
                }

                // 如果需要，可以在这里进行异步的路径优化或处理
                await Task.Yield(); // 让出控制权，保持异步性质

                return pathData;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommandV2] 准备路径数据失败: {ex.Message}");
                return null;
            }
        }

        private async Task<TerrainPaintResult> ProcessSingleTerrainAsync(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            PainterType preferredPainterType,
            bool isPreview,
            CancellationToken cancellationToken)
        {
            IUnifiedTerrainPainter painter = null;

            try
            {
                // 1. 检查路径是否与地形相交
                if (!IsPathIntersectingTerrain(terrain, pathData))
                {
                    return TerrainPaintResult.CreateSuccess(PainterType.GPU, 0f, null);
                }

                // 2. 创建绘制器
                var pathBounds = CalculatePathBounds(pathData, pathProfile);
                painter = UnifiedPainterFactory.CreatePainter(preferredPainterType, terrain, pathBounds);
                
                if (painter == null)
                {
                    return TerrainPaintResult.CreateFailure("无法创建绘制器", PainterType.GPU);
                }

                // 3. 跟踪活跃绘制器
                lock (_activePainters)
                {
                    _activePainters.Add(painter);
                }

                // 4. 执行绘制
                var result = await painter.PaintAsync(terrain, pathData, pathProfile, isPreview, cancellationToken);

                UnityEngine.Debug.Log($"[UnifiedPaintTerrainCommandV2] 地形 '{terrain.name}' 绘制完成 - {result.GetType()}, 耗时 {result.ExecutionTimeMs:F2}ms");

                return result;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommandV2] 地形 '{terrain.name}' 绘制失败: {ex.Message}");
                return TerrainPaintResult.CreateFailure($"地形绘制失败: {ex.Message}", painter?.Type ?? PainterType.GPU);
            }
            finally
            {
                // 清理绘制器
                if (painter != null)
                {
                    lock (_activePainters)
                    {
                        _activePainters.Remove(painter);
                    }
                    painter.Dispose();
                }
            }
        }

        private static bool IsPathIntersectingTerrain(UnityEngine.Terrain terrain, PathData pathData)
        {
            var terrainBounds = new Bounds(
                terrain.transform.position + terrain.terrainData.size * 0.5f,
                terrain.terrainData.size
            );

            // 检查路径的任何点是否在地形边界内
            foreach (var position in pathData.positions)
            {
                if (terrainBounds.Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        private Bounds CalculatePathBounds(PathData pathData, PathProfile pathProfile)
        {
            if (pathData.positions.Count == 0)
                return new Bounds();

            var min = pathData.positions[0];
            var max = pathData.positions[0];

            foreach (var position in pathData.positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            // 扩展边界以包含道路宽度和衰减距离
            var expansion = Vector3.one * (pathProfile.roadWidth * 0.5f + pathProfile.falloffWidth);
            min -= expansion;
            max += expansion;

            var center = (min + max) * 0.5f;
            var size = max - min;

            return new Bounds(center, size);
        }

        private void ValidateInputs(List<UnityEngine.Terrain> terrains, PathProfile pathProfile)
        {
            if (terrains == null || terrains.Count == 0)
                throw new ArgumentException("地形列表不能为空", nameof(terrains));

            if (pathProfile == null)
                throw new ArgumentNullException(nameof(pathProfile));

            if (pathProfile.roadRecipe == null)
                throw new ArgumentException("PathProfile.roadRecipe 不能为空", nameof(pathProfile));

            if (_pathCreator == null)
                throw new InvalidOperationException("PathCreator 不能为空");

            // 检查所有地形是否有效
            for (int i = 0; i < terrains.Count; i++)
            {
                if (terrains[i] == null)
                    throw new ArgumentException($"地形[{i}]为空", nameof(terrains));

                if (terrains[i].terrainData == null)
                    throw new ArgumentException($"地形[{i}].terrainData为空", nameof(terrains));
            }
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 创建地形绘制命令
        /// </summary>
        public static UnifiedPaintTerrainCommandV2 Create(PathCreator pathCreator, IHeightProvider heightProvider = null)
        {
            return new UnifiedPaintTerrainCommandV2(pathCreator, heightProvider);
        }

        #endregion
    }

    /// <summary>
    /// 统一绘制结果
    /// </summary>
    public struct UnifiedPaintResult
    {
        public bool IsSuccess;
        public int ProcessedTerrainCount;
        public int SuccessfulTerrainCount;
        public float TotalExecutionTimeMs;
        public TerrainPaintResult[] TerrainResults;
        public string ErrorMessage;

        public static UnifiedPaintResult CreateFailure(string errorMessage)
        {
            return new UnifiedPaintResult
            {
                IsSuccess = false,
                ProcessedTerrainCount = 0,
                SuccessfulTerrainCount = 0,
                TotalExecutionTimeMs = 0f,
                TerrainResults = new TerrainPaintResult[0],
                ErrorMessage = errorMessage
            };
        }

        public static UnifiedPaintResult CreateSuccess(TerrainPaintResult[] results, float totalTime)
        {
            var successCount = results?.Count(r => r.Success) ?? 0;
            
            return new UnifiedPaintResult
            {
                IsSuccess = successCount > 0,
                ProcessedTerrainCount = results?.Length ?? 0,
                SuccessfulTerrainCount = successCount,
                TotalExecutionTimeMs = totalTime,
                TerrainResults = results ?? new TerrainPaintResult[0],
                ErrorMessage = null
            };
        }
    }
}