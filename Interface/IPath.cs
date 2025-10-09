// IPath.cs
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【大师重构版】定义了所有路径算法必须遵守的核心数学接口。
    /// - 完全解耦了内部数据结构（如List<Vector3>）与外部查询。
    /// - 专注于“能做什么”，而非“有什么”。
    /// </summary>
    public interface IPath
    {
        #region 属性 (Properties)

        /// <summary>
        /// 获取路径的总段数。对于N个锚点，通常有 N-1 段。
        /// </summary>
        int NumSegments { get; }

        /// <summary>
        /// 获取路径中“主要”数据点（或称锚点）的总数。
        /// </summary>
        int NumPoints { get; }

        #endregion

        #region 核心方法 (Core Methods)

        /// <summary>
        /// 根据一个0到NumSegments之间的t值，获取曲线上精确的世界坐标点。
        /// </summary>
        Vector3 GetPointAt(float t, Transform owner);

        /// <summary>
        /// 获取指定索引的“主要”点（锚点）的世界坐标。
        /// </summary>
        Vector3 GetPoint(int index, Transform owner);

        /// <summary>
        /// （可选）获取构成路径的所有点的局部坐标列表，主要用于数据迁移。
        /// </summary>
        List<Vector3> GetPointsForMigration();

        /// <summary>
        /// （可选）使用一组局部坐标点来重建路径，主要用于数据迁移。
        /// </summary>
        void SetPointsFromMigration(List<Vector3> localPoints);

        /// <summary>
        /// 在路径末尾添加一个新的段落/点。
        /// </summary>
        void AddSegment(Vector3 newPointWorldPos, Transform owner);

        /// <summary>
        /// 移动路径上的一个指定点（可以是锚点或控制点）。
        /// </summary>
        void MovePoint(int i, Vector3 newPointWorldPos, Transform owner);

        /// <summary>
        /// 在指定的分段索引后插入一个新的点。
        /// </summary>
        void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, Transform owner);

        /// <summary>
        /// 删除路径上的一个指定点（通常是锚点）。
        /// </summary>
        void DeleteSegment(int pointIndex);

        /// <summary>
        /// 清除路径中的所有点。
        /// </summary>
        void ClearSegments();

        #endregion
    }
}