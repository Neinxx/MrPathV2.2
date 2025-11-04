using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Preview;
using MrPathV2.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Runtime.Strategies
{
    /// <summary>
    ///     Defines the mathematical and editor behavior for a Catmull-Rom spline path.
    /// </summary>
    [CreateAssetMenu(fileName = "CatmullRomStrategy", menuName = "MrPath/Strategies/Catmull-Rom")]
    public class CatmullRomStrategy : PathStrategy
    {
        #region Math & Data Implementation

        public override Vector3 GetPointAt(float t, PathData data)
        {
            if (data.KnotCount < 2)
                return data.KnotCount == 1 ? data.GetPosition(0) : Vector3.zero;

            // Determine the primary segment index and the normalized time within it
            var p1Index = Mathf.FloorToInt(t);
            var localT = t - p1Index;

            // A segment is defined by the knot at p1Index and p1Index + 1
            p1Index = Mathf.Clamp(p1Index, 0, data.SegmentCount - 1);

            // Get the four control points for the Catmull-Rom calculation, clamping to valid knot indices
            var p0 = data.GetPosition(Mathf.Max(0, p1Index - 1));
            var p1 = data.GetPosition(p1Index);
            var p2 = data.GetPosition(Mathf.Min(data.KnotCount - 1, p1Index + 1));
            var p3 = data.GetPosition(Mathf.Min(data.KnotCount - 1, p1Index + 2));

            // Standard Catmull-Rom spline formula for a uniform spline
            var t2 = localT * localT;
            var t3 = t2 * localT;

            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * localT +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
                );
        }

        // Using expression-bodied members for concise, single-line methods
        public override void AddSegment(Vector3 newPointWorldPos, PathData data, Transform owner) =>
            data.AddKnot(owner.InverseTransformPoint(newPointWorldPos), Vector3.zero, Vector3.zero);

        public override void MovePoint(int flatIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
        {
            if (flatIndex >= 0 && flatIndex < data.KnotCount)
                data.MovePosition(flatIndex, owner.InverseTransformPoint(newPointWorldPos));
        }

        public override void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
        {
            if (segmentIndex >= 0 && segmentIndex < data.KnotCount)
                data.InsertKnot(segmentIndex + 1, owner.InverseTransformPoint(newPointWorldPos), Vector3.zero, Vector3.zero);
        }

        public override void DeleteSegment(int flatIndex, PathData data)
        {
            if (flatIndex >= 0 && flatIndex < data.KnotCount)
                data.DeleteKnot(flatIndex);
        }

        #endregion

#if UNITY_EDITOR
        #region Editor Drawing & Interaction

        private const int MinCurveResolution = 4;
        private const int MaxCurveResolution = 128;
        private const float ResolutionScalar = 10f;


        public override void DrawHandles(ref PathEditorHandles.HandleDrawContext context)
        {
            DrawCurve(ref context);
            DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
        }

        public override void UpdatePointHover(ref PathEditorHandles.HandleDrawContext context)
        {
            for (var i = 0; i < context.Creator.NumPoints; i++)
            {
                var worldPos = context.Creator.transform.TransformPoint(context.Creator.pathData.GetPosition(i));
                var handleRadius = HandleUtility.GetHandleSize(worldPos) * drawingStyle.knotStyle.size;

                if (HandleUtility.DistanceToCircle(worldPos, handleRadius) == 0)
                {
                    context.HoveredPointIndex = i;
                    return;
                }
            }
        }

        private void DrawCurve(ref PathEditorHandles.HandleDrawContext context)
        {
            var creator = context.Creator;
            var lineRenderer = context.LineRenderer;
            var shouldDispose = false;

            if (lineRenderer == null)
            {
                lineRenderer = new PreviewLineRenderer();
                shouldDispose = true;
            }

            try
            {
                lineRenderer.Clear(PreviewLineRenderer.LineType.PathCurve);
                lineRenderer.SetCamera(SceneView.currentDrawingSceneView.camera);

                // 曲线绘制分辨率不再依赖 Profile 参数，按段数动态估计
                var resolution = Mathf.Clamp(Mathf.RoundToInt(creator.NumSegments * 16f), MinCurveResolution, MaxCurveResolution);
                var controlPoints = new Vector3[4]; // Pre-allocate to avoid GC alloc in loop

                for (var i = 0; i < creator.NumSegments; i++)
                {
                    var curveStyle = new PreviewLineRenderer.LineStyle
                    {
                        color = i == context.HoveredSegmentIndex ? drawingStyle.curveHoverColor : drawingStyle.curveColor,
                        thickness = drawingStyle.curveThickness,
                        antiAliased = true
                    };

                    GetCatmullRomControlPoints(creator, i, controlPoints);
                    lineRenderer.AddCatmullRomSpline(controlPoints, PreviewLineRenderer.LineType.PathCurve, resolution, curveStyle);
                }
                lineRenderer.Render();
            }
            finally
            {
                if (shouldDispose)
                {
                    lineRenderer.Dispose();
                }
            }
        }

        private void GetCatmullRomControlPoints(PathCreator creator, int segmentIndex, Vector3[] buffer)
        {
            // Ensure buffer is valid
            if (buffer == null || buffer.Length < 4)
                buffer = new Vector3[4];

            // Determine the indices of the four points defining the spline segment
            var p0Idx = Mathf.Clamp(segmentIndex - 1, 0, creator.NumPoints - 1);
            var p1Idx = Mathf.Clamp(segmentIndex, 0, creator.NumPoints - 1);
            var p2Idx = Mathf.Clamp(segmentIndex + 1, 0, creator.NumPoints - 1);
            var p3Idx = Mathf.Clamp(segmentIndex + 2, 0, creator.NumPoints - 1);

            // Populate the buffer with world-space positions
            buffer[0] = creator.transform.TransformPoint(creator.pathData.GetPosition(p0Idx));
            buffer[1] = creator.transform.TransformPoint(creator.pathData.GetPosition(p1Idx));
            buffer[2] = creator.transform.TransformPoint(creator.pathData.GetPosition(p2Idx));
            buffer[3] = creator.transform.TransformPoint(creator.pathData.GetPosition(p3Idx));
        }

        private void DrawPointHandles(ref PathEditorHandles.HandleDrawContext context, Camera camera)
        {
            for (var i = 0; i < context.Creator.NumPoints; i++)
            {
                var localPos = context.Creator.pathData.GetPosition(i);
                PathEditorHandles.DrawHandle(localPos, i, drawingStyle.knotStyle, ref context, camera);
            }
        }

        public override PreviewLineRenderer.LineType GetLineTypeToCleanup()
        {
            return PreviewLineRenderer.LineType.ControlLine;
        }

        #endregion
#endif
    }
}
