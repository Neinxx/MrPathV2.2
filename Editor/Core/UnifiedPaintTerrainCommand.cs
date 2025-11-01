using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using __temp.MrPathV2.Runtime.Core;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一地形绘制命令 - 使用CPU数据源，自动选择最佳绘制器
    /// 遵循Unity最佳实践：命令模式、异步执行、资源管理
    /// </summary>
    public class UnifiedPaintTerrainCommand : IDisposable
    {
        private readonly PathCreator _pathCreator;
        private readonly bool _isPreview;
        private readonly PainterType _preferredPainterType;
        
        private IUnifiedTerrainPainter _painter;
        private bool _disposed = false;

        #region Construction

        public UnifiedPaintTerrainCommand(
            PathCreator pathCreator,
            bool isPreview = false,
            PainterType preferredPainterType = PainterType.GPU)
        {
            _pathCreator = pathCreator ?? throw new ArgumentNullException(nameof(pathCreator));
            _isPreview = isPreview;
            _preferredPainterType = preferredPainterType;

            ValidateInputs();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 异步执行地形绘制
        /// </summary>
        public async Task<TerrainPaintResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnifiedPaintTerrainCommand));

            try
            {
                // 创建最佳绘制器
                _painter = CreateOptimalPainter();
                if (_painter == null)
                {
                    return TerrainPaintResult.CreateFailure("无法创建合适的地形绘制器", PainterType.GPU);
                }

                // 执行绘制
                var result = await _painter.PaintAsync(_pathCreator, _isPreview, cancellationToken);
                
                LogResult(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("[UnifiedPaintTerrainCommand] 绘制操作被取消");
                return TerrainPaintResult.CreateFailure("操作被取消", _painter?.Type ?? PainterType.GPU);
            }
            catch (Exception ex)
            {
                var errorMessage = $"地形绘制失败: {ex.Message}";
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommand] {errorMessage}");
                return TerrainPaintResult.CreateFailure(errorMessage, _painter?.Type ?? PainterType.GPU);
            }
        }

        /// <summary>
        /// 同步执行地形绘制
        /// </summary>
        public TerrainPaintResult Execute()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnifiedPaintTerrainCommand));

            try
            {
                // 创建最佳绘制器
                _painter = CreateOptimalPainter();
                if (_painter == null)
                {
                    return TerrainPaintResult.CreateFailure("无法创建合适的地形绘制器", PainterType.GPU);
                }

                // 执行绘制
                var result = _painter.Paint(_pathCreator, _isPreview);
                
                LogResult(result);
                return result;
            }
            catch (Exception ex)
            {
                var errorMessage = $"地形绘制失败: {ex.Message}";
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommand] {errorMessage}");
                return TerrainPaintResult.CreateFailure(errorMessage, _painter?.Type ?? PainterType.GPU);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _painter?.Dispose();
                _painter = null;
                _disposed = true;
            }
        }

        #endregion

        #region Private Implementation

        private IUnifiedTerrainPainter CreateOptimalPainter()
        {
            try
            {
                // 获取PathData和PathProfile
                var pathData = _pathCreator.pathData;
                var pathProfile = _pathCreator.profile;
                
                // 查找最近的地形
                var terrain = FindNearestTerrain(_pathCreator.transform.position);
                if (terrain == null)
                {
                    UnityEngine.Debug.LogError("[UnifiedPaintTerrainCommand] 无法找到附近的地形");
                    return null;
                }
                
                return UnifiedPainterFactory.CreatePainter(
                    _preferredPainterType,
                    terrain,
                    CalculatePathBounds(pathData, pathProfile)
                );
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommand] 创建绘制器失败: {ex.Message}");
                return null;
            }
        }

        private UnityEngine.Terrain FindNearestTerrain(Vector3 position)
        {
            // 获取场景中所有地形
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains.Length == 0)
                return null;

            // 如果只有一个地形，直接返回
            if (terrains.Length == 1)
                return terrains[0];

            // 查找包含位置的地形
            foreach (var terrain in terrains)
            {
                var terrainPos = terrain.transform.position;
                var terrainSize = terrain.terrainData.size;
                
                // 检查位置是否在地形范围内
                if (position.x >= terrainPos.x && position.x <= terrainPos.x + terrainSize.x &&
                    position.z >= terrainPos.z && position.z <= terrainPos.z + terrainSize.z)
                {
                    return terrain;
                }
            }

            // 如果没有找到包含位置的地形，返回最近的地形
            UnityEngine.Terrain nearestTerrain = null;
            float nearestDistance = float.MaxValue;

            foreach (var terrain in terrains)
            {
                var terrainCenter = terrain.transform.position + terrain.terrainData.size * 0.5f;
                var distance = Vector3.Distance(position, terrainCenter);
                
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTerrain = terrain;
                }
            }

            return nearestTerrain;
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

            // 扩展边界以包含道路宽度
            var expansion = Vector3.one * (pathProfile.roadWidth * 0.5f + pathProfile.falloffWidth);
            min -= expansion;
            max += expansion;

            var center = (min + max) * 0.5f;
            var size = max - min;

            return new Bounds(center, size);
        }

        private void ValidateInputs()
        {
            if (_pathCreator == null)
                throw new ArgumentNullException(nameof(_pathCreator));
                
            var pathData = _pathCreator.pathData;
            var pathProfile = _pathCreator.profile;
            
            if (pathData == null)
                throw new ArgumentException("PathCreator.pathData 不能为空", nameof(_pathCreator));
                
            if (pathData.positions.Count == 0)
                throw new ArgumentException("PathData.positions 不能为空", nameof(_pathCreator));

            if (pathProfile == null)
                throw new ArgumentException("PathCreator.profile 不能为空", nameof(_pathCreator));
                
            if (pathProfile.roadRecipe == null)
                throw new ArgumentException("PathProfile.roadRecipe 不能为空", nameof(_pathCreator));

            if (pathProfile.roadWidth <= 0)
                throw new ArgumentException("PathProfile.roadWidth 必须大于0", nameof(_pathCreator));
        }

        private void LogResult(TerrainPaintResult result)
        {
            if (result.Success)
            {
                UnityEngine.Debug.Log($"[UnifiedPaintTerrainCommand] 绘制成功 - 使用 {result.GetType()} 绘制器，耗时 {result.ExecutionTimeMs:F2}ms");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[UnifiedPaintTerrainCommand] 绘制失败 - {result.ErrorMessage}");
            }
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 创建道路绘制命令
        /// </summary>
        public static UnifiedPaintTerrainCommand CreateRoadPaintCommand(
            PathCreator pathCreator,
            bool isPreview = false,
            PainterType preferredPainterType = PainterType.GPU)
        {
            return new UnifiedPaintTerrainCommand(pathCreator, isPreview, preferredPainterType);
        }

        /// <summary>
        /// 创建预览绘制命令
        /// </summary>
        public static UnifiedPaintTerrainCommand CreatePreviewCommand(
            PathCreator pathCreator,
            PainterType preferredPainterType = PainterType.GPU)
        {
            return new UnifiedPaintTerrainCommand(pathCreator, true, preferredPainterType);
        }

        #endregion
    }

    /// <summary>
    /// 地形绘制命令构建器 - 提供流畅的API
    /// </summary>
    public class UnifiedPaintCommandBuilder
    {
        private PathCreator _pathCreator;
        private bool _isPreview = false;
        private PainterType _preferredPainterType = PainterType.GPU;

        public UnifiedPaintCommandBuilder ForPathCreator(PathCreator pathCreator)
        {
            _pathCreator = pathCreator;
            return this;
        }

        public UnifiedPaintCommandBuilder AsPreview(bool isPreview = true)
        {
            _isPreview = isPreview;
            return this;
        }

        public UnifiedPaintCommandBuilder PreferPainter(PainterType painterType)
        {
            _preferredPainterType = painterType;
            return this;
        }

        public UnifiedPaintTerrainCommand Build()
        {
            return new UnifiedPaintTerrainCommand(_pathCreator, _isPreview, _preferredPainterType);
        }

        /// <summary>
        /// 构建并立即执行命令
        /// </summary>
        public TerrainPaintResult Execute()
        {
            using var command = Build();
            return command.Execute();
        }

        /// <summary>
        /// 构建并立即异步执行命令
        /// </summary>
        public async Task<TerrainPaintResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            using var command = Build();
            return await command.ExecuteAsync(cancellationToken);
        }
    }
}