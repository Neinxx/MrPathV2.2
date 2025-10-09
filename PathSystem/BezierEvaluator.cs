// --- 简单的静态评估器 (Simple Static Evaluators) ---
// 你可以将它们放在一个单独的文件中
using UnityEngine;
namespace MrPathV2
{


    public static class BezierEvaluator
    {
        public static Vector3 Evaluate(float t, PathData data, Transform owner)
        {
            int numSegments = data.SegmentCount;
            if (numSegments == 0) return owner.TransformPoint(data.GetPosition(0));

            int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(t), 0, numSegments - 1);
            float localT = t - segmentIndex;

            var knot1 = data.GetKnot(segmentIndex);
            var knot2 = data.GetKnot(segmentIndex + 1);

            Vector3 p0 = knot1.Position;
            Vector3 p1 = knot1.GlobalTangentOut;
            Vector3 p2 = knot2.GlobalTangentIn;
            Vector3 p3 = knot2.Position;

            float u = 1 - localT;
            Vector3 point = u * u * u * p0 + 3 * u * u * localT * p1 + 3 * u * localT * localT * p2 + localT * localT * localT * p3;
            return owner.TransformPoint(point);
        }
    }
}