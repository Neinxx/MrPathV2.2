// PathSampler.cs (已修正平滑算法调用)
using System.Collections.Generic;
using UnityEngine;

public static class PathSampler
{
    private const float MIN_POINT_DISTANCE_SQUARED = 0.0001f;

    public static PathSpine SamplePath(PathCreator creator, TerrainHeightProvider heightProvider)
    {
        if (creator == null || creator.profile == null) return new PathSpine();

        PathSpine localSpine = GenerateIdealSpine(creator, creator.profile.generationPrecision);
        if (localSpine.VertexCount < 2) return new PathSpine();

        PathSpine worldSpine;
        if (creator.profile.snapToTerrain)
        {
            // 【修正】将 creator.profile 传入，以便获取新的平滑参数
            worldSpine = ProjectSpineToTerrain(
                localSpine,
                creator.transform,
                heightProvider,
                creator.profile
            );
        }
        else
        {
            worldSpine = TransformSpineToWorld(localSpine, creator.transform);
        }

        return PurifySpine(worldSpine);
    }

    // 【修正】ProjectSpineToTerrain 的方法签名已更新
    private static PathSpine ProjectSpineToTerrain(PathSpine localSpine, Transform owner, TerrainHeightProvider heightProvider, PathProfile profile)
    {
        // 如果吸附强度为0，则等同于不吸附
        if (Mathf.Approximately(profile.snapStrength, 0f))
        {
            return TransformSpineToWorld(localSpine, owner);
        }

        var worldPoints = new Vector3[localSpine.VertexCount];
        for (int i = 0; i < localSpine.VertexCount; i++)
        {
            worldPoints[i] = owner.TransformPoint(localSpine.points[i]);
        }

        // 【核心修正】在吸附前，保存一份原始、未吸附的世界坐标点
        var originalWorldPoints = new Vector3[localSpine.VertexCount];
        worldPoints.CopyTo(originalWorldPoints, 0);

        if (heightProvider != null)
        {
            for (int i = 0; i < worldPoints.Length; i++)
            {
                float terrainHeight = heightProvider.GetHeight(worldPoints[i]);
                worldPoints[i].y = Mathf.Lerp(worldPoints[i].y, terrainHeight, profile.snapStrength);
            }
        }

        if (profile.heightSmoothRange > 0)
        {
            // 【核心修正】使用新的参数，正确地调用平滑函数
            SmoothHeightProfile(ref worldPoints, profile.heightSmoothRange, profile.heightSmoothRange, originalWorldPoints);
        }

        var worldTangents = RecalculateTangentsFromPoints(worldPoints);
        var worldNormals = GetSurfaceNormals(worldPoints, heightProvider);

        return new PathSpine(worldPoints, worldTangents, worldNormals, localSpine.timestamps);
    }

    // 【修正】SmoothHeightProfile 的新算法实现
    private static void SmoothHeightProfile(ref Vector3[] snappedPoints, int windowSize, float smoothStrength, Vector3[] originalPoints)
    {
        if (windowSize <= 0 || snappedPoints.Length < 3) return;

        // 1. 先对吸附后的点进行一次模糊，得到一个“绝对平滑”的基准
        var blurredHeights = new float[snappedPoints.Length];
        for (int i = 0; i < snappedPoints.Length; i++)
        {
            float sumOfHeights = 0; int count = 0;
            for (int j = Mathf.Max(0, i - windowSize); j <= Mathf.Min(snappedPoints.Length - 1, i + windowSize); j++)
            {
                sumOfHeights += snappedPoints[j].y; count++;
            }
            if (count > 0) blurredHeights[i] = sumOfHeights / count;
            else blurredHeights[i] = snappedPoints[i].y;
        }

        // 2. 在“绝对平滑”和“绝对原始”之间进行插值，由 smoothStrength 控制
        for (int i = 0; i < snappedPoints.Length; i++)
        {
            snappedPoints[i].y = Mathf.Lerp(blurredHeights[i], originalPoints[i].y, smoothStrength);
        }
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
        const float step = 0.005f;
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
    private static Vector3[] GetSurfaceNormals(Vector3[] worldPoints, TerrainHeightProvider heightProvider)
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