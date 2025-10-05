using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【最终纯净版】定义了所有路径算法必须遵守的核心数学接口。
/// - 已完全移除所有与编辑器绘制和交互相关的职责。
/// </summary>
public interface IPath
{
    #region 属性 (Properties)

    /// <summary>
    ///获取或设置构成路径的所有数据点（ 存储在局部空间）。
    /// </summary>
    List<Vector3> Points { get; set; }

    /// <summary>
    ///获取路径的总段数。
    /// </summary>
    int NumSegments { get; }

    /// <summary>
    ///获取路径中数据点的总数。
    /// </summary>
    int NumPoints { get; }

    #endregion

    #region 核心方法 (Core Methods)

    /// <summary>
    /// 根据一个0到NumSegments之间的t值，获取曲线上精确的世界坐标点。
    /// </summary>
    /// <param name="t">沿曲线的距离参数，从0到NumSegments。</param>
    /// <param name="owner">拥有此路径的Transform组件。</param>
    /// <returns>世界空间中的点坐标。</returns>
    Vector3 GetPointAt (float t, Transform owner);

    /// <summary>
    /// 在路径末尾添加一个新的段落/点。
    /// </summary>
    /// <param name="newPointWorldPos">新点的世界坐标。</param>
    /// <param name="owner">拥有此路径的Transform组件。</param>
    void AddSegment (Vector3 newPointWorldPos, Transform owner);

    /// <summary>
    /// 移动路径上的一个指定点。
    /// </summary>
    /// <param name="i">要移动的点的索引。</param>
    /// <param name="newPointWorldPos">点的新世界坐标。</param>
    /// <param name="owner">拥有此路径的Transform组件。</param>
    void MovePoint (int i, Vector3 newPointWorldPos, Transform owner);

    /// <summary>
    /// 在指定的分段索引后插入一个新的点。
    /// </summary>
    /// <param name="segmentIndex">在其后插入新点的分段索引。</param>
    /// <param name="newPointWorldPos">新点的世界坐标。</param>
    /// <param name="owner">拥有此路径的Transform组件。</param>
    void InsertSegment (int segmentIndex, Vector3 newPointWorldPos, Transform owner);

    /// <summary>
    /// 删除路径上的一个指定点。
    /// </summary>
    /// <param name="pointIndex">要删除的点的索引。</param>
    void DeleteSegment (int pointIndex);

    #endregion
}
