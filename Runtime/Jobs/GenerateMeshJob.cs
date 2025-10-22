// 文件路径: Runtime/Jobs/GenerateMeshJob.cs (并行固定容量版)

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    /// 并行生成顶点与UV（固定容量，避免 Add 扩容）。
    /// 顶点索引映射：index -> (i=row, j=col)，其中 row=i= index/segments, col=j= index%segments。
    /// </summary>
    [BurstCompile]
    public struct GenerateVerticesJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;

        [WriteOnly] public NativeArray<float3> Vertices;
        [WriteOnly] public NativeArray<float2> Uvs;
        [ReadOnly] public int Segments;
        [ReadOnly] public float2 Tiling;

        public void Execute(int index)
        {
            if (Spine.Length < 2 || Segments < 2) return;
            int i = index / Segments;
            int j = index % Segments;
            if (i < 0 || i >= Spine.Length) return;

            float3 spinePoint = Spine.Points[i];
            float3 tangent = Spine.Tangents[i];
            float3 normal = Spine.Normals[i];
            float3 upVector = Profile.ForceHorizontal ? new float3(0, 1, 0) : normal;
            float3 right = math.normalize(math.cross(upVector, tangent));

            float t = j / (float)(Segments - 1);
            float signedT = t * 2f - 1f;
            float3 offset = right * (signedT * Profile.RoadWidth * 0.5f);

            // 高性能预览：移除截面竖向抬升，保持网格平整
            Vertices[index] = spinePoint + offset;

            // 应用平铺信息到UV
            float u = t * Tiling.x;
            float v = ((float)i / math.max(1, (Spine.Length - 1))) * Tiling.y;
            Uvs[index] = new float2(u, v);
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
        [WriteOnly] public NativeArray<int> Indices;
        [ReadOnly] public int Segments;
        [ReadOnly] public int SpineLength;

        public void Execute(int quadIndex)
        {
            if (SpineLength < 2 || Segments < 2) return;
            var quadsPerRow = Segments - 1;
            var i = quadIndex / quadsPerRow;
            var j = quadIndex % quadsPerRow;
            if (i < 0 || i >= SpineLength - 1) return;

            var baseIndex = i * Segments;
            var v0 = baseIndex + j;
            var v1 = baseIndex + j + 1;
            var v2 = baseIndex + Segments + j;
            var v3 = baseIndex + Segments + j + 1;

            var outBase = quadIndex * 6;
            Indices[outBase + 0] = v0;
            Indices[outBase + 1] = v2;
            Indices[outBase + 2] = v1;
            Indices[outBase + 3] = v1;
            Indices[outBase + 4] = v2;
            Indices[outBase + 5] = v3;
        }
    }

    /// <summary>
    /// 并行生成顶点颜色（RGBA最多4层），使用 Strip + Blend 算法与地形路径一致。
    /// </summary>
    [BurstCompile]
    public struct GenerateVertexColorsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public int Segments;
        [ReadOnly] public RecipeData Recipe;
        [ReadOnly] public float4 BaseColor;

        [WriteOnly] public NativeArray<float4> Colors; // RGBA 权重

        public void Execute(int index)
        {
            if (Spine.Length < 2 || Segments < 2) return;
            var i = index / Segments;
            var j = index % Segments;
            if (i < 0 || i >= Spine.Length) return;

            var t = j / (float)(Segments - 1);         // 0..1 左->右
            var signedT = t * 2f - 1f;                  // -1..1 中心为0
            var normalizedDist = math.saturate(math.abs(signedT)); // 0..1 到边缘

            // 只取前4层作为预览（RGBA），其余层忽略
            float r = 0f, g = 0f, b = 0f, a = 0f;
            var layerCount = math.min(4, Recipe.Length);
            for (var k = 0; k < layerCount; k++)
            {
                float layerMask;
                if (Recipe.MaskAtlas.IsCreated)
                {
                    layerMask = TerrainJobsUtility.SampleMaskAtlas(Recipe.MaskAtlas, Recipe.AtlasWidth, k, normalizedDist);
                }

                else
                {
                    layerMask = TerrainJobsUtility.EvaluateStrip(Recipe.Strips, Recipe.StripSlices[k], Recipe.StripResolution, normalizedDist) * Recipe.Opacities[k];
                }
                var blendMode = Recipe.BlendModes[k];
                switch (k)
                {
                    case 0: r = TerrainJobsUtility.Blend(r, layerMask, blendMode); break;
                    case 1: g = TerrainJobsUtility.Blend(g, layerMask, blendMode); break;
                    case 2: b = TerrainJobsUtility.Blend(b, layerMask, blendMode); break;
                    case 3: a = TerrainJobsUtility.Blend(a, layerMask, blendMode); break;
                }
            }

            // 不再在顶点阶段归一化，保持 LUT 的原始不透明度信息
            Colors[index] = new float4(r, g, b, a) * BaseColor;
        }
    }
}