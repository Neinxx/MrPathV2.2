
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 【最终圆满版 • 千变万化之法】
/// </summary>
[CreateAssetMenu(fileName = "BezierStrategy", menuName = "MrPath/Strategies/Bezier")]
public class BezierStrategy : PathStrategy
{
    [Header("法则参数")]
    [Tooltip("在添加新节点时，自动生成的切线长度与线段长度的比例。")]
    [Range(0.05f, 1.0f)]
    public float defaultTangentScale = 0.333f;

    #region 数学法则实现

    // 假设这是你的 BezierPathStrategy.cs 文件
    public override Vector3 GetPointAt(float t, PathData data)
    {
        // --- 修正后的守护逻辑 ---
        if (data.KnotCount == 0) return Vector3.zero;
        if (data.SegmentCount == 0) return data.GetPosition(0);

        // --- 核心计算逻辑 (保持不变) ---
        int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(t), 0, data.SegmentCount - 1);
        float localT = t - segmentIndex;

        PathData.Knot startKnot = data.GetKnot(segmentIndex);
        PathData.Knot endKnot = data.GetKnot(segmentIndex + 1);

        // --- 【【【 最关键的修正 】】】 ---
        // 所有的点现在都在纯粹的本地空间中定义
        // p1 和 p2 是控制点，它们的位置是锚点位置加上其相对的切线向量
        Vector3 p0 = startKnot.Position;
        Vector3 p1 = startKnot.Position + startKnot.TangentOut; // 使用相对切线
        Vector3 p2 = endKnot.Position + endKnot.TangentIn;     // 使用相对切线
        Vector3 p3 = endKnot.Position;

        // --- 贝塞尔曲线公式 (保持不变) ---
        float u = 1 - localT;
        float t_sq = localT * localT;
        float u_sq = u * u;

        Vector3 point = (u_sq * u * p0) + (3 * u_sq * localT * p1) + (3 * u * t_sq * p2) + (t_sq * localT * p3);

        // --- 最终返回：纯粹的本地坐标 ---
        return point;
    }

    public override void AddSegment(Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        Vector3 newPos = owner.InverseTransformPoint(newPointWorldPos);
        if (data.KnotCount == 0)
        {
            data.AddKnot(newPos, Vector3.zero, Vector3.zero);
            return;
        }
        int lastIndex = data.KnotCount - 1;
        Vector3 lastPos = data.GetPosition(lastIndex);
        Vector3 offset = (newPos - lastPos) * defaultTangentScale;
        data.MoveTangentOut(lastIndex, offset);
        data.AddKnot(newPos, -offset, Vector3.zero);
    }

    public override void MovePoint(int flatIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        Vector3 newLocalPos = owner.InverseTransformPoint(newPointWorldPos);
        DecodeIndex(flatIndex, out int knotIndex, out int pointType);
        if (knotIndex < 0 || knotIndex >= data.KnotCount) return;
        if (pointType == 0) data.MovePosition(knotIndex, newLocalPos);
        else if (pointType == 1) data.MoveTangentOut(knotIndex, newLocalPos - data.GetPosition(knotIndex));
        else data.MoveTangentIn(knotIndex, newLocalPos - data.GetPosition(knotIndex));
    }

    public override void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        if (segmentIndex >= data.SegmentCount) { AddSegment(newPointWorldPos, data, owner); return; }
        PathData.Knot startKnot = data.GetKnot(segmentIndex);
        PathData.Knot endKnot = data.GetKnot(segmentIndex + 1);
        Vector3 p0 = startKnot.Position, p1 = startKnot.GlobalTangentOut, p2 = endKnot.GlobalTangentIn, p3 = endKnot.Position;
        float t = FindTValueOnSegment(p0, p1, p2, p3, owner.InverseTransformPoint(newPointWorldPos));
        Vector3 p01 = Vector3.Lerp(p0, p1, t), p12 = Vector3.Lerp(p1, p2, t), p23 = Vector3.Lerp(p2, p3, t);
        Vector3 p012 = Vector3.Lerp(p01, p12, t), p123 = Vector3.Lerp(p12, p23, t);
        Vector3 newKnotPos = Vector3.Lerp(p012, p123, t);
        data.MoveTangentOut(segmentIndex, p01 - startKnot.Position);
        data.MoveTangentIn(segmentIndex + 1, p23 - endKnot.Position);
        data.InsertKnot(segmentIndex + 1, newKnotPos, p012 - newKnotPos, p123 - newKnotPos);
    }

    public override void DeleteSegment(int flatIndex, PathData data)
    {
        if (flatIndex % 3 != 0) return;
        int knotIndex = flatIndex / 3;
        if (knotIndex >= 0 && knotIndex < data.KnotCount) data.DeleteKnot(knotIndex);
    }



    public override void DrawHandles(ref PathEditorHandles.HandleDrawContext context)
    {
        DrawCurve(ref context);
        DrawControlLines(ref context);
        DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
    }

    public override void UpdatePointHover(ref PathEditorHandles.HandleDrawContext context)
    {
        var creator = context.creator;
        // Bézier法则的悬停检测需要检查主节点和所有切线控制点
        for (int i = 0; i < creator.NumPoints; i++)
        {
            var knot = creator.pathData.GetKnot(i);
            // 检查主节点
            if (CheckHandleHover(knot.Position, i * 3, drawingStyle.knotStyle.size, ref context)) return;

            // 检查出切线
            if (i < creator.NumPoints - 1)
            {
                if (CheckHandleHover(knot.GlobalTangentOut, i * 3 + 1, drawingStyle.tangentStyle.size, ref context)) return;
            }
            // 检查入切线
            if (i > 0)
            {
                if (CheckHandleHover(knot.GlobalTangentIn, i * 3 - 1, drawingStyle.tangentStyle.size, ref context)) return;
            }
        }
    }

    private bool CheckHandleHover(Vector3 localPos, int flatIndex, float radius, ref PathEditorHandles.HandleDrawContext context)
    {
        Vector3 worldPos = context.creator.transform.TransformPoint(localPos);
        float handleRadius = HandleUtility.GetHandleSize(worldPos) * radius;
        if (HandleUtility.DistanceToCircle(worldPos, handleRadius) == 0)
        {
            context.hoveredPointIndex = flatIndex;
            return true;
        }
        return false;
    }

    private void DrawCurve(ref PathEditorHandles.HandleDrawContext context)
    {
        var creator = context.creator;
        for (int i = 0; i < creator.NumSegments; i++)
        {
            Handles.color = (i == context.hoveredSegmentIndex) ? drawingStyle.curveHoverColor : drawingStyle.curveColor;
            var knot1 = creator.pathData.GetKnot(i);
            var knot2 = creator.pathData.GetKnot(i + 1);
            Vector3 pStart = creator.transform.TransformPoint(knot1.Position);
            Vector3 pEnd = creator.transform.TransformPoint(knot2.Position);
            Vector3 ctrl1 = creator.transform.TransformPoint(knot1.GlobalTangentOut);
            Vector3 ctrl2 = creator.transform.TransformPoint(knot2.GlobalTangentIn);
            Handles.DrawBezier(pStart, pEnd, ctrl1, ctrl2, Handles.color, null, drawingStyle.curveThickness);
        }
    }

    private void DrawControlLines(ref PathEditorHandles.HandleDrawContext context)
    {
        var creator = context.creator;
        Handles.color = drawingStyle.bezierControlLineColor;
        for (int i = 0; i < creator.NumPoints; i++)
        {
            var knot = creator.pathData.GetKnot(i);
            Vector3 worldPos = creator.transform.TransformPoint(knot.Position);
            Vector3 globalTanIn = creator.transform.TransformPoint(knot.GlobalTangentIn);
            Vector3 globalTanOut = creator.transform.TransformPoint(knot.GlobalTangentOut);
            if (i > 0) Handles.DrawDottedLine(worldPos, globalTanIn, 4f);
            if (i < creator.NumPoints - 1) Handles.DrawDottedLine(worldPos, globalTanOut, 4f);
        }
    }
#if UNITY_EDITOR
    private void DrawPointHandles(ref PathEditorHandles.HandleDrawContext context, Camera camera)
    {
        var creator = context.creator;
        for (int i = 0; i < creator.NumPoints; i++)
        {
            var knot = creator.pathData.GetKnot(i);
            PathEditorHandles.DrawHandle(knot.Position, i * 3, drawingStyle.knotStyle, ref context, camera);
            if (i < creator.NumPoints - 1)
            {
                PathEditorHandles.DrawHandle(knot.GlobalTangentOut, i * 3 + 1, drawingStyle.tangentStyle, ref context, camera);
            }
            if (i > 0)
            {
                PathEditorHandles.DrawHandle(knot.GlobalTangentIn, i * 3 - 1, drawingStyle.tangentStyle, ref context, camera);
            }
        }
    }
#endif
    #endregion

#if UNITY_EDITOR

#endif

    #region 私有辅助 (Private Helpers)

    private void DecodeIndex(int flatIndex, out int knotIndex, out int pointType)
    {
        // pointType: 0 = 主节点, 1 = 出切线, 2 = 入切线
        pointType = flatIndex % 3;
        knotIndex = flatIndex / 3;

        // 入切线 (flatIndex = 3k+2) 逻辑上属于下一个节点 (k+1)
        // 但在我们的数据结构中，它与出切线一同存储在当前节点。
        // 我们的 MovePoint 逻辑需要的是切线所属节点的索引。
        // i*3-1 对应的是 knot i 的入切线。
        // i*3+1 对应的是 knot i 的出切线。

        // 因此，一个更直观的解码方式，应直接反映MovePoint的需求
        if (flatIndex == 0)
        {
            knotIndex = 0;
            pointType = 0; // 主节点
            return;
        }

        if ((flatIndex + 1) % 3 == 0) // 入切线, e.g., 2, 5, 8...
        {
            knotIndex = (flatIndex + 1) / 3;
            pointType = 2; // 入切线
        }
        else if ((flatIndex - 1) % 3 == 0) // 出切线, e.g., 1, 4, 7...
        {
            knotIndex = (flatIndex - 1) / 3;
            pointType = 1; // 出切线
        }
        else // 主节点
        {
            knotIndex = flatIndex / 3;
            pointType = 0; // 主节点
        }
    }

    private float FindTValueOnSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 point)
    {
        const int samples = 100;
        float minSqrDist = float.MaxValue, bestT = 0;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float u = 1 - t;
            Vector3 p = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
            if ((p - point).sqrMagnitude < minSqrDist)
            {
                minSqrDist = (p - point).sqrMagnitude;
                bestT = t;
            }
        }
        return bestT;
    }

    #endregion
}