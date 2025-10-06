using UnityEngine;
using PathData = MrPath.PathData;

public static class CatmullRomEvaluator
{
    public static Vector3 Evaluate(float t, PathData data, Transform owner)
    {
        int numPoints = data.KnotCount;
        int numSegments = data.SegmentCount;
        if (numSegments == 0) return owner.TransformPoint(data.GetPosition(0));

        int p1_idx = Mathf.Clamp(Mathf.FloorToInt(t), 0, numSegments - 1);
        float localT = t - p1_idx;

        int p0_idx = Mathf.Clamp(p1_idx - 1, 0, numPoints - 1);
        int p2_idx = Mathf.Clamp(p1_idx + 1, 0, numPoints - 1);
        int p3_idx = Mathf.Clamp(p1_idx + 2, 0, numPoints - 1);

        Vector3 p0 = data.GetPosition(p0_idx);
        Vector3 p1 = data.GetPosition(p1_idx);
        Vector3 p2 = data.GetPosition(p2_idx);
        Vector3 p3 = data.GetPosition(p3_idx);

        float t2 = localT * localT, t3 = t2 * localT;
        Vector3 point = 0.5f * ((2 * p1) + (-p0 + p2) * localT + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        return owner.TransformPoint(point);
    }
}