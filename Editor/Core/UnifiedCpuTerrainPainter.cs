using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2.Editor.Terrain;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Jobs;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一CPU地形绘制器 - 使用CPU数据源的标准实现
    /// 遵循Unity最佳实践：Job System、Burst编译、内存安全
    /// </summary>
    public class UnifiedCpuTerrainPainter : IUnifiedTerrainPainter
    {
        public bool IsSupported => true; // CPU绘制器总是支持
        public PainterType Type => PainterType.CPU;

        private bool _disposed = false;

        #region Public API

        public async Task<TerrainPaintResult> PaintAsync(
            PathCreator pathCreator,
            bool isPreview = false,
            CancellationToken cancellationToken = default)
        {
            if (pathCreator == null)
                throw new ArgumentNullException(nameof(pathCreator));

            var pathData = pathCreator.pathData;
            var pathProfile = pathCreator.profile;

            // 查找最近的地形
            var terrain = FindNearestTerrain(pathCreator.transform.position);
            if (terrain == null)
                return TerrainPaintResult.CreateFailure("无法找到附近的地形", PainterType.CPU);

            ValidateInputs(terrain, pathData, pathProfile);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 在后台线程执行CPU绘制
                var result = await Task.Run(() => ExecutePaint(terrain, pathData, pathProfile, isPreview, cancellationToken), cancellationToken);

                stopwatch.Stop();
                result.ExecutionTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;

                return result;
            }
            catch (OperationCanceledException)
            {
                return TerrainPaintResult.CreateFailure("操作被取消", PainterType.CPU);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedCpuTerrainPainter] 异步绘制失败: {ex.Message}");
                return TerrainPaintResult.CreateFailure(ex.Message, PainterType.CPU);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public TerrainPaintResult Paint(
            PathCreator pathCreator,
            bool isPreview = false)
        {
            if (pathCreator == null)
                throw new ArgumentNullException(nameof(pathCreator));

            var pathData = pathCreator.pathData;
            var pathProfile = pathCreator.profile;

            // 查找最近的地形
            var terrain = FindNearestTerrain(pathCreator.transform.position);
            if (terrain == null)
                return TerrainPaintResult.CreateFailure("无法找到附近的地形", PainterType.CPU);

            ValidateInputs(terrain, pathData, pathProfile);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = ExecutePaint(terrain, pathData, pathProfile, isPreview, CancellationToken.None);
                stopwatch.Stop();
                result.ExecutionTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[UnifiedCpuTerrainPainter] 同步绘制失败: {ex.Message}");
                return TerrainPaintResult.CreateFailure(ex.Message, PainterType.CPU);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // CPU绘制器通常不需要特殊的资源清理
                _disposed = true;
            }
        }

        #endregion

        #region Core Implementation

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

        private TerrainPaintResult ExecutePaint(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            bool isPreview,
            CancellationToken cancellationToken)
        {
            var terrainData = terrain.terrainData;
            if (terrainData == null)
            {
                return TerrainPaintResult.CreateFailure("地形数据为空", PainterType.CPU);
            }

            try
            {
                // 1. 准备Job数据
                var jobData = PrepareJobData(terrain, pathData, pathProfile, cancellationToken);
                if (!jobData.IsValid)
                {
                    return TerrainPaintResult.CreateFailure("Job数据准备失败", PainterType.CPU);
                }

                // 2. 计算覆盖区域
                var coverageArea = CalculateCoverageArea(terrain, pathData, pathProfile);

                // 3. 执行CPU Job绘制
                var paintResult = ExecuteCpuJobs(terrain, jobData, coverageArea, isPreview, cancellationToken);

                // 4. 清理Job数据
                jobData.Dispose();

                return paintResult;
            }
            catch (Exception ex)
            {
                return TerrainPaintResult.CreateFailure($"绘制执行失败: {ex.Message}", PainterType.CPU);
            }
        }

        private CpuJobData PrepareJobData(
            UnityEngine.Terrain terrain,
            PathData pathData,
            PathProfile pathProfile,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 创建脊线数据
            var spineData = CreateSpineData(pathData);

            // 创建配置数据
            var profileData = CreateProfileData(pathProfile);

            // 创建配方数据
            var recipeData = CreateRecipeData(pathProfile, terrain);

            // 生成道路轮廓
            var (roadContour, contourBounds) = GenerateRoadContour(pathData, pathProfile);

            return new CpuJobData
            {
                SpineData = spineData,
                ProfileData = profileData,
                RecipeData = recipeData,
                RoadContour = roadContour,
                ContourBounds = contourBounds,
                IsValid = spineData.IsCreated && profileData.IsCreated && recipeData.IsCreated && roadContour.IsCreated
            };
        }

        private PathJobsUtility.SpineData CreateSpineData(PathData pathData)
        {
            var spine = CreatePathSpine(pathData);
            return new PathJobsUtility.SpineData(spine, Allocator.TempJob);
        }

        private PathJobsUtility.ProfileData CreateProfileData(PathProfile pathProfile)
        {
            return new PathJobsUtility.ProfileData(pathProfile, Allocator.TempJob);
        }

        private RecipeData CreateRecipeData(PathProfile pathProfile, UnityEngine.Terrain terrain)
        {
            if (pathProfile == null)
            {
                throw new ArgumentNullException(nameof(pathProfile));
            }

            // 解析地形层映射
            var layerMap = LayerResolver.ResolveEnsurePresentSmart(terrain, pathProfile);

            // 创建CPU配方数据（阈值与预览一致：opaquePreview=0.2，否则0）
            var threshold = (pathProfile != null && pathProfile.opaquePreview) ? 0.2f : 0f;
            return new RecipeData(pathProfile, layerMap, 0f, 0f, Allocator.TempJob, threshold);
        }

        private (NativeArray<float2> contour, float4 bounds) GenerateRoadContour(PathData pathData, PathProfile pathProfile)
        {
            var pathSpine = CreatePathSpine(pathData);

            NativeArray<float2> contour;
            float4 bounds;
            RoadContourGenerator.GenerateContour(pathSpine, pathProfile, out contour, out bounds, Allocator.TempJob);

            return (contour, bounds);
        }

        private PathSpine CreatePathSpine(PathData pathData)
        {
            var knotCount = pathData.KnotCount;
            if (knotCount == 0)
            {
                return new PathSpine(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<float>());
            }

            var points = new Vector3[knotCount];
            var tangents = new Vector3[knotCount];
            var normals = new Vector3[knotCount];
            var timestamps = new float[knotCount];

            // 简易法线与切线估算（基于相邻点差分），时间戳使用索引
            for (int i = 0; i < knotCount; i++)
            {
                var knot = pathData.GetKnot(i);
                points[i] = knot.Position;
                timestamps[i] = i;

                Vector3 prev = i > 0 ? pathData.GetKnot(i - 1).Position : knot.Position;
                Vector3 next = i < knotCount - 1 ? pathData.GetKnot(i + 1).Position : knot.Position;
                var tangent = (next - prev);
                tangents[i] = tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
                normals[i] = Vector3.up;
            }

            return new PathSpine(points, tangents, normals, timestamps);
        }

        private CoverageArea CalculateCoverageArea(UnityEngine.Terrain terrain, PathData pathData, PathProfile pathProfile)
        {
            var terrainData = terrain.terrainData;
            var terrainPos = terrain.transform.position;
            var terrainSize = terrainData.size;
            var resolution = terrainData.alphamapResolution;

            // 计算路径边界
            var pathBounds = CalculatePathBounds(pathData, pathProfile.roadWidth);

            // 转换为地形纹理坐标
            var minX = Mathf.FloorToInt(((pathBounds.min.x - terrainPos.x) / terrainSize.x) * resolution);
            var minY = Mathf.FloorToInt(((pathBounds.min.z - terrainPos.z) / terrainSize.z) * resolution);
            var maxX = Mathf.CeilToInt(((pathBounds.max.x - terrainPos.x) / terrainSize.x) * resolution);
            var maxY = Mathf.CeilToInt(((pathBounds.max.z - terrainPos.z) / terrainSize.z) * resolution);

            // 限制在地形范围内
            minX = Mathf.Clamp(minX, 0, resolution - 1);
            minY = Mathf.Clamp(minY, 0, resolution - 1);
            maxX = Mathf.Clamp(maxX, 0, resolution - 1);
            maxY = Mathf.Clamp(maxY, 0, resolution - 1);

            return new CoverageArea
            {
                Min = new Vector2Int(minX, minY),
                Max = new Vector2Int(maxX, maxY)
            };
        }

        private Bounds CalculatePathBounds(PathData pathData, float pathWidth)
        {
            if (pathData.KnotCount == 0)
                return new Bounds();

            var firstKnot = pathData.GetKnot(0);
            var min = firstKnot.Position;
            var max = firstKnot.Position;

            for (int i = 1; i < pathData.KnotCount; i++)
            {
                var knot = pathData.GetKnot(i);
                min = Vector3.Min(min, knot.Position);
                max = Vector3.Max(max, knot.Position);
            }

            // 扩展边界以包含路径宽度
            var halfWidth = pathWidth * 0.5f;
            var expansion = new Vector3(halfWidth, 0, halfWidth);

            return new Bounds(
                (min + max) * 0.5f,
                (max - min) + expansion * 2f
            );
        }

        private TerrainPaintResult ExecuteCpuJobs(
            UnityEngine.Terrain terrain,
            CpuJobData jobData,
            CoverageArea coverageArea,
            bool isPreview,
            CancellationToken cancellationToken)
        {
            try
            {
                // 使用现有的CPU绘制器逻辑
                var cpuPainter = new CpuTerrainPainter();

                // 执行绘制
                var task = cpuPainter.ExecuteAsync(
                    terrain,
                    jobData.SpineData,
                    jobData.ProfileData,
                    jobData.RecipeData,
                    jobData.RoadContour,
                    jobData.ContourBounds,
                    coverageArea.Min,
                    coverageArea.Max,
                    cancellationToken);

                task.Wait(cancellationToken);

                return TerrainPaintResult.CreateSuccess(PainterType.CPU, 0f);
            }
            catch (Exception ex)
            {
                return TerrainPaintResult.CreateFailure($"CPU Job执行失败: {ex.Message}", PainterType.CPU);
            }
        }

        #endregion

        #region Helper Structures

        private struct CpuJobData : IDisposable
        {
            public PathJobsUtility.SpineData SpineData;
            public PathJobsUtility.ProfileData ProfileData;
            public RecipeData RecipeData;
            public NativeArray<float2> RoadContour;
            public float4 ContourBounds;
            public bool IsValid;

            public void Dispose()
            {
                if (SpineData.IsCreated) SpineData.Dispose();
                if (ProfileData.IsCreated) ProfileData.Dispose();
                if (RecipeData.IsCreated) RecipeData.Dispose();
                if (RoadContour.IsCreated) RoadContour.Dispose();
                IsValid = false;
            }
        }

        private struct CoverageArea
        {
            public Vector2Int Min;
            public Vector2Int Max;
        }

        #endregion

        #region Validation

        private static void ValidateInputs(UnityEngine.Terrain terrain, PathData pathData, PathProfile pathProfile)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));
            if (pathData == null)
                throw new ArgumentNullException(nameof(pathData));
            if (pathProfile == null)
                throw new ArgumentNullException(nameof(pathProfile));
            if (pathProfile.roadRecipe == null)
                throw new ArgumentException("PathProfile.roadRecipe 不能为空", nameof(pathProfile));
        }

        #endregion
    }
}