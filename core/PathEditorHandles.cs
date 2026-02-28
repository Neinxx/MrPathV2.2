// PathEditorHandles.cs
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【最终定稿 • 天之纲领】
/// 
/// 路径编辑器句柄的“舞台监督”与“共享法器库”。
/// 它的形态已臻于化境，职责简化到了极致：
/// 
/// 1. 调度 (Dispatch): 作为编辑器工具的总入口，调用当前激活的“法则”进行自我绘制和交互。
/// 2. 共享 (Share): 提供高度可复用的绘制“神通”(如DrawHandle)，供所有法则统一调用，确保风格一致。
/// 3. 通用 (Universal): 处理不属于任何特定法则的通用逻辑（如插入点预览）。
/// 
/// 它本身不再包含任何关于颜色、尺寸、或特定曲线的绘制逻辑，达到了前所未有的纯净与优雅。
/// </summary>
public static class PathEditorHandles
{
    #region 公共结构 (Public Structs)

    /// <summary>
    /// Handle绘制的上下文信息包，在各个绘制方法之间传递状态。
    /// </summary>
    public struct HandleDrawContext
    {
        public PathCreator creator;
        public TerrainHeightProvider heightProvider;
        public PathSpine? latestSpine;
        public bool isDragging;
        public int hoveredPointIndex; // 扁平化索引
        public int hoveredSegmentIndex;
        public float hoveredPathT;
    }

    #endregion

    #region 核心调度 (Core Dispatch)

    /// <summary>
    /// 绘制调度的总入口。
    /// </summary>
    public static void Draw(ref HandleDrawContext context)
    {
        var creator = context.creator;
        if (creator == null || creator.profile == null || creator.pathData.KnotCount == 0) return;

        var camera = SceneView.currentDrawingSceneView.camera;
        if (camera == null) return;

        var strategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
        if (strategy == null) return;

        // 步骤 1: 委托法则进行自我感知（悬停检测）
        UpdateHoverState(ref context, strategy);

        // 步骤 2: 委托法则进行自我描绘（绘制曲线和Handle）
        strategy.DrawHandles(ref context);

        // 步骤 3: 绘制通用的插入预览
        DrawInsertionPreviewHandle(ref context, camera, strategy.drawingStyle);
    }

    #endregion

    #region 通用逻辑 (Universal Logic)

    private static void UpdateHoverState(ref HandleDrawContext context, PathStrategy strategy)
    {
        if (context.isDragging) return;

        // 重置悬停状态
        context.hoveredPointIndex = -1;
        context.hoveredSegmentIndex = -1;
        context.hoveredPathT = -1;

        // 将“哪个点被悬停”的复杂判断，完全交给法则自己去处理
        strategy.UpdatePointHover(ref context);

        if (context.hoveredPointIndex == -1)
        {
            // 如果没有点被悬停，才进行“线”的悬停检测
            UpdatePathHover(ref context);
        }
    }

    // --- 【【【 心法重铸之处 】】】 ---
    private static void UpdatePathHover(ref HandleDrawContext context)
    {
        var creator = context.creator;
        Event e = Event.current;
        float minSqrDist = float.MaxValue;
        float closestT = -1;

        // 定义一个合理的屏幕空间拾取距离阈值
        const float pickDistanceThreshold = 10f;

        for (int i = 0; i < creator.NumSegments; i++)
        {
            // 使用更精细的步长来检测曲线
            const int stepsPerSegment = 20;
            Vector3 prevPoint = creator.GetPointAt(i);

            for (int j = 1; j <= stepsPerSegment; j++)
            {
                float t = i + (float)j / stepsPerSegment;
                Vector3 currentPoint = creator.GetPointAt(t);

                // 将3D线段投影到2D屏幕空间，然后计算鼠标到线段的距离
                float distToSegment = HandleUtility.DistancePointToLineSegment(
                    e.mousePosition,
                    HandleUtility.WorldToGUIPoint(prevPoint),
                    HandleUtility.WorldToGUIPoint(currentPoint)
                );

                if (distToSegment < minSqrDist)
                {
                    minSqrDist = distToSegment;

                    // 当找到更近的线段时，精确计算出这一点在线段上的t值
                    Vector3 closestPoint3D = HandleUtility.ClosestPointToPolyLine(prevPoint, currentPoint);
                    float segmentLength = Vector3.Distance(prevPoint, currentPoint);
                    if (segmentLength > 0.001f)
                    {
                        float localT = Vector3.Distance(prevPoint, closestPoint3D) / segmentLength;
                        closestT = i + (localT * ((float)j / stepsPerSegment - (float)(j - 1) / stepsPerSegment)) + (float)(j - 1) / stepsPerSegment;
                    }
                    else
                    {
                        closestT = i;
                    }
                }
                prevPoint = currentPoint;
            }
        }

        // 只有当最近距离小于阈值时，才认为悬停成功
        if (minSqrDist < pickDistanceThreshold)
        {
            context.hoveredPathT = closestT;
            context.hoveredSegmentIndex = Mathf.FloorToInt(closestT);
        }
    }


    private static void DrawInsertionPreviewHandle(ref HandleDrawContext context, Camera camera, PathDrawingStyle style)
    {
        Event e = Event.current;
        if (e.shift && !e.control && context.hoveredPathT > -1)
        {
            Vector3 previewPos = context.creator.GetPointAt(context.hoveredPathT);
            float handleSize = HandleUtility.GetHandleSize(previewPos);
            var previewStyle = style.insertionPreviewStyle;

            Handles.color = previewStyle.fillColor;
            Handles.DrawSolidDisc(previewPos, camera.transform.forward, handleSize * previewStyle.size);
            Handles.color = previewStyle.borderColor;
            Handles.DrawWireDisc(previewPos, camera.transform.forward, handleSize * previewStyle.size, 1.5f);

            // 重绘场景，确保预览的实时性
            HandleUtility.Repaint();
        }
    }

    #endregion

    #region 共享神通 (Shared Techniques)

    /// <summary>
    /// 【共享神通】一个纯粹的绘制执行者。
    /// 它不再关心颜色和尺寸来自哪里，只负责根据传入的Style来执行绘制和交互。
    /// </summary>
    public static void DrawHandle(Vector3 localPos, int flatIndex, HandleStyle style, ref HandleDrawContext context, Camera camera)
    {
        var creator = context.creator;
        Vector3 worldPos = creator.transform.TransformPoint(localPos);

        bool isHovered = (flatIndex == context.hoveredPointIndex);

        var currentStrategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
        if (currentStrategy == null || currentStrategy.drawingStyle == null)
        {
            Handles.color = Color.red;
            Handles.SphereHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.1f, EventType.Repaint);
            return;
        }
        var hoverStyle = currentStrategy.drawingStyle.hoverStyle;

        var finalStyle = isHovered ? hoverStyle : style;
        float size = isHovered ? finalStyle.size * 1.2f : finalStyle.size;

        float handleSize = HandleUtility.GetHandleSize(worldPos);
        Handles.color = finalStyle.fillColor;
        Handles.DrawSolidDisc(worldPos, camera.transform.forward, handleSize * size);
        Handles.color = finalStyle.borderColor;
        Handles.DrawWireDisc(worldPos, camera.transform.forward, handleSize * size, 2f);

        Handles.color = Color.clear;
        EditorGUI.BeginChangeCheck();
        Vector3 newWorldPos = Handles.FreeMoveHandle(worldPos, Quaternion.identity, handleSize * size * 1.2f, Vector3.zero, Handles.RectangleHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(creator, "Move Path Point");
            creator.ExecuteCommand(new MovePointCommand(flatIndex, newWorldPos));
        }
    }
    #endregion
}