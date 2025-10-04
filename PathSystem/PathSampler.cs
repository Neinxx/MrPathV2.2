using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 静态辅助类，负责将IPath曲线转换为等距的、可吸附地形的路径骨架(PathSpine)。
/// V3.0 (Refactored):
/// - 将核心方法 SamplePath 重构为一条清晰的、由多个辅助方法构成的“流水线”。
/// - 增强了代码的组织、注释和可读性。
/// </summary>
public static class PathSampler
{

    #region Public API

    /// <summary>
    /// 对路径进行采样，生成用于网格构建的骨架数据。
    /// 这是该类的主要入口点，它像一个总指挥，按顺序调用各个处理步骤。
    /// </summary>
    public static PathSpine SamplePath (PathCreator pathCreator, float spacing)
    {
        // 步骤 1: 生成近似等距的采样点、切线和累积距离
        GenerateEquidistantPoints (pathCreator.Path, pathCreator.transform, spacing,
            out List<Vector3> points, out List<Vector3> tangents, out List<float> cumulativeDistances);

        // 如果连两个点都无法生成，说明路径太短或无效，直接返回空骨架
        if (points.Count < 2)
        {
            return new PathSpine (new Vector3[0], new Vector3[0], new Vector3[0], new float[0]);
        }

        Vector3[] sampledPoints = points.ToArray ();
        Vector3[] sampledTangents = tangents.ToArray ();

        // 步骤 2 (可选): 执行高性能地形吸附Job，修改采样点的高度
        if (pathCreator.snapToTerrain && pathCreator.snapStrength > 0)
        {
            RunSnappingJob (ref sampledPoints, pathCreator);
        }

        // 步骤 3: 基于最终的点和切线，计算法线和归一化时间戳
        CalculateNormalsAndTimestamps (sampledTangents, cumulativeDistances, out Vector3[] normals, out float[] timestamps);

        // 步骤 4: 组装并返回最终的路径骨架
        return new PathSpine (sampledPoints, sampledTangents, normals, timestamps);
    }

    #endregion

    #region Private Pipeline Methods

    /// <summary>
    /// 流水线步骤1：生成近似等距的采样点。
    /// 这是最核心和最复杂的算法部分。
    /// </summary>
    private static void GenerateEquidistantPoints (IPath path, Transform owner, float spacing,
        out List<Vector3> points, out List<Vector3> tangents, out List<float> cumulativeDistances)
    {
        points = new List<Vector3> ();
        tangents = new List<Vector3> ();
        cumulativeDistances = new List<float> ();

        if (path.NumPoints == 0) return;

        Vector3 lastValidTangent = Vector3.forward;

        points.Add (path.GetPointAt (0, owner));
        tangents.Add (GetTangentAt (0, path, owner, ref lastValidTangent));
        cumulativeDistances.Add (0);

        if (path.NumPoints <= 1) return;

        float distanceSinceLastSample = 0;
        Vector3 prevPoint = points[0];
        const float step = 0.005f;

        for (float t = step; t <= path.NumSegments; t += step)
        {
            Vector3 currentPoint = path.GetPointAt (t, owner);
            float dist = Vector3.Distance (prevPoint, currentPoint);

            while (distanceSinceLastSample + dist >= spacing)
            {
                float overshoot = (distanceSinceLastSample + dist) - spacing;
                Vector3 newSamplePoint = currentPoint + (prevPoint - currentPoint).normalized * overshoot;

                points.Add (newSamplePoint);
                tangents.Add (GetTangentAt (t, path, owner, ref lastValidTangent));
                cumulativeDistances.Add (cumulativeDistances[cumulativeDistances.Count - 1] + spacing);

                distanceSinceLastSample = overshoot - dist; // 在新的循环中减去已走过的距离
            }

            distanceSinceLastSample += dist;
            prevPoint = currentPoint;
        }
    }

    /// <summary>
    /// 流水线步骤3：计算所有法线和归一化的时间戳。
    /// </summary>
    private static void CalculateNormalsAndTimestamps (IReadOnlyList<Vector3> tangents, IReadOnlyList<float> cumulativeDistances,
        out Vector3[] normals, out float[] timestamps)
    {
        int numPoints = tangents.Count;
        normals = new Vector3[numPoints];
        timestamps = new float[numPoints];
        float totalPathDistance = cumulativeDistances.Count > 1 ? cumulativeDistances[cumulativeDistances.Count - 1] : 0;

        for (int i = 0; i < numPoints; i++)
        {
            normals[i] = GetNormal (tangents[i]);
            timestamps[i] = (totalPathDistance > 0) ? cumulativeDistances[i] / totalPathDistance : 0;
        }
    }

    #endregion

    // 请将这些代码放置在 PathSampler.cs 内部

    // 请将这些代码放置在 PathSampler.cs 内部

    #region Job Execution & Math Helpers

    // 用于检查浮点数或向量长度是否接近于零的极小值
    private const float Epsilon = 0.0001f;

    /// <summary>
    /// 配置、调度并执行地形吸附的Job。
    /// </summary>
    private static void RunSnappingJob (ref Vector3[] points, PathCreator creator)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return;

        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;

        var initialPointsNative = new NativeArray<Vector3> (points, Allocator.TempJob);
        var snappedPointsNative = new NativeArray<Vector3> (points.Length, Allocator.TempJob);
        var heights = terrainData.GetHeights (0, 0, resolution, resolution);
        var heightsNative = new NativeArray<float> (heights.Length, Allocator.TempJob);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heightsNative[y * resolution + x] = heights[y, x];
            }
        }

        var job = new SnapSpineToTerrainJob
        {
            initialPoints = initialPointsNative,
            snapStrength = creator.snapStrength,
            terrainPosition = terrain.GetPosition (),
            terrainSize = terrainData.size,
            terrainHeights = heightsNative,
            heightmapResolution = resolution,
            snappedPoints = snappedPointsNative
        };

        JobHandle handle = job.Schedule (points.Length, 32);
        handle.Complete ();

        snappedPointsNative.CopyTo (points);

        initialPointsNative.Dispose ();
        snappedPointsNative.Dispose ();
        heightsNative.Dispose ();
    }

    /// <summary>
    /// 【已优化】通过在t点前后微小偏移处采样来近似计算切线，并增加了健壮性检查。
    /// </summary>
    private static Vector3 GetTangentAt (float t, IPath path, Transform owner, ref Vector3 lastValidTangent)
    {
        const float delta = 0.001f;
        float t1 = Mathf.Max (0, t - delta);
        float t2 = Mathf.Min (path.NumSegments, t + delta);
        Vector3 p1 = path.GetPointAt (t1, owner);
        Vector3 p2 = path.GetPointAt (t2, owner);
        Vector3 dir = p2 - p1;

        // 卫兵：如果向量长度过小，则返回上一个有效的切线，避免除以零
        if (dir.sqrMagnitude < Epsilon * Epsilon)
        {
            return lastValidTangent;
        }

        lastValidTangent = dir.normalized;
        return lastValidTangent;
    }

    /// <summary>
    /// 【已优化】根据切线计算法线，并增加了应对垂直切线的后备逻辑。
    /// </summary>
    private static Vector3 GetNormal (Vector3 tangent)
    {
        // 主参考轴
        Vector3 referenceUp = Vector3.up;

        // 卫兵：如果切线接近垂直，与Vector3.up的叉积结果会是零向量。
        // 我们检查叉积结果的长度，如果过小，则换一个参考轴。
        Vector3 normal = Vector3.Cross (tangent, referenceUp);
        if (normal.sqrMagnitude < Epsilon * Epsilon)
        {
            // 后备参考轴：使用世界前方向，它与世界Y轴正交
            referenceUp = Vector3.forward;
            normal = Vector3.Cross (tangent, referenceUp);
        }

        // 最后的卫兵：如果所有尝试都失败（例如切线也是(0,0,0)），返回一个默认安全值
        if (normal.sqrMagnitude < Epsilon * Epsilon)
        {
            return Vector3.right;
        }

        return normal.normalized;
    }

    #endregion
}
