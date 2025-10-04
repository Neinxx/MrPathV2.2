using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 一个Burst编译的Job，用于高效地生成路径的预览网格。
/// V2.7 (Sub-mesh Support):
/// - 支持为每个Profile分段生成独立的子网格数据。
/// - 包含了所有之前的性能和视觉修正。
/// </summary>
[BurstCompile]
public struct GenerateMeshJob : IJob
{
    // --- 输入数据 ---
    [ReadOnly] public PathSpineForJob spine;
    [ReadOnly] public NativeArray<ProfileSegmentData> segments;

    // --- 输出数据 ---
    public NativeList<Vector3> vertices;
    public NativeList<Vector2> uvs;
    public NativeList<int> allTriangles;
    public NativeArray<int> subMeshTriangleCounts;

    /// <summary>
    /// Job的执行入口点。
    /// </summary>
    public void Execute ()
    {
        // 遍历路径骨架上的每一个采样点
        for (int i = 0; i < spine.points.Length; i++)
        {
            // 获取当前骨架点的核心数据
            Vector3 spinePoint = spine.points[i];
            Vector3 tangent = spine.tangents[i];
            Vector3 normal = spine.normals[i];
            float timestamp = spine.timestamps[i];

            // 动态计算当前点局部的“Up”向量
            Vector3 localUp = Vector3.Cross (normal, tangent);

            // 遍历Profile中的每一个分段
            for (int j = 0; j < segments.Length; j++)
            {
                ProfileSegmentData segment = segments[j];

                // --- 顶点和UV生成 ---
                Vector3 vertA = spinePoint + normal * (segment.horizontalOffset - segment.width / 2) + localUp * segment.verticalOffset;
                Vector3 vertB = spinePoint + normal * (segment.horizontalOffset + segment.width / 2) + localUp * segment.verticalOffset;
                vertices.Add (vertA);
                vertices.Add (vertB);
                uvs.Add (new Vector2 (0, timestamp));
                uvs.Add (new Vector2 (1, timestamp));

                // --- 三角面生成 ---
                if (i > 0)
                {
                    int vertsPerPointPerSegment = 2;
                    int vertsPerSpinePoint = segments.Length * vertsPerPointPerSegment;

                    int baseCurrent = (i * vertsPerSpinePoint) + (j * vertsPerPointPerSegment);
                    int basePrev = ((i - 1) * vertsPerSpinePoint) + (j * vertsPerPointPerSegment);

                    int prev_A = basePrev;
                    int prev_B = basePrev + 1;
                    int current_A = baseCurrent;
                    int current_B = baseCurrent + 1;

                    // 定义逆时针(CCW)顺序的三角面以正确显示正面
                    allTriangles.Add (prev_A);
                    allTriangles.Add (current_A);
                    allTriangles.Add (prev_B);

                    allTriangles.Add (prev_B);
                    allTriangles.Add (current_A);
                    allTriangles.Add (current_B);

                    // 为当前子网格(分段j)的索引计数器加6
                    subMeshTriangleCounts[j] += 6;
                }
            }
        }
    }
}
