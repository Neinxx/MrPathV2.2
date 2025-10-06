// 请用此最终、完美的完整代码，替换你的 PathEditorHandles.cs

using UnityEditor;
using UnityEngine;

/// <summary>
/// 【洞悉三界之眼 • 终极完美版】
/// 专职绘制路径及其交互Handles的“护法”。其法度严谨，性能卓绝，画风优雅。
/// </summary>
public static class PathEditorHandles
{
    #region 法术常数 (Constants)
    private const float CURVE_THICKNESS = 4.5f;
    private const float POINT_ANCHOR_SIZE = 0.15f;
    private const float POINT_CONTROL_SIZE = 0.09f;
    private const float POINT_HOVER_SCALE = 1.2f;
    private const float INSERT_PREVIEW_SIZE = 0.12f;
    private const float POINT_SIZE_NORMAL = 0.12f;
    private const int CURVE_DRAW_RESOLUTION = 20; // 为非贝塞尔曲线定义绘制精度
    private static readonly Color COLOR_CURVE_IDEAL = Color.white;
    private static readonly Color COLOR_CURVE_SNAPPED = new Color(1, 0.9f, 0.4f);
    private static readonly Color COLOR_CURVE_HOVER = new Color(0.2f, 0.9f, 1f);
    private static readonly Color COLOR_POINT_ANCHOR_FILL = new Color(0.2f, 1f, 0.3f, 0.25f);
    private static readonly Color COLOR_POINT_ANCHOR_BORDER = new Color(0.2f, 1f, 0.3f, 0.9f);
    private static readonly Color COLOR_POINT_CONTROL_FILL = new Color(1f, 1f, 1f, 0.25f);
    private static readonly Color COLOR_POINT_CONTROL_BORDER = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color COLOR_POINT_HOVER_FILL = new Color(1, 0.5f, 0, 0.3f);
    private static readonly Color COLOR_POINT_HOVER_BORDER = new Color(1, 0.5f, 0, 1f);
    private static readonly Color COLOR_INSERT_PREVIEW_FILL = new Color(1f, 0.9f, 0.4f, 0.25f);
    private static readonly Color COLOR_INSERT_PREVIEW_BORDER = new Color(1f, 0.9f, 0.4f, 0.9f);
    private static readonly Color COLOR_BEZIER_LINE = new Color(1f, 1f, 1f, 0.4f);
    #endregion

    #region 公共结构 (Public Structs)
    public struct HandleDrawContext
    {
        public PathCreator creator;
        public TerrainHeightProvider heightProvider;
        public PathSpine? latestSpine;
        public bool isDragging;
        public int hoveredPointIndex;
        public int hoveredSegmentIndex;
        public float hoveredPathT;
    }
    #endregion

    // 增加一个静态缓存，避免在绘制循环中反复分配内存
    private static Vector3[] _curveSamplePointsCache = new Vector3[CURVE_DRAW_RESOLUTION + 1];


    #region 主入口 (Main Entry)
    public static void Draw(ref HandleDrawContext context)
    {
        if (context.creator == null || context.creator.Path == null) return;
        var camera = SceneView.currentDrawingSceneView.camera;
        if (camera == null) return;

        UpdateHoverState(ref context);
        DrawCurve(ref context);
        DrawBezierControlLines(ref context);
        DrawInsertionPreviewHandle(ref context, camera);
        DrawPointHandles(ref context, camera);
    }
    #endregion

    #region 绘制方法 (Drawing Methods)

    // 【【【 核心修正：画龙点睛 】】】
    /// <summary>
    /// 绘制“悬空宝珠”——一个永远朝向镜头的、半透明的预览圆盘。
    /// </summary>
    private static void DrawInsertionPreviewHandle(ref HandleDrawContext context, Camera camera)
    {
        Event e = Event.current;
        if (e.shift && !e.control && context.hoveredPathT > -1)
        {
            Vector3 previewPos = context.creator.GetPointAt(context.hoveredPathT);
            float handleSize = HandleUtility.GetHandleSize(previewPos);
            Vector3 cameraForward = camera.transform.forward;

            // 绘制半透明的底盘
            Handles.color = COLOR_INSERT_PREVIEW_FILL;
            Handles.DrawSolidDisc(previewPos, cameraForward, handleSize * INSERT_PREVIEW_SIZE);

            // 绘制清晰的边缘
            Handles.color = COLOR_INSERT_PREVIEW_BORDER;
            Handles.DrawWireDisc(previewPos, cameraForward, handleSize * INSERT_PREVIEW_SIZE, 1.5f);
        }
    }

    // ... 其他所有方法保持不变 ...

    #region 状态更新 (State Update)
    private static void UpdateHoverState(ref HandleDrawContext context)
    {
        if (context.isDragging) return;
        UpdatePointHover(ref context);
        if (context.hoveredPointIndex == -1) UpdatePathHover(ref context);
        else
        {
            context.hoveredSegmentIndex = -1;
            context.hoveredPathT = -1;
        }
    }
    private static void UpdatePointHover(ref HandleDrawContext context)
    {
        context.hoveredPointIndex = -1;
        var creator = context.creator;

        // --- 【【【 核心性能优化 I：根除性能心魔 】】】 ---
        // 之前这里调用 GetPointsForMigration() 极其低效，因为它可能每次都创建一个新列表。
        // 现在直接调用 PathCreator 封装好的 GetPoint() 方法，它通过接口高效地获取数据。
        for (int i = 0; i < creator.NumPoints; i++)
        {
            // 对于Bezier路径，这会返回锚点。对于CatmullRom，这返回每个点。
            // 注意：此处的NumPoints对于Bezier返回的是锚点数，所以只会对锚点进行悬停检测。
            // 如果需要对控制点也进行悬停，需要更复杂的逻辑，但当前设计是合理的。
            Vector3 worldPos = creator.GetPoint(i);
            float handleRadius = HandleUtility.GetHandleSize(worldPos) * POINT_SIZE_NORMAL;
            if (HandleUtility.DistanceToCircle(worldPos, handleRadius) == 0)
            {
                context.hoveredPointIndex = i;
                return;
            }
        }
    }

    private static void UpdatePathHover(ref HandleDrawContext context)
    {
        Event e = Event.current; Vector2 mousePos = e.mousePosition;
        float minSqrDist = float.MaxValue; float bestT = -1f;
        IPath path = context.creator.Path;
        if (path.NumSegments == 0) return;

        // 【【【 核心修正 I：绝对精准的神念 】】】
        // 无论是否吸附，交互的根基永远是理想的数学曲线，以确保逻辑的绝对一致
        for (int i = 0; i < path.NumSegments; i++)
        {
            // 大幅提高采样密度，以应对各种曲线和缩放级别
            const int steps = 100;
            Vector3 prevPoint = context.creator.GetPointAt(i);
            for (int j = 1; j <= steps; j++)
            {
                float t_end = i + (float)j / steps;
                Vector3 currentPoint = context.creator.GetPointAt(t_end);

                Vector2 p1_gui = HandleUtility.WorldToGUIPoint(prevPoint);
                Vector2 p2_gui = HandleUtility.WorldToGUIPoint(currentPoint);

                Vector2 projectedPoint = ProjectPointOnLineSegment(p1_gui, p2_gui, mousePos);
                float sqrDist = (mousePos - projectedPoint).sqrMagnitude;

                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    float segmentLengthSqr = (p2_gui - p1_gui).sqrMagnitude;
                    if (segmentLengthSqr < 0.001f)
                    {
                        bestT = i;
                    }
                    else
                    {
                        // 通过投影精确计算t值在0-1区间的比例
                        float segmentT = Vector2.Dot(projectedPoint - p1_gui, p2_gui - p1_gui) / segmentLengthSqr;
                        // 将此比例应用到当前采样的小步长中
                        float t_start = i + (float)(j - 1) / steps;
                        bestT = Mathf.Lerp(t_start, t_end, segmentT);
                    }
                }
                prevPoint = currentPoint;
            }
        }

        context.hoveredPathT = bestT;
        context.hoveredSegmentIndex = (bestT > -1) ? Mathf.FloorToInt(bestT) : -1;
    }
    #endregion


    // 新增辅助神通：计算点在2D线段上的投影
    private static Vector2 ProjectPointOnLineSegment(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        Vector2 lineDir = lineEnd - lineStart;
        float lineLengthSqr = lineDir.sqrMagnitude;
        if (lineLengthSqr < 0.0001f) return lineStart;
        float t = Vector2.Dot(point - lineStart, lineDir) / lineLengthSqr;
        return lineStart + lineDir * Mathf.Clamp01(t);
    }
    private static readonly Vector3[] _bezierSegmentPointsCache = new Vector3[4];


    private static void DrawCurve(ref HandleDrawContext context)
    {
        IPath path = context.creator.Path;
        Handles.color = COLOR_CURVE_IDEAL;

        for (int i = 0; i < path.NumSegments; i++)
        {
            Handles.color = (i == context.hoveredSegmentIndex) ? COLOR_CURVE_HOVER : COLOR_CURVE_IDEAL;

            if (path is BezierPath bezierPath)
            {
                // --- 【【【 核心清晰度优化：明晰天道 】】】 ---
                // 不再使用 creator.GetPoint(i*3+1) 这种依赖“魔法数字”的调用。
                // 既然已经知道是 BezierPath，就调用它更清晰、更类型安全的专用接口。
                Vector3 pStart = context.creator.GetPoint(i);
                Vector3 pEnd = context.creator.GetPoint(i + 1);
                Vector3 ctrl1 = bezierPath.GetControlPoint(i, 1, context.creator.transform);
                Vector3 ctrl2 = bezierPath.GetControlPoint(i + 1, 2, context.creator.transform);
                Handles.DrawBezier(pStart, pEnd, ctrl1, ctrl2, Handles.color, null, CURVE_THICKNESS);
            }
            else
            {
                // --- 【【【 核心BUG修正：解放曲线之魂 】】】 ---
                // 不再是画直线！而是通过在曲线上采样一系列点，然后将这些点连接起来，从而绘制出真正的曲线。
                _curveSamplePointsCache[0] = context.creator.GetPointAt(i);
                for (int j = 1; j <= CURVE_DRAW_RESOLUTION; j++)
                {
                    float t = i + (float)j / CURVE_DRAW_RESOLUTION;
                    _curveSamplePointsCache[j] = context.creator.GetPointAt(t);
                }
                Handles.DrawAAPolyLine(CURVE_THICKNESS, _curveSamplePointsCache);
            }
        }
    }
    private static void DrawPointHandles(ref HandleDrawContext context, Camera camera)
    {
        var creator = context.creator;
        for (int i = 0; i < creator.NumPoints; i++)
        {
            Vector3 handleDrawPos = GetHandleDrawPosition(ref context, i);

            bool isAnchor = creator.Path is not BezierPath || i % 3 == 0;
            bool isHovered = (i == context.hoveredPointIndex);

            float size = isAnchor ? POINT_ANCHOR_SIZE : POINT_CONTROL_SIZE;
            if (isHovered) size *= POINT_HOVER_SCALE;

            Color fillColor = isHovered ? COLOR_POINT_HOVER_FILL : (isAnchor ? COLOR_POINT_ANCHOR_FILL : COLOR_POINT_CONTROL_FILL);
            Color borderColor = isHovered ? COLOR_POINT_HOVER_BORDER : (isAnchor ? COLOR_POINT_ANCHOR_BORDER : COLOR_POINT_CONTROL_BORDER);

            // 【【【 核心修正 II：统一‘道袍’ 】】】
            // 自行绘制现代风格圆盘，取代旧的Cap
            float handleSize = HandleUtility.GetHandleSize(handleDrawPos);
            Handles.color = fillColor;
            Handles.DrawSolidDisc(handleDrawPos, camera.transform.forward, handleSize * size);
            Handles.color = borderColor;
            Handles.DrawWireDisc(handleDrawPos, camera.transform.forward, handleSize * size, 2f);

            // 使用一个不可见的Handle来捕捉交互
            Handles.color = Color.clear;
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = Handles.FreeMoveHandle(handleDrawPos, Quaternion.identity, handleSize * size * 1.2f, Vector3.zero, Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(creator, "Move Path Point");
                if (creator.profile.snapToTerrain && creator.profile.snapStrength > 0)
                {
                    float terrainHeight = context.heightProvider.GetHeight(newWorldPos);
                    Vector3 snappedPos = new Vector3(newWorldPos.x, terrainHeight, newWorldPos.z);
                    newWorldPos = Vector3.Lerp(newWorldPos, snappedPos, creator.profile.snapStrength);
                }
                creator.MovePoint(i, newWorldPos);
            }
        }
    }
    /// <summary>
    /// 【新增神通】为贝塞尔曲线绘制‘星轨’连接线
    /// </summary>
    private static void DrawBezierControlLines(ref HandleDrawContext context)
    {
        if (context.creator.Path is not BezierPath bezierPath) return;

        Handles.color = COLOR_BEZIER_LINE;
        for (int i = 0; i < bezierPath.NumSegments; i++)
        {
            // --- 【【【 核心清晰度优化：明晰天道 】】】 ---
            // 同样，使用更清晰的专用接口。
            Vector3 anchor1 = context.creator.GetPoint(i);
            Vector3 control1 = bezierPath.GetControlPoint(i, 1, context.creator.transform);
            Handles.DrawDottedLine(anchor1, control1, 4f);

            Vector3 anchor2 = context.creator.GetPoint(i + 1);
            Vector3 control2 = bezierPath.GetControlPoint(i + 1, 2, context.creator.transform);
            Handles.DrawDottedLine(anchor2, control2, 4f);
        }
    }
    // 新增辅助方法，统一获取Handle的绘制位置
    private static Vector3 GetHandleDrawPosition(ref HandleDrawContext context, int pointIndex)
    {
        // 使用新的GetPoint方法，确保能正确处理所有路径类型
        Vector3 worldPos = context.creator.GetPoint(pointIndex);
        if (context.creator.profile.snapToTerrain)
        {
            worldPos.y = context.heightProvider.GetHeight(worldPos);
        }
        return worldPos;
    }
    #endregion
}