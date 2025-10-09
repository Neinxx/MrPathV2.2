using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【简单的静态评估器】Catmull-Rom 曲线评估器。
    /// 你可以将它们放在一个单独的文件中。
    /// </summary>
    /// <remarks>
    /// Catmull-Rom 曲线是一种通过控制点的插值曲线，常用于路径和平滑动画。
    /// 它具有局部控制性和C1连续性，适合需要平滑过渡的场景。
    /// </remarks>

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
}