// PathSampler.cs (已修正平滑算法调用)

using System.Collections.Generic;
using MrPathV2.Runtime.Interfaces;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    public static class PathSampler
    {
        private const float MinPointDistanceSquared = 1e-8f;


        public static PathSpine SamplePath(PathCreator creator, IHeightProvider heightProvider)
        {
            using (ProfilingMarkers.PathSamplerSamplePath.Auto())
            {
                if (creator == null || creator.profile == null) return new PathSpine();

                // 1. 生成理想的、平滑的局部空间脊线
                var localSpine = GenerateIdealSpine(creator, creator.profile.longitudinalSegments);
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
        }

        /// <summary>
        ///     【全新算法】将路径脊线通过“悬挂与松弛”算法应用到地形上。
        /// </summary>
        private static PathSpine DrapeSpineOnTerrain(PathSpine localSpine, Transform owner, IHeightProvider heightProvider, PathProfile profile)
        {
            using (ProfilingMarkers.PathSamplerDrapeSpineOnTerrain.Auto())
            {
                var pointCount = localSpine.VertexCount;
                var worldPoints = new Vector3[pointCount];
                var terrainHeights = new float[pointCount];
                float pathAverageHeight = 0;
                float terrainAverageHeight = 0;

                // --- 步骤 0: 转换到世界空间并采样地形 ---
                for (var i = 0; i < pointCount; i++)
                {
                    worldPoints[i] = owner.TransformPoint(localSpine.Points[i]);
                    terrainHeights[i] = heightProvider.GetHeight(worldPoints[i]);
                    pathAverageHeight += worldPoints[i].y;
                    terrainAverageHeight += terrainHeights[i];
                }
                pathAverageHeight /= pointCount;
                terrainAverageHeight /= pointCount;

                // --- 步骤 1: 整体高度对齐 ---
                var elevationDifference = terrainAverageHeight - pathAverageHeight;
                for (var i = 0; i < pointCount; i++)
                {
                    worldPoints[i].y += elevationDifference;
                }

                // --- 步骤 2: 向上悬挂 (Upward Drape) ---
                for (var i = 0; i < pointCount; i++)
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

                    for (var iter = 0; iter < profile.smoothness; iter++)
                    {
                        for (var i = 0; i < pointCount; i++)
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
                        for (var i = 0; i < pointCount; i++)
                        {
                            worldPoints[i].y = Mathf.Max(smoothedHeights[i], terrainHeights[i]);
                        }
                    }
                }

                // --- 步骤 4: 应用最终的高度偏移 ---
                if (Mathf.Abs(profile.heightOffset) > 0.001f)
                {
                    for (var i = 0; i < pointCount; i++)
                    {
                        worldPoints[i].y += profile.heightOffset;
                    }
                }

                // --- 最后: 重新计算切线和法线 ---
                var worldTangents = RecalculateTangentsFromPoints(worldPoints);
                var worldNormals = GetSurfaceNormals(worldPoints, heightProvider);

                return new PathSpine(worldPoints, worldTangents, worldNormals, localSpine.Timestamps);
            }
        }


        // --- 其他所有辅助方法保持不变 ---
        // ... (GenerateIdealSpine, GenerateEquidistantPoints, TransformSpineToWorld, etc.)
        private static PathSpine GenerateIdealSpine(PathCreator creator, int segmentsAlong)
        {
            GeneratePointsBySegments(creator, Mathf.Max(2, segmentsAlong), out var points, out var cumulativeDistances);
            if (points.Count < 2)
            {
                var p0 = creator.GetPointAtLocal(0);
                var p1 = creator.GetPointAtLocal(creator.NumSegments);
                if ((p1 - p0).sqrMagnitude < 1e-8f)
                    p1 = p0 + Vector3.forward * 0.02f;
                points = new List<Vector3> { p0, p1 };
                cumulativeDistances = new List<float> { 0f, Vector3.Distance(p0, p1) };
            }
            var sampledPoints = points.ToArray();
            var tangents = RecalculateTangentsFromPoints(sampledPoints);
            var upVectors = new Vector3[sampledPoints.Length];
            for (var i = 0; i < upVectors.Length; i++) upVectors[i] = Vector3.up;
            CalculateTimestamps(cumulativeDistances, out var timestamps);
            return new PathSpine(sampledPoints, tangents, upVectors, timestamps);
        }
        // duplicate removed
        
        private static void GeneratePointsBySegments(PathCreator creator, int segments, out List<Vector3> localPoints, out List<float> cumulativeDistances)
        {
            localPoints = new List<Vector3>();
            cumulativeDistances = new List<float>();
            if (creator.NumPoints < 2)
                return;
        
            segments = Mathf.Max(2, segments);
        
            // 细采样以获得近似弧长
            var finePoints = new List<Vector3>();
            var fineDistances = new List<float>();
            var lastPoint = creator.GetPointAtLocal(0);
            finePoints.Add(lastPoint);
            fineDistances.Add(0f);
        
            var step = Mathf.Max(1f / (creator.NumSegments * 40f), 0.005f);
            var accum = 0f;
            for (var t = step; t <= creator.NumSegments; t += step)
            {
                var p = creator.GetPointAtLocal(t);
                var d = Vector3.Distance(lastPoint, p);
                if (d > 1e-6f)
                {
                    accum += d;
                    finePoints.Add(p);
                    fineDistances.Add(accum);
                    lastPoint = p;
                }
            }
        
            if (finePoints.Count < 2)
                return;
        
            var totalLength = fineDistances[fineDistances.Count - 1];
            if (totalLength <= 0f)
                return;
        
            var targetSpacing = totalLength / segments; // segments 个区间，生成 segments+1 个点
            localPoints.Add(finePoints[0]);
            cumulativeDistances.Add(0f);
        
            var targetDist = targetSpacing;
            var i = 1; // 从第二个细采样点开始
            while (i < finePoints.Count && targetDist < totalLength - 1e-5f)
            {
                var prevDist = fineDistances[i - 1];
                var currDist = fineDistances[i];
                if (currDist >= targetDist)
                {
                    var segLen = currDist - prevDist;
                    var w = segLen > 0f ? (targetDist - prevDist) / segLen : 0f;
                    var newPoint = Vector3.Lerp(finePoints[i - 1], finePoints[i], w);
                    localPoints.Add(newPoint);
                    cumulativeDistances.Add(targetDist);
                    targetDist += targetSpacing;
                }
                else
                {
                    i++;
                }
            }
        
            // 确保末端点
            localPoints.Add(finePoints[finePoints.Count - 1]);
            cumulativeDistances.Add(totalLength);
        }
        private static void GenerateEquidistantPoints(PathCreator creator, float spacing, out List<Vector3> localPoints, out List<float> cumulativeDistances)
        {
            localPoints = new List<Vector3>();
            cumulativeDistances = new List<float>();
            if (creator.NumPoints < 2) return;
            var lastSampledPoint = creator.GetPointAtLocal(0);
            localPoints.Add(lastSampledPoint);
            cumulativeDistances.Add(0);
            var distanceSinceLastSample = 0f;
            var previousFineStepPoint = lastSampledPoint;
            // const float step = 0.005f;
            // 依据路径分段动态调整采样步长，确保每段约采样20次，且不低于0.01
            var step = Mathf.Max(1f / (creator.NumSegments * 20f), 0.01f);
            for (var t = step; t <= creator.NumSegments; t += step)
            {
                var currentFineStepPoint = creator.GetPointAtLocal(t);
                var segmentLength = Vector3.Distance(previousFineStepPoint, currentFineStepPoint);
                if (segmentLength < 0.0001f) continue;
                distanceSinceLastSample += segmentLength;
                while (distanceSinceLastSample >= spacing)
                {
                    var overshoot = distanceSinceLastSample - spacing;
                    var newPoint = Vector3.Lerp(currentFineStepPoint, previousFineStepPoint, overshoot / segmentLength);
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
            for (var i = 0; i < localSpine.VertexCount; i++)
            {
                worldPoints[i] = owner.TransformPoint(localSpine.Points[i]);
                worldTangents[i] = owner.TransformDirection(localSpine.Tangents[i]).normalized;
                worldNormals[i] = owner.TransformDirection(localSpine.SurfaceNormals[i]).normalized;
            }
            return new PathSpine(worldPoints, worldTangents, worldNormals, localSpine.Timestamps);
        }
        private static PathSpine PurifySpine(PathSpine sourceSpine)
        {
            if (sourceSpine.VertexCount < 2) return sourceSpine;
            var cleanPoints = new List<Vector3>
            {
                sourceSpine.Points[0]
            };
            var cleanTimestamps = new List<float>
            {
                sourceSpine.Timestamps[0]
            };
            var cleanNormals = new List<Vector3>
            {
                sourceSpine.SurfaceNormals[0]
            };
            for (var i = 1; i < sourceSpine.VertexCount; i++)
            {
                if ((sourceSpine.Points[i] - cleanPoints[cleanPoints.Count - 1]).sqrMagnitude > MinPointDistanceSquared)
                {
                    cleanPoints.Add(sourceSpine.Points[i]);
                    cleanTimestamps.Add(sourceSpine.Timestamps[i]);
                    cleanNormals.Add(sourceSpine.SurfaceNormals[i]);
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
            for (var i = 0; i < points.Length; i++)
            {
                if (i == 0) tangents[i] = (points[1] - points[0]).normalized;
                else if (i == points.Length - 1) tangents[i] = (points[i] - points[i - 1]).normalized;
                else tangents[i] = (points[i + 1] - points[i - 1]).normalized;
                if (tangents[i].sqrMagnitude < 0.0001f)
                    tangents[i] = i > 0 ? (points[i] - points[i - 1]).normalized : Vector3.forward;
            }
            return tangents;
        }
        private static Vector3[] GetSurfaceNormals(Vector3[] worldPoints, IHeightProvider heightProvider)
        {
            var normals = new Vector3[worldPoints.Length];
            if (heightProvider == null)
            {
                for (var i = 0; i < worldPoints.Length; i++) normals[i] = Vector3.up;
                return normals;
            }
            for (var i = 0; i < worldPoints.Length; i++) normals[i] = heightProvider.GetNormal(worldPoints[i]);
            return normals;
        }
        private static void CalculateTimestamps(IReadOnlyList<float> cumulativeDistances, out float[] timestamps)
        {
            var numPoints = cumulativeDistances.Count;
            timestamps = new float[numPoints];
            if (numPoints < 2) return;
            var totalPathDistance = cumulativeDistances[numPoints - 1];
            for (var i = 0; i < numPoints; i++)
            {
                timestamps[i] = totalPathDistance > 0 ? cumulativeDistances[i] / totalPathDistance : 0;
            }
        }
    }
}
