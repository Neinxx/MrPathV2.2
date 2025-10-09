// CatmullRomPath.cs
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{
    [System.Serializable]
    public class CatmullRomPath : IPath
    {
        [SerializeField]
        private List<Vector3> points = new();

        // 显式地将 List<Vector3> 从公共API中隐藏，强制使用接口方法
        private List<Vector3> Points { get => points; set => points = value; }

        public int NumPoints => Points.Count;
        public int NumSegments => Points.Count < 2 ? 0 : Points.Count - 1;

        public void AddSegment(Vector3 newPointWorldPos, Transform owner)
            => Points.Add(owner.InverseTransformPoint(newPointWorldPos));

        public void MovePoint(int i, Vector3 newPointWorldPos, Transform owner)
            => Points[i] = owner.InverseTransformPoint(newPointWorldPos);

        public void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, Transform owner)
            => Points.Insert(segmentIndex + 1, owner.InverseTransformPoint(newPointWorldPos));

        public void DeleteSegment(int pointIndex)
        {
            if (pointIndex >= 0 && pointIndex < Points.Count)
            {
                Points.RemoveAt(pointIndex);
            }
        }

        public void ClearSegments() => Points.Clear();

        public Vector3 GetPoint(int index, Transform owner)
        {
            if (index >= 0 && index < Points.Count)
            {
                return owner.TransformPoint(Points[index]);
            }
            return owner.position; // 返回一个安全的默认值
        }

        public List<Vector3> GetPointsForMigration() => new List<Vector3>(points);
        public void SetPointsFromMigration(List<Vector3> localPoints) => points = localPoints;

        public Vector3 GetPointAt(float t, Transform owner)
        {
            if (NumPoints == 0) return owner.position;
            if (NumPoints == 1) return owner.TransformPoint(Points[0]);

            int p1_idx = Mathf.Clamp(Mathf.FloorToInt(t), 0, NumSegments - 1);
            float localT = t - p1_idx;

            int p0_idx = p1_idx - 1;
            int p2_idx = p1_idx + 1;
            int p3_idx = p2_idx + 1;

            Vector3 p0 = Points[Mathf.Clamp(p0_idx, 0, NumPoints - 1)];
            Vector3 p1 = Points[p1_idx];
            Vector3 p2 = Points[Mathf.Clamp(p2_idx, 0, NumPoints - 1)];
            Vector3 p3 = Points[Mathf.Clamp(p3_idx, 0, NumPoints - 1)];

            float t2 = localT * localT;
            float t3 = t2 * localT;

            Vector3 point = 0.5f * (
                (2.0f * p1) +
                (-p0 + p2) * localT +
                (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
                (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
            );

            return owner.TransformPoint(point);
        }
    }
}