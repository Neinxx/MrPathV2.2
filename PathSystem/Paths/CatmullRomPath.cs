using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 实现了IPath接口，提供Catmull-Rom样条曲线的功能。
/// V1.7: 包含了自身的编辑器绘制逻辑。
/// </summary>
[System.Serializable]
public class CatmullRomPath : IPath
{
    [SerializeField]
    private List<Vector3> points = new List<Vector3> ();
    public List<Vector3> Points { get => points; set => points = value; }
    public int NumPoints => Points.Count;
    public int NumSegments => Points.Count < 2 ? 0 : Points.Count - 1;

    public void AddSegment (Vector3 newPointWorldPos, Transform owner)
    {
        Points.Add (owner.InverseTransformPoint (newPointWorldPos));
    }

    public void MovePoint (int i, Vector3 newPointWorldPos, Transform owner)
    {
        Points[i] = owner.InverseTransformPoint (newPointWorldPos);
    }

    public Vector3 GetPointAt (float t, Transform owner)
    {

        // 卫兵语句：增强以处理Points列表为空或只有一个点的情况
        if (Points.Count <= 1)
        {
            return Points.Count == 1 ? owner.TransformPoint (Points[0]) : owner.position;
        }

        int p1_idx = Mathf.Clamp (Mathf.FloorToInt (t), 0, NumSegments - 1);
        int p2_idx = p1_idx + 1;
        int p0_idx = p1_idx - 1;
        int p3_idx = p2_idx + 1;
        float localT = t - p1_idx;

        Vector3 p0 = Points[Mathf.Clamp (p0_idx, 0, Points.Count - 1)];
        Vector3 p1 = Points[p1_idx];
        Vector3 p2 = Points[Mathf.Clamp (p2_idx, 0, Points.Count - 1)];
        Vector3 p3 = Points[Mathf.Clamp (p3_idx, 0, Points.Count - 1)];

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

    public void DrawEditorHandles (PathCreator creator)
    {
        Transform owner = creator.transform;

        // 1. 绘制曲线 (通过采样点)
        Handles.color = Color.white;
        const int stepsPerSegment = 20;
        for (int i = 0; i < NumSegments; i++)
        {
            Vector3[] segmentLine = new Vector3[stepsPerSegment + 1];
            for (int j = 0; j <= stepsPerSegment; j++)
            {
                float t = i + (float) j / stepsPerSegment;
                segmentLine[j] = GetPointAt (t, owner);
            }
            Handles.DrawPolyLine (segmentLine);
        }

        // 2. 绘制手柄
        Handles.color = Color.green;
        for (int i = 0; i < Points.Count; i++)
        {
            Vector3 worldPos = owner.TransformPoint (Points[i]);
            Vector3 newWorldPos = Handles.FreeMoveHandle (
                worldPos, Quaternion.identity,
                HandleUtility.GetHandleSize (worldPos) * 0.15f,
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
