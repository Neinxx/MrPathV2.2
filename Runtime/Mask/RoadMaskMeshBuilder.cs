using __temp.MrPathV2.Runtime.Jobs;
using UnityEngine;

namespace __temp.MrPathV2.Runtime.Mask
{
    /// <summary>
    ///     生成用于道路遮罩渲染的简洁网格（沿脊线的四边形条）。
    ///     输入为 SpineData 与 ProfileData，避免依赖高层 PathSpine/PathProfile。
    ///     风格：提前返回、单一职责、简洁高效。
    /// </summary>
    public static class RoadMaskMeshBuilder
    {
        /// <summary>
        ///     基于脊线与道路宽度构建网格。仅包含顶点与索引，UV/颜色不必填。
        /// </summary>
        public static Mesh Build(PathJobsUtility.SpineData spine, PathJobsUtility.ProfileData profile)
        {
            if (!spine.IsCreated || spine.Length < 2) return null;
            var half = Mathf.Max(1e-4f, profile.RoadWidth * 0.5f);

            // 顶点：每个脊线点生成左右两个顶点（左、右）
            var pointCount = spine.Length;
            var vertCount = pointCount * 2;
            var idxCount = (pointCount - 1) * 6;
            if (vertCount <= 0 || idxCount <= 0) return null;

            var verts = new Vector3[vertCount];
            var indices = new int[idxCount];

            // 逐点生成左右边缘
            for (var i = 0; i < pointCount; i++)
            {
                Vector3 p = spine.Points[i];
                Vector3 t = spine.Tangents[i];
                Vector3 n = spine.Normals[i];

                // 右向量：normal × tangent
                // 显式使用 Vector3 类型以避免歧义
                var right = Vector3.Cross(n, t);
                var len = right.magnitude;
                if (len <= 1e-6f)
                {
                    // 退化保护：若法线/切线异常，则采用水平 XZ 平面右向
                    var dir = new Vector2(t.x, t.z);
                    var r2 = new Vector2(-dir.y, dir.x);
                    var rl = r2.magnitude;
                    right = rl > 1e-6f ? new Vector3(r2.x / rl, 0f, r2.y / rl) : Vector3.right;
                }
                else
                {
                    right /= len;
                }

                var leftPos = p - right * half;
                var rightPos = p + right * half;


                var vi = i * 2;
                verts[vi + 0] = leftPos;
                verts[vi + 1] = rightPos;
            }

            // 索引：每个段构成两个三角形
            var idx = 0;
            for (var i = 0; i < pointCount - 1; i++)
            {
                var v0 = i * 2;
                var v1 = v0 + 1;
                var v2 = v0 + 2;
                var v3 = v0 + 3;

                // 三角形 (v0, v1, v2) 与 (v1, v3, v2) 保持一致缠绕
                indices[idx++] = v0;
                indices[idx++] = v1;
                indices[idx++] = v2;

                indices[idx++] = v1;
                indices[idx++] = v3;
                indices[idx++] = v2;
            }

            var mesh = new Mesh
            {
                name = "RoadMaskMesh",
                indexFormat = vertCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
