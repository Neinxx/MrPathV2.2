// GenerateMeshJob.cs (终极稳定版 - 逐段构建算法)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
namespace MrPathV2
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct GenerateMeshJob : IJob
    {
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;

        public NativeList<float3> vertices;
        public NativeList<int> triangles;
        public NativeList<float2> uvs;
        public NativeList<Color32> colors;
        public NativeList<float3> normals;

        public void Execute()
        {
            if (spine.Length < 2 || profile.Length == 0) return;

            for (int layerIndex = 0; layerIndex < profile.Length; layerIndex++)
            {
                var layer = profile.layers[layerIndex];
                int vertexOffset = vertices.Length;

                // --- 逐段构建算法 ---
                for (int i = 0; i < spine.Length - 1; i++)
                {
                    // 获取当前段 和 下一段 的顶点信息
                    GetSegmentVertices(i, layer, out float3 v0, out float3 v1);
                    GetSegmentVertices(i + 1, layer, out float3 v2, out float3 v3);

                    // 添加顶点
                    vertices.Add(v0); // 左下
                    vertices.Add(v1); // 右下
                    vertices.Add(v2); // 左上
                    vertices.Add(v3); // 右上

                    // 添加 UV 和 颜色
                    float vCoord_i = (float)i / (spine.Length - 1);
                    float vCoord_i1 = (float)(i + 1) / (spine.Length - 1);
                    uvs.Add(new float2(0, vCoord_i)); uvs.Add(new float2(1, vCoord_i));
                    uvs.Add(new float2(0, vCoord_i1)); uvs.Add(new float2(1, vCoord_i1));

                    Color32 layerColor = GetColorForLayer(layerIndex);
                    colors.Add(layerColor); colors.Add(layerColor);
                    colors.Add(layerColor); colors.Add(layerColor);

                    // 添加法线
                    normals.Add(spine.normals[i]); normals.Add(spine.normals[i]);
                    normals.Add(spine.normals[i + 1]); normals.Add(spine.normals[i + 1]);

                    // 添加三角形索引
                    int baseIndex = vertexOffset + i * 4;
                    triangles.Add(baseIndex + 0); // 左下
                    triangles.Add(baseIndex + 2); // 左上
                    triangles.Add(baseIndex + 1); // 右下

                    triangles.Add(baseIndex + 1); // 右下
                    triangles.Add(baseIndex + 2); // 左上
                    triangles.Add(baseIndex + 3); // 右上
                }
            }
        }

        // 辅助方法，用于获取在指定脊椎点上的路面左右顶点
        private void GetSegmentVertices(int spineIndex, PathJobsUtility.LayerData layer, out float3 vertLeft, out float3 vertRight)
        {
            float3 spinePoint = spine.points[spineIndex];
            float3 tangent = spine.tangents[spineIndex];
            float3 normal = spine.normals[spineIndex];

            float3 upVector = profile.forceHorizontal ? new float3(0, 1, 0) : normal;
            float3 right = math.normalize(math.cross(upVector, tangent));

            float halfWidth = layer.width / 2f;
            float3 horizontalOffsetVec = right * layer.horizontalOffset;
            float3 widthOffsetVec = right * halfWidth;
            float3 verticalOffsetVec = normal * layer.verticalOffset;

            vertLeft = spinePoint + horizontalOffsetVec - widthOffsetVec + verticalOffsetVec;
            vertRight = spinePoint + horizontalOffsetVec + widthOffsetVec + verticalOffsetVec;
        }

        private Color32 GetColorForLayer(int layerIndex)
        {
            byte r = (byte)(layerIndex == 0 ? 255 : 0);
            byte g = (byte)(layerIndex == 1 ? 255 : 0);
            byte b = (byte)(layerIndex == 2 ? 255 : 0);
            byte a = (byte)(layerIndex == 3 ? 255 : 0);
            return new Color32(r, g, b, a);
        }
    }
}