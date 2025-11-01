using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using __temp.MrPathV2.Runtime.Core;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一数据适配器 - 将CPU数据源转换为GPU绘制所需的格式
    /// 遵循Unity最佳实践：单一职责、依赖注入、内存安全
    /// </summary>
    public static class UnifiedDataAdapter
    {
        #region GPU Data Structures

        /// <summary>
        /// GPU绘制所需的路径数据（从CPU PathData + PathProfile转换而来）
        /// </summary>
        public struct GpuPathData : IDisposable
        {
            public NativeArray<float3> SpinePoints;
            public NativeArray<float2> ContourPoints;
            public float PathWidth;
            public float PathLength;
            public Bounds PathBounds;
            public bool IsCreated;

            public void Dispose()
            {
                if (SpinePoints.IsCreated) SpinePoints.Dispose();
                if (ContourPoints.IsCreated) ContourPoints.Dispose();
                IsCreated = false;
            }
        }

        /// <summary>
        /// GPU绘制所需的配方数据（从CPU StylizedRoadRecipe转换而来）
        /// </summary>
        public struct GpuRecipeData : IDisposable
        {
            public NativeArray<LayerConfig> Layers;
            public float FalloffDistance;
            public NativeArray<Keyframe> FalloffCurve;
            public float MasterOpacity;
            public bool IsCreated;

            public void Dispose()
            {
                if (Layers.IsCreated) Layers.Dispose();
                if (FalloffCurve.IsCreated) FalloffCurve.Dispose();
                IsCreated = false;
            }
        }

        /// <summary>
        /// GPU层配置（从CPU RoadLayer转换而来）
        /// </summary>
        [Serializable]
        public struct LayerConfig
        {
            public int TerrainLayerIndex;
            public BlendMode BlendMode;
            public float Opacity;
            public bool Enabled;
        }

        /// <summary>
        /// 混合模式枚举
        /// </summary>
        public enum BlendMode
        {
            Normal = 0,
            Multiply = 1,
            Additive = 2,
            Overlay = 3
        }

        #endregion

        #region Conversion Methods

        /// <summary>
        /// 将CPU PathData转换为GPU所需格式
        /// </summary>
        /// <param name="cpuPathData">CPU路径数据</param>
        /// <param name="pathProfile">路径配置文件</param>
        /// <param name="allocator">内存分配器</param>
        /// <returns>GPU路径数据</returns>
        public static GpuPathData ConvertPathData(PathData cpuPathData, PathProfile pathProfile, Allocator allocator = Allocator.TempJob)
        {
            if (cpuPathData == null)
                throw new ArgumentNullException(nameof(cpuPathData));
            if (pathProfile == null)
                throw new ArgumentNullException(nameof(pathProfile));

            var knotCount = cpuPathData.KnotCount;
            if (knotCount == 0)
            {
                return new GpuPathData
                {
                    IsCreated = false
                };
            }

            // 转换脊线点
            var spinePoints = new NativeArray<float3>(knotCount, allocator);
            for (int i = 0; i < knotCount; i++)
            {
                var knot = cpuPathData.GetKnot(i);
                spinePoints[i] = knot.Position;
            }

            // 计算路径长度
            float pathLength = CalculatePathLength(spinePoints);

            // 计算路径边界
            var pathBounds = CalculatePathBounds(spinePoints, pathProfile.roadWidth);

            // 生成轮廓点（使用现有的轮廓生成器）
            var contourPoints = GenerateContourPoints(cpuPathData, pathProfile, allocator);

            return new GpuPathData
            {
                SpinePoints = spinePoints,
                ContourPoints = contourPoints,
                PathWidth = pathProfile.roadWidth,
                PathLength = pathLength,
                PathBounds = pathBounds,
                IsCreated = true
            };
        }

        /// <summary>
        /// 将CPU StylizedRoadRecipe转换为GPU所需格式
        /// </summary>
        /// <param name="cpuRecipe">CPU道路配方</param>
        /// <param name="pathProfile">路径配置文件</param>
        /// <param name="allocator">内存分配器</param>
        /// <returns>GPU配方数据</returns>
        public static GpuRecipeData ConvertRecipeData(StylizedRoadRecipe cpuRecipe, PathProfile pathProfile, Allocator allocator = Allocator.TempJob)
        {
            if (cpuRecipe == null)
                throw new ArgumentNullException(nameof(cpuRecipe));
            if (pathProfile == null)
                throw new ArgumentNullException(nameof(pathProfile));

            var activeLayers = cpuRecipe.layers.FindAll(layer => layer != null && layer.enabled);
            if (activeLayers.Count == 0)
            {
                return new GpuRecipeData
                {
                    IsCreated = false
                };
            }

            // 转换层配置
            var layers = new NativeArray<LayerConfig>(activeLayers.Count, allocator);
            for (int i = 0; i < activeLayers.Count; i++)
            {
                var cpuLayer = activeLayers[i];
                layers[i] = new LayerConfig
                {
                    TerrainLayerIndex = GetTerrainLayerIndex(cpuLayer.contentLayer),
                    BlendMode = ConvertBlendMode(BlendMode.Normal),
                    Opacity = cpuLayer.opacity,
                    Enabled = cpuLayer.enabled
                };
            }

            // 转换衰减曲线
            var falloffCurve = ConvertAnimationCurve(pathProfile.falloffShape, allocator);

            return new GpuRecipeData
            {
                Layers = layers,
                FalloffDistance = pathProfile.falloffWidth,
                FalloffCurve = falloffCurve,
                MasterOpacity = cpuRecipe.masterOpacity,
                IsCreated = true
            };
        }

        /// <summary>
        /// 创建完整的GPU绘制数据包
        /// </summary>
        /// <param name="cpuPathData">CPU路径数据</param>
        /// <param name="pathProfile">路径配置文件</param>
        /// <param name="allocator">内存分配器</param>
        /// <returns>GPU绘制数据包</returns>
        public static GpuRenderData CreateGpuRenderData(PathData cpuPathData, PathProfile pathProfile, Allocator allocator = Allocator.TempJob)
        {
            var gpuPathData = ConvertPathData(cpuPathData, pathProfile, allocator);
            var gpuRecipeData = ConvertRecipeData(pathProfile.roadRecipe, pathProfile, allocator);

            return new GpuRenderData
            {
                PathData = gpuPathData,
                RecipeData = gpuRecipeData,
                IsCreated = gpuPathData.IsCreated && gpuRecipeData.IsCreated
            };
        }

        #endregion

        #region Helper Methods

        public static float CalculatePathLength(NativeArray<float3> spinePoints)
        {
            if (spinePoints.Length < 2) return 0f;

            float totalLength = 0f;
            for (int i = 1; i < spinePoints.Length; i++)
            {
                totalLength += math.distance(spinePoints[i - 1], spinePoints[i]);
            }
            return totalLength;
        }

        public static Bounds CalculatePathBounds(NativeArray<float3> spinePoints, float pathWidth)
        {
            if (spinePoints.Length == 0)
                return new Bounds();

            var min = spinePoints[0];
            var max = spinePoints[0];

            for (int i = 1; i < spinePoints.Length; i++)
            {
                min = math.min(min, spinePoints[i]);
                max = math.max(max, spinePoints[i]);
            }

            // 扩展边界以包含路径宽度
            var halfWidth = pathWidth * 0.5f;
            var expansion = new float3(halfWidth, 0, halfWidth);

            return new Bounds(
                (min + max) * 0.5f,
                (max - min) + expansion * 2f
            );
        }

        private static NativeArray<float2> GenerateContourPoints(PathData cpuPathData, PathProfile pathProfile, Allocator allocator)
        {
            // 使用现有的轮廓生成器
            var pathSpine = CreatePathSpine(cpuPathData);

            NativeArray<float2> contour;
            float4 bounds;
            RoadContourGenerator.GenerateContour(pathSpine, pathProfile, out contour, out bounds, allocator);
            return contour;
        }

        private static PathSpine CreatePathSpine(PathData cpuPathData)
        {
            // 从CPU PathData创建PathSpine（数组版）
            var knotCount = cpuPathData.KnotCount;
            if (knotCount == 0)
            {
                return new PathSpine(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<float>());
            }

            var points = new Vector3[knotCount];
            var tangents = new Vector3[knotCount];
            var normals = new Vector3[knotCount];
            var timestamps = new float[knotCount];

            for (int i = 0; i < knotCount; i++)
            {
                var knot = cpuPathData.GetKnot(i);
                points[i] = knot.Position;
                timestamps[i] = i;

                Vector3 prev = i > 0 ? cpuPathData.GetKnot(i - 1).Position : knot.Position;
                Vector3 next = i < knotCount - 1 ? cpuPathData.GetKnot(i + 1).Position : knot.Position;
                var tangent = next - prev;
                tangents[i] = tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.forward;
                normals[i] = Vector3.up;
            }

            return new PathSpine(points, tangents, normals, timestamps);
        }

        private static int GetTerrainLayerIndex(TerrainLayer terrainLayer)
        {
            // 这里需要实现地形层索引的查找逻辑
            // 暂时返回0，实际实现需要查找地形层在TerrainData中的索引
            return 0;
        }

        private static BlendMode ConvertBlendMode(BlendMode cpuBlendMode)
        {
            return cpuBlendMode switch
            {
                BlendMode.Normal => BlendMode.Normal,
                BlendMode.Multiply => BlendMode.Multiply,
                BlendMode.Additive => BlendMode.Additive,
                BlendMode.Overlay => BlendMode.Overlay,
                _ => BlendMode.Normal
            };
        }

        private static NativeArray<Keyframe> ConvertAnimationCurve(AnimationCurve curve, Allocator allocator)
        {
            if (curve == null || curve.keys.Length == 0)
            {
                // 创建默认的线性衰减曲线
                var defaultKeys = new NativeArray<Keyframe>(2, allocator);
                defaultKeys[0] = new Keyframe(0f, 1f);
                defaultKeys[1] = new Keyframe(1f, 0f);
                return defaultKeys;
            }

            var keys = new NativeArray<Keyframe>(curve.keys.Length, allocator);
            for (int i = 0; i < curve.keys.Length; i++)
            {
                keys[i] = curve.keys[i];
            }
            return keys;
        }

        #endregion
    }

    /// <summary>
    /// GPU绘制数据包 - 包含所有GPU绘制所需的数据
    /// </summary>
    public struct GpuRenderData : IDisposable
    {
        public UnifiedDataAdapter.GpuPathData PathData;
        public UnifiedDataAdapter.GpuRecipeData RecipeData;
        public bool IsCreated;

        public void Dispose()
        {
            PathData.Dispose();
            RecipeData.Dispose();
            IsCreated = false;
        }
    }
}
