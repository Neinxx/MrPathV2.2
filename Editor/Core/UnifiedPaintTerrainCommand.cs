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
        private readonly UnityEngine.Terrain _terrain;
        private readonly PathData _pathData;
        private readonly PathProfile _pathProfile;
        private readonly bool _isPreview;
        private readonly PainterType _preferredPainterType;
        
        private IUnifiedTerrainPainter _painter;
        private bool _disposed = false;

        #region Construction

        public UnifiedPaintTerrainCommand(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            bool isPreview = false,
            PainterType preferredPainterType = PainterType.GPU)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _pathData = pathData ?? throw new ArgumentNullException(nameof(pathData));
            _pathProfile = pathProfile ?? throw new ArgumentNullException(nameof(pathProfile));
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
                var result = await _painter.PaintAsync(_terrain, _pathData, _pathProfile, _isPreview, cancellationToken);
                
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
                var result = _painter.Paint(_terrain, _pathData, _pathProfile, _isPreview);
                
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
                return UnifiedPainterFactory.CreatePainter(
                    _preferredPainterType,
                    _terrain,
                    CalculatePathBounds()
                );
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedPaintTerrainCommand] 创建绘制器失败: {ex.Message}");
                return null;
            }
        }

        private Bounds CalculatePathBounds()
        {
            if (_pathData.positions.Count == 0)
                return new Bounds();

            var min = _pathData.positions[0];
            var max = _pathData.positions[0];

            foreach (var position in _pathData.positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            // 扩展边界以包含道路宽度
            var expansion = Vector3.one * (_pathProfile.roadWidth * 0.5f + _pathProfile.falloffWidth);
            min -= expansion;
            max += expansion;

            var center = (min + max) * 0.5f;
            var size = max - min;

            return new Bounds(center, size);
        }

        private void ValidateInputs()
        {
            if (_terrain.terrainData == null)
                throw new ArgumentException("Terrain.terrainData 不能为空", nameof(_terrain));

            if (_pathData.positions.Count == 0)
                throw new ArgumentException("PathData.positions 不能为空", nameof(_pathData));

            if (_pathProfile.roadRecipe == null)
                throw new ArgumentException("PathProfile.roadRecipe 不能为空", nameof(_pathProfile));

            if (_pathProfile.roadWidth <= 0)
                throw new ArgumentException("PathProfile.roadWidth 必须大于0", nameof(_pathProfile));
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
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            bool isPreview = false,
            PainterType preferredPainterType = PainterType.GPU)
        {
            return new UnifiedPaintTerrainCommand(terrain, pathData, pathProfile, isPreview, preferredPainterType);
        }

        /// <summary>
        /// 创建预览绘制命令
        /// </summary>
        public static UnifiedPaintTerrainCommand CreatePreviewCommand(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            PainterType preferredPainterType = PainterType.GPU)
        {
            return new UnifiedPaintTerrainCommand(terrain, pathData, pathProfile, true, preferredPainterType);
        }

        #endregion
    }

    /// <summary>
    /// 地形绘制命令构建器 - 提供流畅的API
    /// </summary>
    public class UnifiedPaintCommandBuilder
    {
        private UnityEngine.Terrain _terrain;
        private PathData _pathData;
        private PathProfile _pathProfile;
        private bool _isPreview = false;
        private PainterType _preferredPainterType = PainterType.GPU;

        public UnifiedPaintCommandBuilder ForTerrain(UnityEngine.Terrain terrain)
        {
            _terrain = terrain;
            return this;
        }

        public UnifiedPaintCommandBuilder WithPath(PathData pathData)
        {
            _pathData = pathData;
            return this;
        }

        public UnifiedPaintCommandBuilder WithProfile(PathProfile pathProfile)
        {
            _pathProfile = pathProfile;
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
            return new UnifiedPaintTerrainCommand(_terrain, _pathData, _pathProfile, _isPreview, _preferredPainterType);
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