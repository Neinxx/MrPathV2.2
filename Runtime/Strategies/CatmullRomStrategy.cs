using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Runtime.Strategies
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
            // Splines风格：非重绘事件也要绘制句柄，重负载曲线绘制仅在Repaint执行
#if UNITY_EDITOR
            var evt = Event.current;
            if (context.UseSplinesStyle && evt != null && evt.type != EventType.Repaint)
            {
                DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
                return;
            }
#endif
            // 统一相机设置与单次渲染，减少重复批次更新
            var lineRenderer = context.LineRenderer;
            if (lineRenderer != null)
            {
                lineRenderer.SetCamera(SceneView.currentDrawingSceneView.camera);
            }

            DrawCurve(ref context);
            DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
            if (lineRenderer != null && !context.UseSplinesStyle)
            {
                lineRenderer.Render();
            }
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
            // 依赖注入：若未提供共享渲染器则提前返回，避免临时实例
            if (lineRenderer == null && !context.UseSplinesStyle) return;
            if (!context.UseSplinesStyle)
            {
                lineRenderer.Clear(PreviewLineRenderer.LineType.PathCurve);
            }

#if UNITY_EDITOR
            // 相机移动时（且非拖拽句柄），Splines风格下跳过曲线绘制保证场景拖动流畅
            if (context.UseSplinesStyle && !context.IsDragging && context.IsCameraMoving)
            {
                return;
            }
#endif

            var controlPoints = new Vector3[4]; // Pre-allocate to avoid GC alloc in loop

            // 屏幕采样步长（像素）通过上下文依赖注入，移除 Runtime 对 Editor 的依赖
            var pixelStep = context.PreviewMaxPixelStep > 0f
                ? Mathf.Clamp(context.PreviewMaxPixelStep, 2f, 24f)
                : 6f; // 兜底默认值
            if (context.IsDragging)
            {
                pixelStep = Mathf.Max(pixelStep, 12f);
            }

            var minRes = context.IsDragging ? 4 : 8;
            var maxRes = context.IsDragging ? 512 : 1024;

            int startSeg = 0, endSeg = creator.NumSegments;
            if (context.UseSplinesStyle && context.IsDragging && context.DrawActiveSegmentOnly && context.HoveredSegmentIndex >= 0)
            {
                startSeg = Mathf.Max(0, context.HoveredSegmentIndex - context.DragNeighborRange);
                endSeg = Mathf.Min(creator.NumSegments, context.HoveredSegmentIndex + context.DragNeighborRange + 1);
            }

            for (var i = startSeg; i < endSeg; i++)
            {
                var color = i == context.HoveredSegmentIndex ? drawingStyle.curveHoverColor : drawingStyle.curveColor;
                var thickness = drawingStyle.curveThickness;

                GetCatmullRomControlPoints(creator, i, controlPoints);
                // 屏幕自适应采样：根据相机与像素步长估算每段分辨率，确保编辑平滑
                
#if UNITY_EDITOR
                if (context.UseSplinesStyle)
                {
                    // 直接在 Editor 使用 AA 折线绘制，并复用共享缓存以减少采样与GC
                    var cam = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView.camera : null;
                    var lowQuality = context.IsDragging || (!context.IsDragging && context.IsCameraMoving);
                    var pts = context.PolylineCache != null
                        ? context.PolylineCache.GetCatmull(i, controlPoints[0], controlPoints[1], controlPoints[2], controlPoints[3], pixelStep, cam, lowQuality)
                        : SampleCatmullRom(controlPoints, lowQuality ? 8 : 32);
                    var oldColor = Handles.color;
                    try
                    {
                        Handles.color = color;
                        Handles.DrawAAPolyLine(thickness, pts);
                    }
                    finally
                    {
                        Handles.color = oldColor;
                    }
                }
                else
#endif
                {
                    var curveStyle = new PreviewLineRenderer.LineStyle
                    {
                        color = color,
                        thickness = thickness,
                        antiAliased = true
                    };
                    lineRenderer.AddCatmullRomSplineAdaptive(controlPoints, PreviewLineRenderer.LineType.PathCurve,
                        maxPixelStep: pixelStep, customStyle: curveStyle, minResolution: minRes, maxResolution: maxRes);
                }
            }
        }

        // 局部快速采样：避免调度Job与NativeArray分配，在拖拽预览下更顺滑
        private static Vector3[] s_CatmullBuffer;
        private static Vector3[] SampleCatmullRom(Vector3[] cps, int resolution)
        {
            var count = resolution + 1;
            if (s_CatmullBuffer == null || s_CatmullBuffer.Length < count)
                s_CatmullBuffer = new Vector3[count];
            var p0 = cps[0];
            var p1 = cps[1];
            var p2 = cps[2];
            var p3 = cps[3];
            for (int i = 0; i <= resolution; i++)
            {
                var t = i / (float)resolution;
                s_CatmullBuffer[i] = CalculateCatmullPoint(p0, p1, p2, p3, t);
            }
            return s_CatmullBuffer;
        }

        private static Vector3 CalculateCatmullPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var tt = t * t;
            var ttt = tt * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
            );
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
            int startKnot = 0, endKnot = context.Creator.NumPoints;
            if (context.UseSplinesStyle && context.IsDragging && context.DrawActiveSegmentOnly && context.HoveredSegmentIndex >= 0)
            {
                startKnot = Mathf.Max(0, context.HoveredSegmentIndex);
                endKnot = Mathf.Min(context.Creator.NumPoints, context.HoveredSegmentIndex + 2);
            }

            for (var i = startKnot; i < endKnot; i++)
            {
                var localPos = context.Creator.pathData.GetPosition(i);
                PathEditorHandles.DrawHandle(localPos, i, drawingStyle.knotStyle, ref context, camera);
            }
        }

        public override PreviewLineRenderer.LineType GetLineTypeToCleanup() => PreviewLineRenderer.LineType.ControlLine;

        #endregion
#endif
    }
}
