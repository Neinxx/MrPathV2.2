using UnityEditor;
using UnityEngine;

/// <summary>
/// 【最终定稿版】一个通用的静态类，负责绘制任何IPath曲线及其在场景视图中的交互Handles。
/// 它集绘制、悬停检测、点移动处理于一体。
/// </summary>
public static class PathEditorHandles
{
    #region 公共主方法 (Public Main Method)

    /// <summary>
    /// 在场景视图中完整地绘制路径、手柄，并处理其交互。
    /// </summary>
    /// <param name="creator">路径的持有者。</param>
    /// <param name="hoveredPointIndex">被悬停的点的索引（通过ref传递，可被此方法修改）。</param>
    /// <param name="hoveredSegmentIndex">被悬停的段的索引（通过ref传递，可被此方法修改）。</param>
    /// <param name="isDragging">当前是否正在拖拽手柄。</param>
    public static void Draw (PathCreator creator, ref int hoveredPointIndex, ref int hoveredSegmentIndex, bool isDragging)
    {
        // Debug.Log ("PathEditorHandles.Draw called.");
        if (creator == null || creator.Path == null) return;
        // 在处理绘制和交互前，先更新悬停状态
        UpdateHoverState (creator, ref hoveredPointIndex, ref hoveredSegmentIndex, isDragging);

        // 根据曲线类型，选择不同的方法绘制曲线本身
        if (creator.Path is BezierPath bezierPath)
        {
            DrawBezierCurve (bezierPath, creator.transform, hoveredSegmentIndex);
        }
        else // 对于CatmullRom或任何其他类型的曲线，使用通用的采样绘制法
        {
            DrawSampledCurve (creator.Path, creator.transform, hoveredSegmentIndex);
        }

        // 绘制所有数据点的手柄
        DrawPointHandles (creator, hoveredPointIndex);
    }

    #endregion

    #region 内部绘制与交互逻辑 (Internal Drawing & Interaction)

    /// <summary>
    /// 核心职责1：更新悬停状态。
    /// </summary>
    private static void UpdateHoverState (PathCreator creator, ref int hoveredPointIndex, ref int hoveredSegmentIndex, bool isDragging)
    {
        if (isDragging) return; // 拖拽时锁定悬停状态，防止抖动

        hoveredPointIndex = -1;
        for (int i = 0; i < creator.NumPoints; i++)
        {
            Vector3 worldPos = creator.transform.TransformPoint (creator.Path.Points[i]);
            float handleRadius = HandleUtility.GetHandleSize (worldPos) * 0.15f;
            if (HandleUtility.DistanceToCircle (worldPos, handleRadius) == 0)
            {
                hoveredPointIndex = i;
                break;
            }
        }

        hoveredSegmentIndex = -1;
        if (hoveredPointIndex == -1)
        {
            float minSegmentDist = 10f;
            for (int i = 0; i < creator.NumSegments; i++)
            {
                Vector3 prevPoint = creator.GetPointAt (i);
                for (int j = 1; j <= 10; j++)
                {
                    Vector3 currentPoint = creator.GetPointAt (i + j / 10f);
                    if (HandleUtility.DistanceToLine (prevPoint, currentPoint) < minSegmentDist)
                    {
                        minSegmentDist = HandleUtility.DistanceToLine (prevPoint, currentPoint);
                        hoveredSegmentIndex = i;
                    }
                    prevPoint = currentPoint;
                }
            }
        }
    }

    /// <summary>
    /// 核心职责2：绘制所有可交互的控制点手柄并处理移动。
    /// </summary>
    private static void DrawPointHandles (PathCreator creator, int hoveredPointIndex)
    {
        for (int i = 0; i < creator.NumPoints; i++)
        {
            Vector3 worldPos = creator.transform.TransformPoint (creator.Path.Points[i]);
            bool isAnchor = (creator.Path is BezierPath && i % 3 == 0) || creator.Path is not BezierPath;

            Handles.color = (i == hoveredPointIndex) ? Color.yellow : (isAnchor ? Color.green : Color.white);
            float handleSize = isAnchor ? 0.15f : 0.08f;

            EditorGUI.BeginChangeCheck ();
            Vector3 newWorldPos = Handles.FreeMoveHandle (
                worldPos, Quaternion.identity, HandleUtility.GetHandleSize (worldPos) * handleSize,
                Vector3.zero, Handles.SphereHandleCap
            );
            if (EditorGUI.EndChangeCheck ())
            {
                Undo.RecordObject (creator, "Move Path Point");

                // --- 开始施展“地形吸附” ---
                if (creator.snapToTerrain && creator.snapStrength > 0)
                {
                    // 尝试找到场景中所有激活的地形
                    Terrain[] terrains = Terrain.activeTerrains;
                    if (terrains.Length > 0)
                    {
                        // 简单起见，我们只使用第一个找到的地形
                        Terrain activeTerrain = terrains[0];
                        float terrainHeight = activeTerrain.SampleHeight (newWorldPos) + activeTerrain.GetPosition ().y;

                        // 计算完全吸附时的目标位置
                        Vector3 snappedPos = new Vector3 (newWorldPos.x, terrainHeight, newWorldPos.z);

                        // 根据吸附强度，在原始拖拽位置和完全吸附位置之间进行插值
                        newWorldPos = Vector3.Lerp (newWorldPos, snappedPos, creator.snapStrength);
                    }
                }
                // --- “地形吸附”施展完毕 ---

                if (EditorGUI.EndChangeCheck ())
                {
                    Undo.RecordObject (creator, "Move Path Point");
                    creator.MovePoint (i, newWorldPos);
                }
            }
        }
    }

    /// <summary>
    /// 核心职责3a：为贝塞尔曲线特化的绘制方法。
    /// </summary>
    private static void DrawBezierCurve (BezierPath path, Transform owner, int hoveredSegmentIndex)
    {
        for (int i = 0; i < path.NumSegments; i++)
        {
            Vector3[] segmentPoints = path.GetPointsInSegment (i);
            Vector3 p0 = owner.TransformPoint (segmentPoints[0]);
            Vector3 p1 = owner.TransformPoint (segmentPoints[1]);
            Vector3 p2 = owner.TransformPoint (segmentPoints[2]);
            Vector3 p3 = owner.TransformPoint (segmentPoints[3]);

            Color curveColor = (i == hoveredSegmentIndex) ? Color.cyan : Color.white;
            Handles.DrawBezier (p0, p3, p1, p2, curveColor, null, 2f);
        }
    }

    /// <summary>
    /// 核心职责3b：适用于所有曲线的通用采样绘制方法。
    /// </summary>
    private static void DrawSampledCurve (IPath path, Transform owner, int hoveredSegmentIndex)
    {
        const int stepsPerSegment = 20;
        for (int i = 0; i < path.NumSegments; i++)
        {
            Color curveColor = (i == hoveredSegmentIndex) ? Color.cyan : Color.white;
            Handles.color = curveColor;

            Vector3 prevPoint = path.GetPointAt (i, owner);
            for (int j = 1; j <= stepsPerSegment; j++)
            {
                float t = i + (float) j / stepsPerSegment;
                Vector3 currentPoint = path.GetPointAt (t, owner);
                Handles.DrawLine (prevPoint, currentPoint);
                prevPoint = currentPoint;
            }
        }
    }

    #endregion
}
