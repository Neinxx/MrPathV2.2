using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Preview;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace MrPathV2.Runtime.Strategies
{
    /// <summary>
    ///     【最终圆满版 • 千变万化之法】
    /// </summary>
    [CreateAssetMenu(fileName = "BezierStrategy", menuName = "MrPath/Strategies/Bezier")]
    public partial class BezierStrategy : PathStrategy
    {
        [Header("法则参数")]
        [Tooltip("在添加新节点时，自动生成的切线长度与线段长度的比例。")]
        [Range(0.05f, 1.0f)]
        public float defaultTangentScale = 0.333f;

        #region 数学法则实现

        public override Vector3 GetPointAt(float t, PathData data)
        {
            // --- 修正后的守护逻辑 ---
            if (data.KnotCount == 0) return Vector3.zero;
            if (data.SegmentCount == 0) return data.GetPosition(0);

            // --- 核心计算逻辑 (保持不变) ---
            var segmentIndex = Mathf.Clamp(Mathf.FloorToInt(t), 0, data.SegmentCount - 1);
            var localT = t - segmentIndex;

            var startKnot = data.GetKnot(segmentIndex);
            var endKnot = data.GetKnot(segmentIndex + 1);

            // --- 【【【 最关键的修正 】】】 ---
            // 所有的点现在都在纯粹的本地空间中定义
            // p1 和 p2 是控制点，它们的位置是锚点位置加上其相对的切线向量
            var p0 = startKnot.Position;
            var p1 = startKnot.Position + startKnot.TangentOut; // 使用相对切线
            var p2 = endKnot.Position + endKnot.TangentIn; // 使用相对切线
            var p3 = endKnot.Position;

            // --- 贝塞尔曲线公式 (保持不变) ---
            var u = 1 - localT;
            var tSq = localT * localT;
            var uSq = u * u;

            var point = uSq * u * p0 + 3 * uSq * localT * p1 + 3 * u * tSq * p2 + tSq * localT * p3;

            // --- 最终返回：纯粹的本地坐标 ---
            return point;
        }

        public override void AddSegment(Vector3 newPointWorldPos, PathData data, Transform owner)
        {
            var newPos = owner.InverseTransformPoint(newPointWorldPos);
            if (data.KnotCount == 0)
            {
                data.AddKnot(newPos, Vector3.zero, Vector3.zero);
                return;
            }
            var lastIndex = data.KnotCount - 1;
            var lastPos = data.GetPosition(lastIndex);
            var offset = (newPos - lastPos) * defaultTangentScale;
            data.MoveTangentOut(lastIndex, offset);
            data.AddKnot(newPos, -offset, Vector3.zero);
        }

        public override void MovePoint(int flatIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
        {
            var newLocalPos = owner.InverseTransformPoint(newPointWorldPos);
            DecodeIndex(flatIndex, out var knotIndex, out var pointType);
            if (knotIndex < 0 || knotIndex >= data.KnotCount) return;
            if (pointType == 0) data.MovePosition(knotIndex, newLocalPos);
            else if (pointType == 1) data.MoveTangentOut(knotIndex, newLocalPos - data.GetPosition(knotIndex));
            else data.MoveTangentIn(knotIndex, newLocalPos - data.GetPosition(knotIndex));
        }

        public override void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, PathData data, Transform owner)
        {
            if (segmentIndex >= data.SegmentCount)
            {
                AddSegment(newPointWorldPos, data, owner);
                return;
            }
            var startKnot = data.GetKnot(segmentIndex);
            var endKnot = data.GetKnot(segmentIndex + 1);
            Vector3 p0 = startKnot.Position, p1 = startKnot.GlobalTangentOut, p2 = endKnot.GlobalTangentIn, p3 = endKnot.Position;
            var t = FindTValueOnSegment(p0, p1, p2, p3, owner.InverseTransformPoint(newPointWorldPos));
            Vector3 p01 = Vector3.Lerp(p0, p1, t), p12 = Vector3.Lerp(p1, p2, t), p23 = Vector3.Lerp(p2, p3, t);
            Vector3 p012 = Vector3.Lerp(p01, p12, t), p123 = Vector3.Lerp(p12, p23, t);
            var newKnotPos = Vector3.Lerp(p012, p123, t);
            data.MoveTangentOut(segmentIndex, p01 - startKnot.Position);
            data.MoveTangentIn(segmentIndex + 1, p23 - endKnot.Position);
            data.InsertKnot(segmentIndex + 1, newKnotPos, p012 - newKnotPos, p123 - newKnotPos);
        }

        public override void DeleteSegment(int flatIndex, PathData data)
        {
            if (flatIndex % 3 != 0) return;
            var knotIndex = flatIndex / 3;
            if (knotIndex >= 0 && knotIndex < data.KnotCount) data.DeleteKnot(knotIndex);
        }


#if UNITY_EDITOR
        public override void DrawHandles(ref PathEditorHandles.HandleDrawContext context)
        {
            // Splines风格：在非重绘事件也必须绘制句柄以保证可交互，重负载曲线绘制仅在Repaint执行
#if UNITY_EDITOR
            var evt = Event.current;
            if (context.UseSplinesStyle && evt != null && evt.type != EventType.Repaint)
            {
                DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
                return;
            }
#endif
            // 统一相机设置与单次渲染：避免每帧重复Render导致卡顿
            var lineRenderer = context.LineRenderer;
            if (lineRenderer == null)
            {
                DrawCurve(ref context);
                DrawControlLines(ref context);
                DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
                return;
            }

            lineRenderer.SetCamera(SceneView.currentDrawingSceneView.camera);

            DrawCurve(ref context);
            DrawControlLines(ref context);
            // 单次渲染提交，减少批次更新与排序开销（非Splines风格时才提交）
            if (!context.UseSplinesStyle)
            {
                lineRenderer.Render();
            }
            DrawPointHandles(ref context, SceneView.currentDrawingSceneView.camera);
        }

        public override void UpdatePointHover(ref PathEditorHandles.HandleDrawContext context)
        {
            var creator = context.Creator;
            var knotRadius = drawingStyle != null && drawingStyle.knotStyle != null ? drawingStyle.knotStyle.size : 0.1f;
            var tangentRadius = drawingStyle != null && drawingStyle.tangentStyle != null ? drawingStyle.tangentStyle.size : 0.1f;
            // Bézier法则的悬停检测需要检查主节点和所有切线控制点
            for (var i = 0; i < creator.NumPoints; i++)
            {
                var knot = creator.pathData.GetKnot(i);
                // 检查主节点
                if (CheckHandleHover(knot.Position, i * 3, knotRadius, ref context)) return;

                // 检查出切线
                if (i < creator.NumPoints - 1)
                {
                    if (CheckHandleHover(knot.GlobalTangentOut, i * 3 + 1, tangentRadius, ref context)) return;
                }
                // 检查入切线
                if (i > 0)
                {
                    if (CheckHandleHover(knot.GlobalTangentIn, i * 3 - 1, tangentRadius, ref context)) return;
                }
            }
        }

        private bool CheckHandleHover(Vector3 localPos, int flatIndex, float radius, ref PathEditorHandles.HandleDrawContext context)
        {
            var worldPos = context.Creator.transform.TransformPoint(localPos);
            var handleRadius = HandleUtility.GetHandleSize(worldPos) * radius;
            if (HandleUtility.DistanceToCircle(worldPos, handleRadius) == 0)
            {
                context.HoveredPointIndex = flatIndex;
                return true;
            }
            return false;
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

                // 场景相机移动时（且非拖拽句柄），Splines风格下跳过曲线绘制以保障流畅度
#if UNITY_EDITOR
                if (context.UseSplinesStyle && !context.IsDragging && context.IsCameraMoving)
                {
                    return;
                }
#endif

                // 屏幕采样步长（像素）改为通过上下文依赖注入，移除 Runtime 对 Editor 的依赖
                var pixelStep = context.PreviewMaxPixelStep > 0f
                    ? Mathf.Clamp(context.PreviewMaxPixelStep, 2f, 24f)
                    : 6f; // 兜底默认值
                // 拖拽时降低采样密度：增大像素步长以提升帧率
                if (context.IsDragging)
                {
                    pixelStep = Mathf.Max(pixelStep, 12f);
                }

                // 拖拽期间降低最小分辨率上限，进一步减压计算
                var minRes = context.IsDragging ? 4 : 8;
                var maxRes = context.IsDragging ? 1024 : 2048;

                // Editor下：一次性计算相机与视锥体，用于剔除不可见段
#if UNITY_EDITOR
                var cam = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView.camera : null;
                var planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
#endif

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

                    GetBezierControlPointsWorld(creator, i, out var pStart, out var pEnd, out var ctrl1, out var ctrl2);

#if UNITY_EDITOR
                    if (context.UseSplinesStyle)
                    {
                        // 视锥体剔除：段包围盒不在视野则跳过
                        if (planes != null)
                        {
                            var bounds = new Bounds(pStart, Vector3.zero);
                            bounds.Encapsulate(pEnd);
                            bounds.Encapsulate(ctrl1);
                            bounds.Encapsulate(ctrl2);
                            if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                                continue;
                        }
                        // 使用 AA 折线并从共享缓存获取采样点，降低每帧计算与GC
                        var lowQuality = context.IsDragging || (!context.IsDragging && context.IsCameraMoving);
                        var pts = context.PolylineCache != null
                            ? context.PolylineCache.GetBezier(i, pStart, ctrl1, ctrl2, pEnd, pixelStep, cam, lowQuality)
                            : SampleBezierPoints(pStart, ctrl1, ctrl2, pEnd, lowQuality ? 12 : 28);
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
                        // 屏幕自适应采样（像素步长）：在SceneView中保证平滑且避免过采样
                        lineRenderer.AddBezierCurveAdaptive(pStart, pEnd, ctrl1, ctrl2,
                            PreviewLineRenderer.LineType.PathCurve, maxPixelStep: pixelStep, customStyle: curveStyle,
                            minResolution: minRes, maxResolution: maxRes);
                    }
                }
        }

        private static void GetBezierControlPointsWorld(PathCreator creator, int segmentIndex, out Vector3 pStart, out Vector3 pEnd, out Vector3 ctrl1, out Vector3 ctrl2)
        {
            var knot1 = creator.pathData.GetKnot(segmentIndex);
            var knot2 = creator.pathData.GetKnot(segmentIndex + 1);
            pStart = creator.transform.TransformPoint(knot1.Position);
            pEnd = creator.transform.TransformPoint(knot2.Position);
            ctrl1 = creator.transform.TransformPoint(knot1.GlobalTangentOut);
            ctrl2 = creator.transform.TransformPoint(knot2.GlobalTangentIn);
        }

        private void DrawControlLines(ref PathEditorHandles.HandleDrawContext context)
        {
            var creator = context.Creator;
            var lineRenderer = context.LineRenderer;
            if (lineRenderer == null && !context.UseSplinesStyle) return; // 依赖注入：未提供则不绘制
            // 拖拽时跳过控制线（Unity Splines 行为的简化版），降低绘制负载
            if (context.IsDragging) return;
            // 相机移动时也跳过控制线，进一步减少负载
            if (context.UseSplinesStyle && context.IsCameraMoving) return;

            // 清除上一帧遗留的控制线，避免重复累积导致卡顿（Splines风格下改为直接绘制）
            if (!context.UseSplinesStyle)
            {
                lineRenderer.Clear(PreviewLineRenderer.LineType.ControlLine);
            }

                // 设置控制线样式
                var controlLineStyle = new PreviewLineRenderer.LineStyle
                {
                    color = drawingStyle.bezierControlLineColor,
                    thickness = 1f,
                    dashed = true,
                    dashSize = 4f,
                    antiAliased = true
                };

                // 添加控制线到渲染器
                for (var i = 0; i < creator.NumPoints; i++)
                {
                    var knot = creator.pathData.GetKnot(i);
                    var worldPos = creator.transform.TransformPoint(knot.Position);
                    var globalTanIn = creator.transform.TransformPoint(knot.GlobalTangentIn);
                    var globalTanOut = creator.transform.TransformPoint(knot.GlobalTangentOut);

                    if (i > 0)
                    {
#if UNITY_EDITOR
                        if (context.UseSplinesStyle)
                        {
                            Handles.DrawDottedLine(worldPos, globalTanIn, controlLineStyle.dashSize);
                        }
                        else
#endif
                        {
                            lineRenderer.AddLine(worldPos, globalTanIn, PreviewLineRenderer.LineType.ControlLine, controlLineStyle);
                        }
                    }
                    if (i < creator.NumPoints - 1)
                    {
#if UNITY_EDITOR
                        if (context.UseSplinesStyle)
                        {
                            Handles.DrawDottedLine(worldPos, globalTanOut, controlLineStyle.dashSize);
                        }
                        else
#endif
                        {
                            lineRenderer.AddLine(worldPos, globalTanOut, PreviewLineRenderer.LineType.ControlLine, controlLineStyle);
                        }
                    }
                }

                // 渲染所有线条
                // 统一由 DrawHandles 进行单次渲染提交
        }
#if UNITY_EDITOR
        private void DrawPointHandles(ref PathEditorHandles.HandleDrawContext context, Camera camera)
        {
            var creator = context.Creator;
            int startKnot = 0, endKnot = creator.NumPoints;
            if (context.UseSplinesStyle && context.IsDragging && context.DrawActiveSegmentOnly && context.HoveredSegmentIndex >= 0)
            {
                // 仅绘制当前段的两端节点以及其切线
                startKnot = Mathf.Max(0, context.HoveredSegmentIndex);
                endKnot = Mathf.Min(creator.NumPoints, context.HoveredSegmentIndex + 2);
            }

            for (var i = startKnot; i < endKnot; i++)
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
#endif

        #endregion

        #region 私有辅助 (Private Helpers)
        private static Vector3[] s_BezierBuffer;

        private static Vector3[] SampleBezierPoints(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int resolution)
        {
            var count = resolution + 1;
            if (s_BezierBuffer == null || s_BezierBuffer.Length < count)
                s_BezierBuffer = new Vector3[count];
            for (int i = 0; i <= resolution; i++)
            {
                var t = i / (float)resolution;
                s_BezierBuffer[i] = CalculateBezierPoint(p0, p1, p2, p3, t);
            }
            return s_BezierBuffer;
        }

        private static Vector3 CalculateBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;
            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

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
            for (var i = 0; i <= samples; i++)
            {
                var t = (float)i / samples;
                var u = 1 - t;
                var p = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
                if ((p - point).sqrMagnitude < minSqrDist)
                {
                    minSqrDist = (p - point).sqrMagnitude;
                    bestT = t;
                }
            }
            return bestT;
        }

        public override PreviewLineRenderer.LineType GetLineTypeToCleanup() => PreviewLineRenderer.LineType.PathCurve;

        #endregion
    }
}
