using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【纯净版】实现了IPath接口，提供Catmull-Rom样条曲线的数学模型。
/// - 已完全移除所有与UnityEditor相关的绘制逻辑。
/// </summary>
[System.Serializable]
public class CatmullRomPath : IPath
{
    #region 字段与属性 (Fields & Properties)

    [SerializeField]
    private List<Vector3> points = new List<Vector3> ();

    /// <summary>
    /// 路径的所有数据点，存储在局部空间中。
    /// </summary>
    public List<Vector3> Points { get => points; set => points = value; }

    /// <summary>
    /// 路径中数据点的总数。
    /// </summary>
    public int NumPoints => Points.Count;

    /// <summary>
    /// 曲线的总段数。
    /// </summary>
    public int NumSegments => Points.Count < 2 ? 0 : Points.Count - 1;

    #endregion

    #region 公共接口 (IPath Implementation)

    /// <summary>
    /// 在路径末尾添加一个新的点。
    /// </summary>
    public void AddSegment (Vector3 newPointWorldPos, Transform owner)
    {
        Points.Add (owner.InverseTransformPoint (newPointWorldPos));
    }

    /// <summary>
    /// 移动路径上的一个指定点。
    /// </summary>
    public void MovePoint (int i, Vector3 newPointWorldPos, Transform owner)
    {
        Points[i] = owner.InverseTransformPoint (newPointWorldPos);
    }

    /// <summary>
    /// 在指定曲线段之后插入一个新的点。
    /// </summary>
    public void InsertSegment (int segmentIndex, Vector3 newPointWorldPos, Transform owner)
    {
        Points.Insert (segmentIndex + 1, owner.InverseTransformPoint (newPointWorldPos));
    }

    /// <summary>
    /// 删除路径上的一个点。
    /// </summary>
    public void DeleteSegment (int pointIndex)
    {
        if (pointIndex >= 0 && pointIndex < Points.Count)
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
        if (NumPoints == 1) return owner.TransformPoint (Points[0]);

        int p1_idx = Mathf.Clamp (Mathf.FloorToInt (t), 0, NumSegments - 1);
        float localT = t - p1_idx;

        int p0_idx = p1_idx - 1;
        int p2_idx = p1_idx + 1;
        int p3_idx = p2_idx + 1;

        Vector3 p0 = Points[Mathf.Clamp (p0_idx, 0, NumPoints - 1)];
        Vector3 p1 = Points[p1_idx];
        Vector3 p2 = Points[Mathf.Clamp (p2_idx, 0, NumPoints - 1)];
        Vector3 p3 = Points[Mathf.Clamp (p3_idx, 0, NumPoints - 1)];

        float t2 = localT * localT;
        float t3 = t2 * localT;

        Vector3 point = 0.5f * (
            (2.0f * p1) +
            (-p0 + p2) * localT +
            (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
            (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
        );

        return owner.TransformPoint (point);
    }

    #endregion
}
