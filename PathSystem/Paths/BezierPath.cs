using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 实现了IPath接口，提供三阶贝塞尔曲线的功能。
/// V1.7: 包含了自身的编辑器绘制逻辑。
/// </summary>
[System.Serializable]
public class BezierPath : IPath
{
    [SerializeField]
    private List<Vector3> points = new List<Vector3> ();
    public List<Vector3> Points { get => points; set => points = value; }
    public int NumPoints => Points.Count;

    public int NumSegments => (Points.Count - 1) / 3;

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

    public void MovePoint (int i, Vector3 newWorldPos, Transform owner)
    {
        Vector3 newLocalPos = owner.InverseTransformPoint (newWorldPos);
        Vector3 deltaMove = newLocalPos - Points[i];

        Points[i] = newLocalPos;

        // 如果移动的是锚点，则其关联的控制点也应跟随移动
        if (i % 3 == 0)
        {
            if (i + 1 < Points.Count) Points[i + 1] += deltaMove;
            if (i - 1 >= 0) Points[i - 1] += deltaMove;
        }
        else // 如果移动的是控制点
        {
            // TODO: 在此实现Aligned和Broken模式的逻辑
        }
    }

    public Vector3 GetPointAt (float t, Transform owner)
    {
        // ====================== 卫兵语句 (Guard Clause) ======================
        // 如果没有任何曲线段 (即只有一个点或没有点)，则直接返回第一个点的位置
        // 卫兵语句：增强以处理Points列表为空的最终边界情况
        if (Points.Count == 0)
        {
            return owner.position; // 如果路径为空，返回对象原点
        }
        if (NumSegments == 0)
        {
            return owner.TransformPoint (Points[0]); // 如果只有一个点
        }
        // =================================================================

        int segmentIndex = Mathf.Clamp (Mathf.FloorToInt (t), 0, NumSegments - 1);
        float localT = t - segmentIndex;

        Vector3[] segment = GetPointsInSegment (segmentIndex);

        float u = 1 - localT;
        float tt = localT * localT;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * localT;

        Vector3 p = uuu * segment[0];
        p += 3 * uu * localT * segment[1];
        p += 3 * u * tt * segment[2];
        p += ttt * segment[3];

        return owner.TransformPoint (p);
    }

    public Vector3[] GetPointsInSegment (int segmentIndex)
    {
        int startIndex = segmentIndex * 3;
        return new Vector3[]
        {
            Points[startIndex], Points[startIndex + 1],
                Points[startIndex + 2], Points[startIndex + 3]
        };
    }

    public void DrawEditorHandles (PathCreator creator)
    {
        Transform owner = creator.transform;

        // 1. 绘制曲线
        for (int i = 0; i < NumSegments; i++)
        {
            Vector3[] segment = GetPointsInSegment (i);
            Vector3 p0 = owner.TransformPoint (segment[0]);
            Vector3 p1 = owner.TransformPoint (segment[1]);
            Vector3 p2 = owner.TransformPoint (segment[2]);
            Vector3 p3 = owner.TransformPoint (segment[3]);
            Handles.DrawBezier (p0, p3, p1, p2, Color.white, null, 2f);
        }

        // 2. 绘制切线
        Handles.color = Color.gray;
        for (int i = 0; i < Points.Count; i++)
        {
            if (i % 3 == 0) // 是锚点
            {
                Vector3 anchorPos = owner.TransformPoint (Points[i]);
                if (i + 1 < Points.Count) Handles.DrawLine (anchorPos, owner.TransformPoint (Points[i + 1]));
                if (i - 1 >= 0) Handles.DrawLine (anchorPos, owner.TransformPoint (Points[i - 1]));
            }
        }

        // 3. 绘制手柄
        for (int i = 0; i < Points.Count; i++)
        {
            bool isAnchor = i % 3 == 0;
            Handles.color = isAnchor ? Color.green : Color.white;
            float handleSize = isAnchor ? 0.15f : 0.08f;

            Vector3 worldPos = owner.TransformPoint (Points[i]);
            Vector3 newWorldPos = Handles.FreeMoveHandle (
                worldPos, Quaternion.identity,
                HandleUtility.GetHandleSize (worldPos) * handleSize,
                Vector3.zero, Handles.SphereHandleCap
            );

            if (newWorldPos != worldPos)
            {
                if (creator.snapToTerrain)
                {
                    Terrain terrain = Terrain.activeTerrain; // (可优化为更复杂的寻路逻辑)
                    if (terrain != null)
                    {
                        // 获取地形在手柄正下方的高度
                        float terrainHeight = terrain.SampleHeight (newWorldPos) + terrain.GetPosition ().y;

                        // 计算完全吸附时的目标位置
                        Vector3 snappedPos = new Vector3 (newWorldPos.x, terrainHeight, newWorldPos.z);

                        // 根据吸附强度进行插值
                        newWorldPos = Vector3.Lerp (newWorldPos, snappedPos, creator.snapStrength);
                    }
                }
                creator.MovePoint (i, newWorldPos);
            }
        }
    }
}
