// BezierPath.cs
using System.Collections.Generic;
using System.Linq; // For migration methods
using UnityEngine;

[System.Serializable]
public class BezierPath : IPath
{
    [System.Serializable]
    public struct AnchorPoint
    {
        public Vector3 position;
        public Vector3 controlPoint1; // Out-tangent (relative to position)
        public Vector3 controlPoint2; // In-tangent (relative to position)
    }

    [SerializeField]
    private List<AnchorPoint> anchorPoints = new();

    // +++ ADDED +++: 为编辑器提供直接访问内部数据的能力，但设为只读
    public IReadOnlyList<AnchorPoint> AnchorPoints => anchorPoints;

    // --- REMOVED ---: 彻底移除不符合Bezier数据结构的 Points 属性
    /*
    public List<Vector3> Points { ... } 
    */

    // --- MODIFIED ---: NumPoints 现在正确地返回“锚点”的数量，符合新接口的定义
    public int NumPoints => anchorPoints.Count;
    public int NumSegments => Mathf.Max(0, anchorPoints.Count - 1);


    #region IPath Interface Implementation

    // --- MODIFIED ---: GetPoint 现在极其纯粹，只返回指定索引的“锚点”位置
    public Vector3 GetPoint(int index, Transform owner)
    {
        if (index >= 0 && index < anchorPoints.Count)
        {
            return owner.TransformPoint(anchorPoints[index].position);
        }
        // 对于无效索引，返回一个安全默认值
        return owner.position;
    }

    public Vector3 GetPointAt(float t, Transform owner)
    {
        if (anchorPoints.Count == 0) return owner.position;
        if (NumSegments == 0) return owner.TransformPoint(anchorPoints[0].position);

        int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(t), 0, NumSegments - 1);
        float localT = t - segmentIndex;

        Vector3 p0 = anchorPoints[segmentIndex].position;
        Vector3 p1 = anchorPoints[segmentIndex].GlobalControl1();
        Vector3 p2 = anchorPoints[segmentIndex + 1].GlobalControl2();
        Vector3 p3 = anchorPoints[segmentIndex + 1].position;

        return owner.TransformPoint(EvaluateCubic(p0, p1, p2, p3, localT));
    }

    public void AddSegment(Vector3 newAnchorWorldPos, Transform owner)
    {
        Vector3 newAnchorLocalPos = owner.InverseTransformPoint(newAnchorWorldPos);
        if (anchorPoints.Count == 0)
        {
            anchorPoints.Add(new AnchorPoint { position = newAnchorLocalPos });
            return;
        }
        var lastPoint = anchorPoints[anchorPoints.Count - 1];
        Vector3 offset = (newAnchorLocalPos - lastPoint.position) * 0.333f;
        lastPoint.controlPoint1 = offset;
        anchorPoints[anchorPoints.Count - 1] = lastPoint;
        anchorPoints.Add(new AnchorPoint { position = newAnchorLocalPos, controlPoint2 = -offset });
    }

    // MovePoint 的实现保持不变，它内部的 DecodeIndex 逻辑是处理扁平化索引的完美封装
    public void MovePoint(int i, Vector3 newWorldPos, Transform owner)
    {
        Vector3 newLocalPos = owner.InverseTransformPoint(newWorldPos);
        int anchorIndex, pointType;
        DecodeIndex(i, out anchorIndex, out pointType);

        if (pointType == 0) // Anchor
        {
            var anchor = anchorPoints[anchorIndex];
            anchor.position = newLocalPos;
            anchorPoints[anchorIndex] = anchor;
        }
        else if (pointType == 1) // Control 1 (Out)
        {
            var anchor = anchorPoints[anchorIndex];
            anchor.controlPoint1 = newLocalPos - anchor.position;
            anchorPoints[anchorIndex] = anchor;
        }
        else // Control 2 (In)
        {
            var anchor = anchorPoints[anchorIndex];
            anchor.controlPoint2 = newLocalPos - anchor.position;
            anchorPoints[anchorIndex] = anchor;
        }
    }

    // InsertSegment 的实现非常精妙，无需改动
    public void InsertSegment(int segmentIndex, Vector3 newPointWorldPos, Transform owner)
    {
        // ... (你的完美代码保持不变)
        if (segmentIndex >= NumSegments) { AddSegment(newPointWorldPos, owner); return; }
        AnchorPoint startAnchor = anchorPoints[segmentIndex];
        AnchorPoint endAnchor = anchorPoints[segmentIndex + 1];
        Vector3 p0 = startAnchor.position, p1 = startAnchor.GlobalControl1(), p2 = endAnchor.GlobalControl2(), p3 = endAnchor.position;
        float t = FindTValueOnSegment(p0, p1, p2, p3, owner.InverseTransformPoint(newPointWorldPos));
        Vector3 p01 = Vector3.Lerp(p0, p1, t), p12 = Vector3.Lerp(p1, p2, t), p23 = Vector3.Lerp(p2, p3, t);
        Vector3 p012 = Vector3.Lerp(p01, p12, t), p123 = Vector3.Lerp(p12, p23, t);
        Vector3 newAnchorPos = Vector3.Lerp(p012, p123, t);
        startAnchor.controlPoint1 = p01 - startAnchor.position;
        var newAnchor = new AnchorPoint { position = newAnchorPos, controlPoint2 = p012 - newAnchorPos, controlPoint1 = p123 - newAnchorPos };
        endAnchor.controlPoint2 = p23 - endAnchor.position; // 修正：应为 p23 - endAnchor.position，以保持曲线形状
        anchorPoints[segmentIndex] = startAnchor;
        anchorPoints[segmentIndex + 1] = endAnchor; // 修正：更新终点锚点
        anchorPoints.Insert(segmentIndex + 1, newAnchor);
    }

    // DeleteSegment 的逻辑依赖于扁平化索引，保持不变
    public void DeleteSegment(int pointIndex)
    {
        if (pointIndex % 3 != 0) return;
        int anchorIndex = pointIndex / 3;
        if (anchorIndex >= 0 && anchorIndex < anchorPoints.Count)
        {
            anchorPoints.RemoveAt(anchorIndex);
        }
    }

    public void ClearSegments() { anchorPoints.Clear(); }

    #endregion

    #region Migration & Bezier-Specific API

    // +++ ADDED +++: 实现数据迁移接口
    public List<Vector3> GetPointsForMigration()
    {
        return anchorPoints.Select(p => p.position).ToList();
    }

    public void SetPointsFromMigration(List<Vector3> localPoints)
    {
        ClearSegments();
        if (localPoints == null || localPoints.Count == 0) return;

        foreach (var point in localPoints)
        {
            anchorPoints.Add(new AnchorPoint { position = point });
        }

        // 迁移后，自动生成合理的控制点以保证曲线平滑
        for (int i = 0; i < anchorPoints.Count; i++)
        {
            var anchor = anchorPoints[i];
            Vector3 posPrev = (i > 0) ? anchorPoints[i - 1].position : anchor.position;
            Vector3 posNext = (i < anchorPoints.Count - 1) ? anchorPoints[i + 1].position : anchor.position;
            Vector3 tangent = (posNext - posPrev).normalized * (posNext - posPrev).magnitude * 0.333f;

            if (i > 0) anchor.controlPoint2 = -tangent;
            if (i < anchorPoints.Count - 1) anchor.controlPoint1 = tangent;

            anchorPoints[i] = anchor;
        }
    }

    // +++ ADDED +++: 为编辑器提供更丰富的、类型安全的API
    public Vector3 GetControlPoint(int anchorIndex, int controlPointType, Transform owner)
    {
        if (anchorIndex < 0 || anchorIndex >= anchorPoints.Count) return owner.position;
        var anchor = anchorPoints[anchorIndex];
        Vector3 localPos = (controlPointType == 1) ? anchor.GlobalControl1() : anchor.GlobalControl2();
        return owner.TransformPoint(localPos);
    }

    #endregion

    #region Internal Helpers (保持不变)

    // DecodeIndex 是一个处理扁平化索引的内部实现细节，封装得很好，予以保留。
    private void DecodeIndex(int i, out int anchorIndex, out int pointType)
    {
        if (i == 0) { anchorIndex = 0; pointType = 0; return; }
        anchorIndex = (i - 1) / 3;
        pointType = (i - 1) % 3;
        if (pointType == 0) { pointType = 1; }
        else if (pointType == 1) { pointType = 2; anchorIndex++; }
        else { pointType = 0; anchorIndex++; }
    }
    private static Vector3 EvaluateCubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }
    private float FindTValueOnSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 point)
    {
        // 这是一个近似算法，对于编辑器交互已足够精确
        const int samples = 100;
        float minSqrDist = float.MaxValue;
        float bestT = 0;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 p = EvaluateCubic(p0, p1, p2, p3, t);
            float sqrDist = (p - point).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                minSqrDist = sqrDist;
                bestT = t;
            }
        }
        return bestT;
    }
    #endregion
}



// 在BezierPath.cs文件底部，添加这个扩展方法，让代码更清晰
public static class BezierPathExtensions
{
    public static Vector3 GlobalControl1(this BezierPath.AnchorPoint anchor) => anchor.position + anchor.controlPoint1;
    public static Vector3 GlobalControl2(this BezierPath.AnchorPoint anchor) => anchor.position + anchor.controlPoint2;
}