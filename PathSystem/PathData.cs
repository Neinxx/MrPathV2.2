// PathData.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MrPath
{

    /// <summary>
    /// 【大师重构版】通用路径数据容器。
    /// 采用SoA（Structure of Arrays）布局，为极致性能和可扩展性而生。
    /// 它本身不关心曲线类型，只负责忠实地存储数据。
    /// </summary>
    [System.Serializable]
    public class PathData
    {
        public readonly struct Knot
        {
            public readonly Vector3 Position;
            public readonly Vector3 TangentIn;
            public readonly Vector3 TangentOut;

            public Knot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
            {
                Position = position;
                TangentIn = tangentIn;
                TangentOut = tangentOut;
            }

            public Vector3 GlobalTangentIn => Position + TangentIn;
            public Vector3 GlobalTangentOut => Position + TangentOut;
        }

        [SerializeField] private List<Vector3> positions = new();
        [SerializeField] private List<Vector3> tangentsIn = new();
        [SerializeField] private List<Vector3> tangentsOut = new();
        // 可以在此轻松扩展，例如： [SerializeField] private List<Quaternion> orientations = new();

        public int KnotCount => positions.Count;
        public int SegmentCount => Mathf.Max(0, positions.Count - 1);

        public Knot GetKnot(int index) => new Knot(positions[index], tangentsIn[index], tangentsOut[index]);
        public Vector3 GetPosition(int index) => positions[index];
        public Vector3 GetTangentIn(int index) => tangentsIn[index];
        public Vector3 GetTangentOut(int index) => tangentsOut[index];

        public void AddKnot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
        {
            positions.Add(position);
            tangentsIn.Add(tangentIn);
            tangentsOut.Add(tangentOut);
        }

        public void InsertKnot(int index, Vector3 position, Vector3 tangentIn, Vector3 tangentOut)
        {
            positions.Insert(index, position);
            tangentsIn.Insert(index, tangentIn);
            tangentsOut.Insert(index, tangentOut);
        }

        public void DeleteKnot(int index)
        {
            positions.RemoveAt(index);
            tangentsIn.RemoveAt(index);
            tangentsOut.RemoveAt(index);
        }

        public void MovePosition(int index, Vector3 newPosition) => positions[index] = newPosition;
        public void MoveTangentIn(int index, Vector3 newTangentIn) => tangentsIn[index] = newTangentIn;
        public void MoveTangentOut(int index, Vector3 newTangentOut) => tangentsOut[index] = newTangentOut;

        public void Clear()
        {
            positions.Clear();
            tangentsIn.Clear();
            tangentsOut.Clear();
        }
    }
}