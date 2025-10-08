// PathStrategy.cs
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 【最终圆满版 • 法则基石】
/// </summary>
public abstract class PathStrategy : ScriptableObject
{
    [Header("法则外观定义")]
    public PathDrawingStyle drawingStyle;


    #region 数学法则契约 (Math Law Contract)
    public abstract Vector3 GetPointAt(float t, PathData data);
    public abstract void AddSegment(Vector3 newPointWorldPos, PathData data, Transform owner);
    public abstract void MovePoint(int flatIndex, Vector3 newPointWorldPos, PathData data, Transform owner);
    public abstract void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, PathData data, Transform owner);
    public abstract void DeleteSegment(int flatIndex, PathData data);
    public virtual void ClearSegments(PathData data)
    {
        if (data.KnotCount > 2)
        {
            Vector3 firstPointPosition = data.GetPosition(0);
            data.Clear();
            data.AddKnot(firstPointPosition, Vector3.zero, Vector3.zero);
            data.AddKnot(firstPointPosition + Vector3.forward * 5f, Vector3.zero, Vector3.zero);
        }
    }
    #endregion

#if UNITY_EDITOR
    #region 绘制与交互契约 (Drawing & Interaction Contract)
    public abstract void DrawHandles(ref PathEditorHandles.HandleDrawContext context);
    public abstract void UpdatePointHover(ref PathEditorHandles.HandleDrawContext context);
    #endregion
#endif
}