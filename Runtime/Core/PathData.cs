// PathData.cs
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【第三步：万法归一的容器】
    /// 通用路径数据容器。这是整个框架的数据核心。
    /// 
    /// - 采用SoA（Structure of Arrays）布局，为极致性能和未来的可扩展性（如Jobs/Burst）而生。
    /// - 它本身不关心曲线类型，只负责忠实地存储数据。
    /// - 通过封装好的方法（如AddKnot）来保证数据的一致性，防止列表不同步。
    /// </summary>
    [System.Serializable]
    public class PathData
    {
        /// <summary>
        /// Knot (节点) - 一个只读的结构体，作为访问SoA数据的便捷“视图”。
        /// 它不用于存储，只用于从外部安全、方便地查询一个节点的完整信息。
        /// </summary>
        public readonly struct Knot
        {
            public readonly Vector3 Position;
            public readonly Vector3 TangentIn;  // 对应旧系统的 controlPoint2 (相对坐标)
            public readonly Vector3 TangentOut; // 对应旧系统的 controlPoint1 (相对坐标)

            public Knot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
            {
                Position = position;
                TangentIn = tangentIn;
                TangentOut = tangentOut;
            }

            // 便捷属性，用于获取世界空间（相对于节点位置）的控制点坐标
            public Vector3 GlobalTangentIn => Position + TangentIn;
            public Vector3 GlobalTangentOut => Position + TangentOut;
        }

        // --- 核心数据存储：SoA 布局 ---
        [SerializeField] private List<Vector3> positions = new();
        [SerializeField] private List<Vector3> tangentsIn = new();
        [SerializeField] private List<Vector3> tangentsOut = new();
        // 未来可在此轻松扩展，例如： [SerializeField] private List<Quaternion> orientations = new();

        #region 公共查询 API (Public Query API)

        /// <summary>
        /// 获取路径中的节点（锚点）总数。
        /// </summary>
        public int KnotCount => positions.Count;

        /// <summary>
        /// 获取路径中的总段数。
        /// </summary>
        public int SegmentCount => Mathf.Max(0, positions.Count - 1);

        /// <summary>
        /// 获取指定索引节点的便捷“视图”。
        /// </summary>
        public Knot GetKnot(int index) => new(positions[index], tangentsIn[index], tangentsOut[index]);

        /// <summary>
        /// 获取指定索引节点的位置。
        /// </summary>
        public Vector3 GetPosition(int index) => positions[index];

        #endregion

        #region 公共操作 API (Public Manipulation API)

        /// <summary>
        /// 在路径末尾添加一个完整的节点。
        /// </summary>
        public void AddKnot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
        {
            positions.Add(position);
            tangentsIn.Add(tangentIn);
            tangentsOut.Add(tangentOut);
        }

        /// <summary>
        /// 在指定索引处插入一个完整的节点。
        /// </summary>
        public void InsertKnot(int index, Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
        {
            positions.Insert(index, position);
            tangentsIn.Insert(index, tangentIn);
            tangentsOut.Insert(index, tangentOut);
        }

        /// <summary>
        /// 删除指定索引处的节点。
        /// </summary>
        public void DeleteKnot(int index)
        {
            positions.RemoveAt(index);
            tangentsIn.RemoveAt(index);
            tangentsOut.RemoveAt(index);
        }

        /// <summary>
        /// 移动指定节点的位置。
        /// </summary>
        public void MovePosition(int index, Vector3 newPosition) => positions[index] = newPosition;

        /// <summary>
        /// 移动指定节点的入切线（相对坐标）。
        /// </summary>
        public void MoveTangentIn(int index, Vector3 newTangentIn) => tangentsIn[index] = newTangentIn;

        /// <summary>
        /// 移动指定节点的出切线（相对坐标）。
        /// </summary>
        public void MoveTangentOut(int index, Vector3 newTangentOut) => tangentsOut[index] = newTangentOut;

        /// <summary>
        /// 清空所有路径数据。
        /// </summary>
        public void Clear()
        {
            positions.Clear();
            tangentsIn.Clear();
            tangentsOut.Clear();
        }

        public void ShiftAllPositions(Vector3 delta)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                positions[i] += delta;
            }
        }
        #endregion
    }
}