// 文件路径: Runtime/Jobs/GenerateMeshJob.cs (并行固定容量版)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2
{
    /// <summary>
    /// 并行生成顶点与UV（固定容量，避免 Add 扩容）。
    /// 顶点索引映射：index -> (i=row, j=col)，其中 row=i= index/segments, col=j= index%segments。
    /// </summary>
    [BurstCompile]
    public struct GenerateVerticesJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;

        [WriteOnly] public NativeArray<float3> vertices;
        [WriteOnly] public NativeArray<float2> uvs;
        [ReadOnly] public int segments;
        [ReadOnly] public float2 tiling;

        public void Execute(int index)
        {
            if (spine.Length < 2 || segments < 2) return;
            int i = index / segments;
            int j = index % segments;
            if (i < 0 || i >= spine.Length) return;

            float3 spinePoint = spine.points[i];
            float3 tangent = spine.tangents[i];
            float3 normal = spine.normals[i];
            float3 upVector = profile.forceHorizontal ? new float3(0, 1, 0) : normal;
            float3 right = math.normalize(math.cross(upVector, tangent));

            float t = j / (float)(segments - 1);
            float signedT = t * 2f - 1f;
            float3 offset = right * (signedT * profile.roadWidth * 0.5f);

            // 高性能预览：移除截面竖向抬升，保持网格平整
            vertices[index] = spinePoint + offset;

            // 应用平铺信息到UV
            float u = t * tiling.x;
            float v = ((float)i / math.max(1, (spine.Length - 1))) * tiling.y;
            uvs[index] = new float2(u, v);
        }
    }

    /// <summary>
    /// 并行生成索引缓冲（固定容量）。
    /// 四边形索引映射：quadIndex -> (i=row, j=col)，其中 row=i=quadIndex/(segments-1), col=j=quadIndex%(segments-1)。
    /// 每个四边形写入6个三角索引到 indices[quadIndex*6..quadIndex*6+5]。
    /// </summary>
    [BurstCompile]
    public struct GenerateIndicesJob : IJobParallelFor
    {
        // 写入每个四边形的6个索引，不与job迭代索引一一对应，因此需解除并行写入限制
        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<int> indices;
        [ReadOnly] public int segments;
        [ReadOnly] public int spineLength;

        public void Execute(int quadIndex)
        {
            if (spineLength < 2 || segments < 2) return;
            int quadsPerRow = segments - 1;
            int i = quadIndex / quadsPerRow;
            int j = quadIndex % quadsPerRow;
            if (i < 0 || i >= spineLength - 1) return;

            int baseIndex = i * segments;
            int v0 = baseIndex + j;
            int v1 = baseIndex + j + 1;
            int v2 = baseIndex + segments + j;
            int v3 = baseIndex + segments + j + 1;

            int outBase = quadIndex * 6;
            indices[outBase + 0] = v0;
            indices[outBase + 1] = v2;
            indices[outBase + 2] = v1;
            indices[outBase + 3] = v1;
            indices[outBase + 4] = v2;
            indices[outBase + 5] = v3;
        }
    }

    /// <summary>
    /// 并行生成顶点颜色（RGBA最多4层），使用 Strip + Blend 算法与地形路径一致。
    /// </summary>
    [BurstCompile]
    public struct GenerateVertexColorsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public int segments;
        [ReadOnly] public RecipeData recipe;
        [ReadOnly] public float4 baseColor;

        [WriteOnly] public NativeArray<float4> colors; // RGBA 权重

        public void Execute(int index)
        {
            if (spine.Length < 2 || segments < 2) return;
            int i = index / segments;
            int j = index % segments;
            if (i < 0 || i >= spine.Length) return;

            float t = j / (float)(segments - 1);         // 0..1 左->右
            float signedT = t * 2f - 1f;                  // -1..1 中心为0
            float normalizedDist = math.saturate(math.abs(signedT)); // 0..1 到边缘

            // 只取前4层作为预览（RGBA），其余层忽略
            float r = 0f, g = 0f, b = 0f, a = 0f;
            int layerCount = math.min(4, recipe.Length);
            for (int k = 0; k < layerCount; k++)
            {
                float layerMask = TerrainJobsUtility.EvaluateStrip(recipe.strips, recipe.stripSlices[k], recipe.stripResolution, normalizedDist);
                int blendMode = recipe.blendModes[k];
                switch (k)
                {
                    case 0: r = TerrainJobsUtility.Blend(r, layerMask, blendMode); break;
                    case 1: g = TerrainJobsUtility.Blend(g, layerMask, blendMode); break;
                    case 2: b = TerrainJobsUtility.Blend(b, layerMask, blendMode); break;
                    case 3: a = TerrainJobsUtility.Blend(a, layerMask, blendMode); break;
                }
            }

            // 归一化，便于直觉预览
            float sum = r + g + b + a;
            if (sum > 1e-6f)
            {
                float inv = 1f / sum; r *= inv; g *= inv; b *= inv; a *= inv;
            }
            else
            {
                r = 1f; g = 0f; b = 0f; a = 0f; // 保底：红通道显示
            }

            colors[index] = new float4(r, g, b, a) * baseColor;
        }
    }
}