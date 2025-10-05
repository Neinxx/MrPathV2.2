using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【纯净版】实现了IPath接口，提供三阶贝塞尔曲线的数学模型。
/// - 已完全移除所有与UnityEditor相关的绘制逻辑，成为一个纯粹的数据与算法类。
/// </summary>
[System.Serializable]
public class BezierPath : IPath
{
    #region 字段与属性 (Fields & Properties)

    [SerializeField]
    private List<Vector3> points = new List<Vector3> ();

    /// <summary>
    /// 路径的所有数据点（锚点和控制点），存储在局部空间中。
    /// </summary>
    public List<Vector3> Points { get => points; set => points = value; }

    /// <summary>
    /// 路径中数据点的总数。
    /// </summary>
    public int NumPoints => Points.Count;

    /// <summary>
    /// 曲线的总段数。对于贝塞尔曲线，每3个点构成一段。
    /// </summary>
    public int NumSegments => (Points.Count - 1) / 3;

    #endregion

    #region 公共接口 (IPath Implementation)

    /// <summary>
    /// 在路径末尾添加一个新的曲线段（一个锚点和两个控制点）。
    /// </summary>
    public void AddSegment (Vector3 newAnchorWorldPos, Transform owner)
    {
        Vector3 newAnchorLocalPos = owner.InverseTransformPoint (newAnchorWorldPos);
        if (Points.Count == 0)
        {
            Points.Add (newAnchorLocalPos);
            return;
        }

        Vector3 lastAnchorPos = Points[Points.Count - 1];
        Vector3 control1 = lastAnchorPos + (newAnchorLocalPos - lastAnchorPos) * 0.25f;
        Vector3 control2 = newAnchorLocalPos - (newAnchorLocalPos - lastAnchorPos) * 0.25f;

        Points.Add (control1);
        Points.Add (control2);
        Points.Add (newAnchorLocalPos);
    }

    /// <summary>
    /// 移动路径上的一个指定点。
    /// </summary>
    public void MovePoint (int i, Vector3 newWorldPos, Transform owner)
    {
        Vector3 newLocalPos = owner.InverseTransformPoint (newWorldPos);
        Vector3 deltaMove = newLocalPos - Points[i];

        Points[i] = newLocalPos;

        if (i % 3 == 0) // 如果移动的是锚点，则其关联的控制点也应跟随移动
        {
            if (i + 1 < Points.Count) { Points[i + 1] += deltaMove; }
            if (i - 1 >= 0) { Points[i - 1] += deltaMove; }
        }
    }

    /// <summary>
    /// 在指定曲线段内插入一个新的锚点。 (简化实现)
    /// </summary>
    public void InsertSegment (int segmentIndex, Vector3 newPointWorldPos, Transform owner)
    {
        // 简化处理：在路径末尾添加新段落
        AddSegment (newPointWorldPos, owner);
    }

    /// <summary>
    /// 删除路径上的一个点（如果是锚点，则会删除相关分段）。
    /// </summary>
    public void DeleteSegment (int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= Points.Count || pointIndex % 3 != 0) return;

        if (NumSegments > 0)
        {
            if (pointIndex == 0) Points.RemoveRange (0, 3);
            else if (pointIndex == Points.Count - 1) Points.RemoveRange (pointIndex - 2, 3);
            else Points.RemoveRange (pointIndex - 1, 3);
        }
        else if (NumPoints > 0)
        {
            Points.RemoveAt (pointIndex);
        }
    }

    /// <summary>
    /// 根据一个0到NumSegments之间的t值，获取曲线上精确的世界坐标点。
    /// </summary>
    public Vector3 GetPointAt (float t, Transform owner)
    {
        if (NumPoints == 0) return owner.position;
        if (NumSegments == 0) return owner.TransformPoint (Points[0]);

        int segmentIndex = Mathf.Clamp (Mathf.FloorToInt (t), 0, NumSegments - 1);
        float localT = t - segmentIndex;

        Vector3[] segment = GetPointsInSegment (segmentIndex);

        float u = 1 - localT;
        float tt = localT * localT;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * localT;

        Vector3 p = uuu * segment[0] + 3 * uu * localT * segment[1] + 3 * u * tt * segment[2] + ttt * segment[3];

        return owner.TransformPoint (p);
    }

    #endregion

    #region 内部辅助方法 (Internal Helpers)

    /// <summary>
    /// 获取指定索引的分段所包含的4个点。
    /// </summary>
    public Vector3[] GetPointsInSegment (int segmentIndex)
    {
        int startIndex = segmentIndex * 3;
        return new Vector3[]
        {
            Points[startIndex],
                Points[startIndex + 1],
                Points[startIndex + 2],
                Points[startIndex + 3]
        };
    }

    #endregion
}
