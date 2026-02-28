// CatmullRomStrategy.cs
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 【最终圆满版 • 人之简约】
/// 实现了Catmull-Rom曲线的具体法则。
/// 它的美在于简洁：只关心位置，忽略切线，并用高效的采样方式绘制自身。
/// 它的所有视觉表现，都由其自身的 drawingStyle 属性来定义。
/// </summary>
[CreateAssetMenu(fileName = "CatmullRomStrategy", menuName = "MrPath/Strategies/Catmull-Rom")]
public class CatmullRomStrategy : PathStrategy
{
    #region 数学法则实现 (Math Law Implementation)

    public override Vector3 GetPointAt(float t, PathData data, Transform owner)
    {
        if (data.KnotCount == 0) return owner.position;
        if (data.SegmentCount == 0) return owner.TransformPoint(data.GetPosition(0));

        int p1_idx = Mathf.Clamp(Mathf.FloorToInt(t), 0, data.SegmentCount - 1);
        float localT = t - p1_idx;

        int p0_idx = Mathf.Clamp(p1_idx - 1, 0, data.KnotCount - 1);
        int p2_idx = Mathf.Clamp(p1_idx + 1, 0, data.KnotCount - 1);
        int p3_idx = Mathf.Clamp(p1_idx + 2, 0, data.KnotCount - 1);

        Vector3 p0 = data.GetPosition(p0_idx);
        Vector3 p1 = data.GetPosition(p1_idx);
        Vector3 p2 = data.GetPosition(p2_idx);
        Vector3 p3 = data.GetPosition(p3_idx);

        float t2 = localT * localT, t3 = t2 * localT;
        Vector3 point = 0.5f * ((2 * p1) + (-p0 + p2) * localT + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        return owner.TransformPoint(point);
    }

    public override void AddSegment(Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        data.AddKnot(owner.InverseTransformPoint(newPointWorldPos), Vector3.zero, Vector3.zero);
    }

    public override void MovePoint(int flatIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        if (flatIndex >= 0 && flatIndex < data.KnotCount)
        {
            data.MovePosition(flatIndex, owner.InverseTransformPoint(newPointWorldPos));
        }
    }

    public override void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
    {
        if (segmentIndex < 0 || segmentIndex >= data.KnotCount) return;
        data.InsertKnot(segmentIndex + 1, owner.InverseTransformPoint(newPointWorldPos), Vector3.zero, Vector3.zero);
    }

    public override void DeleteSegment(int flatIndex, PathData data)
    {
        if (flatIndex >= 0 && flatIndex < data.KnotCount)
        {
            data.DeleteKnot(flatIndex);
        }
    }



    #endregion

#if UNITY_EDITOR
    #region 绘制与交互实现 (Drawing & Interaction Implementation)

    public override void DrawHandles(ref PathEditorHandles.HandleDrawContext context)
    {
        DrawCurve(ref context);
        DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
    }

    /// <summary>
    /// 【【【 核心修正 I：赋予感知 】】】
    /// </summary>
    public override void UpdatePointHover(ref PathEditorHandles.HandleDrawContext context)
    {
        var creator = context.creator;
        // Catmull-Rom 只需检测主节点
        for (int i = 0; i < creator.NumPoints; i++)
        {
            var knot = creator.pathData.GetKnot(i);
            Vector3 worldPos = creator.transform.TransformPoint(knot.Position);

            // 使用自己的样式来计算检测半径
            float handleRadius = HandleUtility.GetHandleSize(worldPos) * drawingStyle.knotStyle.size;

            if (HandleUtility.DistanceToCircle(worldPos, handleRadius) == 0)
            {
                context.hoveredPointIndex = i;
                return; // 找到即返回
            }
        }
    }

    private void DrawCurve(ref PathEditorHandles.HandleDrawContext context)
    {
        var creator = context.creator;
        const int resolution = 20;
        var points = new Vector3[resolution + 1];

        for (int i = 0; i < creator.NumSegments; i++)
        {
            // --- 【【【 核心修正 II：身心合一 】】】 ---
            // 使用自己的样式来定义曲线颜色和粗细
            Handles.color = (i == context.hoveredSegmentIndex) ? drawingStyle.curveHoverColor : drawingStyle.curveColor;
            for (int j = 0; j <= resolution; j++)
            {
                points[j] = creator.GetPointAt(i + (float)j / resolution);
            }
            Handles.DrawAAPolyLine(drawingStyle.curveThickness, points);
        }
    }

    private void DrawPointHandles(ref PathEditorHandles.HandleDrawContext context, Camera camera)
    {
        var creator = context.creator;
        for (int i = 0; i < creator.NumPoints; i++)
        {
            var knot = creator.pathData.GetKnot(i);
            // --- 【【【 核心修正 II：身心合一 】】】 ---
            // 调用共享神通时，传入自己的样式
            PathEditorHandles.DrawHandle(knot.Position, i, drawingStyle.knotStyle, ref context, camera);
        }
    }

    #endregion
#endif
}