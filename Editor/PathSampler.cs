// 请用此完整代码替换你的 PathSampler.cs

using System.Collections.Generic;
using UnityEngine;

public static class PathSampler
{
    public static PathSpine SamplePath(PathCreator creator, TerrainHeightProvider heightProvider)
    {
        PathSpine idealSpine = GenerateIdealSpine(creator.Path, creator.transform, creator.profile.generationPrecision);
        if (idealSpine.VertexCount < 2) return new PathSpine();

        if (creator.profile.snapToTerrain)
        {
            // 【解印】将神通所需的参数，传入法门之中
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

    // ... 其他所有方法保持不变 ...
    #region Unchanged Methods
    private static PathSpine GenerateIdealSpine(IPath path, Transform owner, float precision)
    {
        GenerateEquidistantPoints(path, owner, precision, out var points, out var cumulativeDistances);
        if (points.Count < 2) return new PathSpine();
        var sampledPoints = points.ToArray();
        var tangents = RecalculateTangentsFromPoints(sampledPoints);
        var upVectors = new Vector3[sampledPoints.Length];
        for (int i = 0; i < upVectors.Length; i++) upVectors[i] = Vector3.up;
        CalculateTimestamps(cumulativeDistances, out var timestamps);
        return new PathSpine(sampledPoints, tangents, upVectors, timestamps);
    }
    private static void GenerateEquidistantPoints(IPath path, Transform owner, float spacing, out List<Vector3> points, out List<float> cumulativeDistances)
    {
        points = new List<Vector3>(); cumulativeDistances = new List<float>();
        if (path.NumPoints == 0) return;
        points.Add(path.GetPointAt(0, owner)); cumulativeDistances.Add(0);
        if (path.NumPoints <= 1) return;
        float distanceSinceLastSample = 0; Vector3 prevPoint = points[0];
        const float step = 0.005f;
        for (float t = step; t <= path.NumSegments; t += step)
        {
            Vector3 currentPoint = path.GetPointAt(t, owner);
            float dist = Vector3.Distance(prevPoint, currentPoint);
            while (distanceSinceLastSample + dist >= spacing)
            {
                float overshoot = (distanceSinceLastSample + dist) - spacing;
                Vector3 newSamplePoint = currentPoint + (prevPoint - currentPoint).normalized * overshoot;
                points.Add(newSamplePoint); cumulativeDistances.Add(cumulativeDistances[cumulativeDistances.Count - 1] + spacing);
                distanceSinceLastSample = overshoot - dist; prevPoint = newSamplePoint;
            }
            distanceSinceLastSample += dist; prevPoint = currentPoint;
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