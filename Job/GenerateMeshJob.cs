using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 【神兵正刃版】一个Burst编译的Job，用于高效地生成路径的预览网格。
/// - 修正了三角面顶点的环绕顺序，确保网格正面朝外。
/// </summary>
[BurstCompile]
public struct GenerateMeshJob : IJob
{
    #region 输入数据 (Input Data)

    [ReadOnly] public PathSpineForJob spine;
    [ReadOnly] public NativeArray<ProfileSegmentData> segments;

    #endregion

    #region 输出数据 (Output Data)

    public NativeList<Vector3> vertices;
    public NativeList<Vector2> uvs;
    public NativeList<int> allTriangles;
    public NativeArray<int> subMeshTriangleCounts;

    #endregion

    /// <summary>
    /// Job的执行入口点。
    /// </summary>
    public void Execute ()
    {
        // 遍历路径骨架上的每一个采样点
        for (int i = 0; i < spine.points.Length; i++)
        {
            Vector3 spinePoint = spine.points[i];
            Vector3 tangent = spine.tangents[i];
            Vector3 normal = spine.normals[i];
            float timestamp = spine.timestamps[i];

            Vector3 localUp = Vector3.Cross (normal, tangent);

            // 遍历Profile中的每一个分段(图层)
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

                    // --- 【核心修正】采用逆时针(CCW)顺序定义三角面 ---

                    // 第一个三角面
                    allTriangles.Add (prev_A);
                    allTriangles.Add (prev_B);
                    allTriangles.Add (current_A);

                    // 第二个三角面
                    allTriangles.Add (prev_B);
                    allTriangles.Add (current_B);
                    allTriangles.Add (current_A);

                    // ---------------------------------------------

                    subMeshTriangleCounts[j] += 6;
                }
            }
        }
    }
}
