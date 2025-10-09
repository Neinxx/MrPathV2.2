using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【终极纯净版】一个Burst编译的Job，用于高效地生成路径的预览网格。
    /// - 废弃了所有旧的localUp计算逻辑，完全依赖于传入的surfaceNormals。
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
        public void Execute()
        {
            for (int i = 0; i < spine.points.Length; i++)
            {
                // --- 1. 获取三大核心法则 ---
                Vector3 spinePoint = spine.points[i];
                Vector3 tangent = spine.tangents[i];      // “前进”方向
                Vector3 localUp = spine.surfaceNormals[i];  // “向上”方向 (源自大地)

                // --- 2. 推演“右方”法则 ---
                Vector3 normal = Vector3.Cross(tangent, localUp).normalized;


                Vector3 right = Vector3.Cross(tangent, localUp).normalized;


                // --- 3. 守护：若前进与向上平行（极端情况），则使用世界右方作为备用
                if (right.sqrMagnitude < 0.001f)
                {
                    right = Vector3.Cross(tangent, Vector3.right).normalized;
                }

                float timestamp = spine.timestamps[i];

                for (int j = 0; j < segments.Length; j++)
                {
                    ProfileSegmentData segment = segments[j];

                    // --- 4. 依据三大法则，构建顶点 ---
                    Vector3 vertA = spinePoint + right * (segment.horizontalOffset - segment.width / 2) + localUp * segment.verticalOffset;
                    Vector3 vertB = spinePoint + right * (segment.horizontalOffset + segment.width / 2) + localUp * segment.verticalOffset;
                    // ...
                    vertices.Add(vertA);
                    vertices.Add(vertB);
                    uvs.Add(new Vector2(0, timestamp));
                    uvs.Add(new Vector2(1, timestamp));

                    // --- 5. 连接三角面 (逻辑不变) ---
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
                        allTriangles.Add(prev_A);
                        allTriangles.Add(prev_B);
                        allTriangles.Add(current_A);

                        // 第二个三角面
                        allTriangles.Add(prev_B);
                        allTriangles.Add(current_B);
                        allTriangles.Add(current_A);

                        // ---------------------------------------------

                        subMeshTriangleCounts[j] += 6;
                    }
                }
            }
        }
    }
}