// PathSampler.cs (已修正平滑算法调用)
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{
    public static class PathSampler
    {
        private const float MIN_POINT_DISTANCE_SQUARED = 0.0001f;



        public static PathSpine SamplePath(PathCreator creator, IHeightProvider heightProvider)
        {
            if (creator == null || creator.profile == null) return new PathSpine();

            // 1. 生成理想的、平滑的局部空间脊线
            PathSpine localSpine = GenerateIdealSpine(creator, creator.profile.generationPrecision);
            if (localSpine.VertexCount < 2) return new PathSpine();

            // 2. 根据配置决定是否进行地形吸附
            PathSpine worldSpine;
            if (creator.profile.snapToTerrain && heightProvider != null)
            {
                // 【核心重构】调用全新的地形吸附算法
                worldSpine = DrapeSpineOnTerrain(localSpine, creator.transform, heightProvider, creator.profile);
            }
            else
            {
                // 如果不吸附，则简单转换到世界空间
                worldSpine = TransformSpineToWorld(localSpine, creator.transform);
            }

            // 3. 清理并最终确定脊线数据
            return PurifySpine(worldSpine);
        }

        /// <summary>
        /// 【全新算法】将路径脊线通过“悬挂与松弛”算法应用到地形上。
        /// </summary>
        private static PathSpine DrapeSpineOnTerrain(PathSpine localSpine, Transform owner, IHeightProvider heightProvider, PathProfile profile)
        {
            int pointCount = localSpine.VertexCount;
            var worldPoints = new Vector3[pointCount];
            var terrainHeights = new float[pointCount];
            float pathAverageHeight = 0;
            float terrainAverageHeight = 0;

            // --- 步骤 0: 转换到世界空间并采样地形 ---
            for (int i = 0; i < pointCount; i++)
            {
                worldPoints[i] = owner.TransformPoint(localSpine.points[i]);
                terrainHeights[i] = heightProvider.GetHeight(worldPoints[i]);
                pathAverageHeight += worldPoints[i].y;
                terrainAverageHeight += terrainHeights[i];
            }
            pathAverageHeight /= pointCount;
            terrainAverageHeight /= pointCount;

            // --- 步骤 1: 整体高度对齐 ---
            float elevationDifference = terrainAverageHeight - pathAverageHeight;
            for (int i = 0; i < pointCount; i++)
            {
                worldPoints[i].y += elevationDifference;
            }

            // --- 步骤 2: 向上悬挂 (Upward Drape) ---
            for (int i = 0; i < pointCount; i++)
            {
                if (worldPoints[i].y < terrainHeights[i])
                {
                    worldPoints[i].y = terrainHeights[i];
                }
            }

            // --- 步骤 3: 迭代松弛平滑 (Iterative Relaxation) ---
            if (profile.smoothness > 0)
            {
                // 我们使用一个临时数组来存储每次迭代的结果，避免原地修改导致错误
                var smoothedHeights = new float[pointCount];

                for (int iter = 0; iter < profile.smoothness; iter++)
                {
                    for (int i = 0; i < pointCount; i++)
                    {
                        // 将每个点的高度设置为其邻居的平均高度
                        if (i > 0 && i < pointCount - 1)
                        {
                            smoothedHeights[i] = (worldPoints[i - 1].y + worldPoints[i + 1].y) / 2f;
                        }
                        else
                        {
                            smoothedHeights[i] = worldPoints[i].y; // 保持端点不变
                        }
                    }

                    // 将平滑后的结果应用回 worldPoints，但要确保不穿地
                    for (int i = 0; i < pointCount; i++)
                    {
                        worldPoints[i].y = Mathf.Max(smoothedHeights[i], terrainHeights[i]);
                    }
                }
            }

            // --- 步骤 4: 应用最终的高度偏移 ---
            if (Mathf.Abs(profile.heightOffset) > 0.001f)
            {
                for (int i = 0; i < pointCount; i++)
                {
                    worldPoints[i].y += profile.heightOffset;
                }
            }

            // --- 最后: 重新计算切线和法线 ---
            var worldTangents = RecalculateTangentsFromPoints(worldPoints);
            var worldNormals = GetSurfaceNormals(worldPoints, heightProvider);

            return new PathSpine(worldPoints, worldTangents, worldNormals, localSpine.timestamps);
        }



        // --- 其他所有辅助方法保持不变 ---
        // ... (GenerateIdealSpine, GenerateEquidistantPoints, TransformSpineToWorld, etc.)
        private static PathSpine GenerateIdealSpine(PathCreator creator, float precision)
        {
            GenerateEquidistantPoints(creator, precision, out var points, out var cumulativeDistances);
            if (points.Count < 2) return new PathSpine();
            var sampledPoints = points.ToArray();
            var tangents = RecalculateTangentsFromPoints(sampledPoints);
            var upVectors = new Vector3[sampledPoints.Length];
            for (int i = 0; i < upVectors.Length; i++) upVectors[i] = Vector3.up;
            CalculateTimestamps(cumulativeDistances, out var timestamps);
            return new PathSpine(sampledPoints, tangents, upVectors, timestamps);
        }
        private static void GenerateEquidistantPoints(PathCreator creator, float spacing, out List<Vector3> localPoints, out List<float> cumulativeDistances)
        {
            localPoints = new List<Vector3>(); cumulativeDistances = new List<float>();
            if (creator.NumPoints < 2) return;
            Vector3 lastSampledPoint = creator.GetPointAtLocal(0);
            localPoints.Add(lastSampledPoint); cumulativeDistances.Add(0);
            float distanceSinceLastSample = 0f;
            Vector3 previousFineStepPoint = lastSampledPoint;
            // const float step = 0.005f;
            // 依据路径分段动态调整采样步长，确保每段约采样20次，且不低于0.01
            float step = Mathf.Max(1f / (creator.NumSegments * 20f), 0.01f);
            for (float t = step; t <= creator.NumSegments; t += step)
            {
                Vector3 currentFineStepPoint = creator.GetPointAtLocal(t);
                float segmentLength = Vector3.Distance(previousFineStepPoint, currentFineStepPoint);
                if (segmentLength < 0.0001f) continue;
                distanceSinceLastSample += segmentLength;
                while (distanceSinceLastSample >= spacing)
                {
                    float overshoot = distanceSinceLastSample - spacing;
                    Vector3 newPoint = Vector3.Lerp(currentFineStepPoint, previousFineStepPoint, overshoot / segmentLength);
                    localPoints.Add(newPoint);
                    cumulativeDistances.Add(cumulativeDistances[cumulativeDistances.Count - 1] + spacing);
                    distanceSinceLastSample = overshoot;
                }
                previousFineStepPoint = currentFineStepPoint;
            }
        }
        private static PathSpine TransformSpineToWorld(PathSpine localSpine, Transform owner)
        {
            var worldPoints = new Vector3[localSpine.VertexCount];
            var worldTangents = new Vector3[localSpine.VertexCount];
            var worldNormals = new Vector3[localSpine.VertexCount];
            for (int i = 0; i < localSpine.VertexCount; i++)
            {
                worldPoints[i] = owner.TransformPoint(localSpine.points[i]);
                worldTangents[i] = owner.TransformDirection(localSpine.tangents[i]).normalized;
                worldNormals[i] = owner.TransformDirection(localSpine.surfaceNormals[i]).normalized;
            }
            return new PathSpine(worldPoints, worldTangents, worldNormals, localSpine.timestamps);
        }
        private static PathSpine PurifySpine(PathSpine sourceSpine)
        {
            if (sourceSpine.VertexCount < 2) return sourceSpine;
            var cleanPoints = new List<Vector3> { sourceSpine.points[0] };
            var cleanTimestamps = new List<float> { sourceSpine.timestamps[0] };
            var cleanNormals = new List<Vector3> { sourceSpine.surfaceNormals[0] };
            for (int i = 1; i < sourceSpine.VertexCount; i++)
            {
                if ((sourceSpine.points[i] - cleanPoints[cleanPoints.Count - 1]).sqrMagnitude > MIN_POINT_DISTANCE_SQUARED)
                {
                    cleanPoints.Add(sourceSpine.points[i]);
                    cleanTimestamps.Add(sourceSpine.timestamps[i]);
                    cleanNormals.Add(sourceSpine.surfaceNormals[i]);
                }
            }
            if (cleanPoints.Count < 2) return new PathSpine();
            var purifiedPoints = cleanPoints.ToArray();
            var purifiedTimestamps = cleanTimestamps.ToArray();
            var purifiedNormals = cleanNormals.ToArray();
            var newTangents = RecalculateTangentsFromPoints(purifiedPoints);
            return new PathSpine(purifiedPoints, newTangents, purifiedNormals, purifiedTimestamps);
        }
        public static Vector3[] RecalculateTangentsFromPoints(Vector3[] points)
        {
            if (points.Length < 2) return new Vector3[0];
            var tangents = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                if (i == 0) tangents[i] = (points[1] - points[0]).normalized;
                else if (i == points.Length - 1) tangents[i] = (points[i] - points[i - 1]).normalized;
                else tangents[i] = (points[i + 1] - points[i - 1]).normalized;
                if (tangents[i].sqrMagnitude < 0.0001f)
                    tangents[i] = (i > 0) ? (points[i] - points[i - 1]).normalized : Vector3.forward;
            }
            return tangents;
        }
        private static Vector3[] GetSurfaceNormals(Vector3[] worldPoints, IHeightProvider heightProvider)
        {
            var normals = new Vector3[worldPoints.Length];
            if (heightProvider == null) { for (int i = 0; i < worldPoints.Length; i++) normals[i] = Vector3.up; return normals; }
            for (int i = 0; i < worldPoints.Length; i++) normals[i] = heightProvider.GetNormal(worldPoints[i]);
            return normals;
        }
        private static void CalculateTimestamps(IReadOnlyList<float> cumulativeDistances, out float[] timestamps)
        {
            int numPoints = cumulativeDistances.Count; timestamps = new float[numPoints];
            if (numPoints < 2) return;
            float totalPathDistance = cumulativeDistances[numPoints - 1];
            for (int i = 0; i < numPoints; i++)
                timestamps[i] = (totalPathDistance > 0) ? cumulativeDistances[i] / totalPathDistance : 0;
        }
    }
}