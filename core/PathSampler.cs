// 请用此完整代码替换你的 PathSampler.cs

using System.Collections.Generic;
using UnityEngine;

public static class PathSampler
{
    /// <summary>
    /// 对路径进行采样，生成一个可用于渲染或物理计算的“脊椎”数据。
    /// </summary>
    public static PathSpine SamplePath(PathCreator creator, TerrainHeightProvider heightProvider)
    {
        if (creator == null || creator.profile == null) return new PathSpine();

        // --- 核心修改 I：直接将 creator 传递下去 ---
        // 我们不再需要分解出 creator.Path，creator 本身就是我们需要的一切。
        PathSpine idealSpine = GenerateIdealSpine(creator, creator.profile.generationPrecision);
        if (idealSpine.VertexCount < 2) return new PathSpine();

        if (creator.profile.snapToTerrain)
        {
            return ProjectSpineToTerrain(
                idealSpine,
                heightProvider,
                creator.profile.snapStrength,
                creator.profile.heightSmoothness
            );
        }
        return idealSpine;
    }

    private static PathSpine ProjectSpineToTerrain(PathSpine sourceSpine, TerrainHeightProvider heightProvider, float snapStrength, int heightSmoothness)
    {
        var projectedPoints = new Vector3[sourceSpine.VertexCount];
        System.Array.Copy(sourceSpine.points, projectedPoints, sourceSpine.VertexCount);

        if (heightProvider != null)
        {
            for (int i = 0; i < projectedPoints.Length; i++)
            {
                float terrainHeight = heightProvider.GetHeight(projectedPoints[i]);
                // 【解印 I：吸附度生效】
                // 以“吸附度”决定路径向地形贴合的程度
                projectedPoints[i].y = Mathf.Lerp(projectedPoints[i].y, terrainHeight, snapStrength);
            }
        }

        // 【解印 II：平滑度生效】
        // 在投影之后，进行高度平滑处理
        if (heightSmoothness > 0)
        {
            SmoothHeightProfile(ref projectedPoints, heightSmoothness);
        }

        var newTangents = RecalculateTangentsFromPoints(projectedPoints);
        var newNormals = GetSurfaceNormals(projectedPoints, heightProvider);
        return new PathSpine(projectedPoints, newTangents, newNormals, sourceSpine.timestamps);
    }

    // 【新增辅助神通】用于高度平滑
    private static void SmoothHeightProfile(ref Vector3[] points, int windowSize)
    {
        if (windowSize <= 0 || points.Length < 3) return;
        var originalHeights = new float[points.Length];
        for (int i = 0; i < points.Length; i++) originalHeights[i] = points[i].y;

        for (int i = 0; i < points.Length; i++)
        {
            float sumOfHeights = 0;
            int count = 0;
            for (int j = Mathf.Max(0, i - windowSize); j <= Mathf.Min(points.Length - 1, i + windowSize); j++)
            {
                sumOfHeights += originalHeights[j];
                count++;
            }
            if (count > 0) points[i].y = sumOfHeights / count;
        }
    }


    #region Unchanged Methods
    /// <summary>
    /// 根据理想的数学曲线，生成一个PathSpine。
    /// </summary>
    // --- 核心修改 II：方法签名变更 ---
    // 不再接收 IPath 和 Transform，而是直接接收 PathCreator。
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
    /// <summary>
    /// 沿路径生成等距的采样点。这是最底层的核心采样逻辑。
    /// </summary>
    // --- 核心修改 III：方法签名变更，并更新内部调用 ---
    private static void GenerateEquidistantPoints(PathCreator creator, float spacing, out List<Vector3> points, out List<float> cumulativeDistances)
    {
        points = new List<Vector3>();
        cumulativeDistances = new List<float>();

        // 旧调用: path.NumPoints -> 新调用: creator.NumPoints
        if (creator.NumPoints == 0) return;

        // 旧调用: path.GetPointAt(0, owner) -> 新调用: creator.GetPointAt(0)
        points.Add(creator.GetPointAt(0));
        cumulativeDistances.Add(0);

        if (creator.NumPoints <= 1) return;

        float distanceSinceLastSample = 0;
        Vector3 prevPoint = points[0];
        const float step = 0.005f; // 细分的步长，用于精确计算距离

        // 旧调用: path.NumSegments -> 新调用: creator.NumSegments
        for (float t = step; t <= creator.NumSegments; t += step)
        {
            // 旧调用: path.GetPointAt(t, owner) -> 新调用: creator.GetPointAt(t)
            Vector3 currentPoint = creator.GetPointAt(t);
            float dist = Vector3.Distance(prevPoint, currentPoint);

            while (distanceSinceLastSample + dist >= spacing)
            {
                float overshoot = (distanceSinceLastSample + dist) - spacing;
                Vector3 newSamplePoint = currentPoint + (prevPoint - currentPoint).normalized * overshoot;
                points.Add(newSamplePoint);
                cumulativeDistances.Add(cumulativeDistances[cumulativeDistances.Count - 1] + spacing);
                distanceSinceLastSample = overshoot - dist;
                prevPoint = newSamplePoint;
            }
            distanceSinceLastSample += dist;
            prevPoint = currentPoint;
        }
    }
    private static Vector3[] RecalculateTangentsFromPoints(Vector3[] points)
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
    private static Vector3[] GetSurfaceNormals(Vector3[] points, TerrainHeightProvider heightProvider)
    {
        var normals = new Vector3[points.Length];
        if (heightProvider == null)
        {
            for (int i = 0; i < points.Length; i++) normals[i] = Vector3.up;
            return normals;
        }
        for (int i = 0; i < points.Length; i++) normals[i] = heightProvider.GetNormal(points[i]);
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
    #endregion
}