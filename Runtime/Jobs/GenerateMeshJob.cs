// 文件路径: Runtime/Jobs/GenerateMeshJob.cs (并行固定容量版)

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     并行生成顶点与UV（固定容量，避免 Add 扩容）。
    ///     顶点索引映射：index -> (i=row, j=col)，其中 row=i= index/segments, col=j= index%segments。
    /// </summary>
    [BurstCompile]
    public struct GenerateVerticesJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] public NativeArray<float> AccumulatedDistances;

        [WriteOnly] public NativeArray<float3> Vertices;
        [WriteOnly] public NativeArray<float2> Uvs;
        [ReadOnly] public int Segments;
        [ReadOnly] public float2 Tiling;

        public void Execute(int index)
        {
            if (Spine.Length < 2 || Segments < 2) return;
            var i = index / Segments;
            var j = index % Segments;
            if (i < 0 || i >= Spine.Length) return;

            var spinePoint = Spine.Points[i];
            var tangent = Spine.Tangents[i];
            var normal = Spine.Normals[i];
            var upVector = Profile.ForceHorizontal ? new float3(0, 1, 0) : normal;
            var right = math.normalize(math.cross(upVector, tangent));

            var t = j / (float)(Segments - 1);
            var signedT = t * 2f - 1f;
            var offset = right * (signedT * Profile.RoadWidth * 0.5f);

            // 高性能预览：移除截面竖向抬升，保持网格平整
            var basePos = spinePoint + offset;

            // 风格化：为两侧边缘加入横向噪声扰动
            if (Profile.EnableEdgeNoise && Profile.EdgeNoiseAmplitude > 1e-6f && (j == 0 || j == Segments - 1))
            {
                float totalLen1 = math.max(1e-5f, AccumulatedDistances[Spine.Length - 1]);
                var pathProgress = AccumulatedDistances[i] / totalLen1; // 0..1
                var edgeSign = (j == Segments - 1) ? 1f : -1f;
                // 生成可重复且可控的抖动项
                var h = math.sin(Profile.EdgeNoiseSeed * 12.9898f + i * 78.233f + j * 37.719f);
                var jitter = (math.abs(h) - math.floor(math.abs(h))) * Profile.EdgeNoiseJitter; // 0..jitter
                var phase = pathProgress * Profile.EdgeNoisePeriod + jitter;
                var n = math.sin(phase * 6.2831853f); // 2π
                var delta = right * edgeSign * n * Profile.EdgeNoiseAmplitude;
                basePos += delta;
            }

            Vertices[index] = basePos;

            // 统一UV语义：网格UV直接输出归一化 Across/Along，不再乘平铺次数
            var u = t; // 0..1 左->右
            var totalLen = math.max(1e-5f, AccumulatedDistances[Spine.Length - 1]);
            var progressByDistance = AccumulatedDistances[i] / totalLen; // 0..1 沿路径
            var v = progressByDistance; // 0..1 起点->终点
            Uvs[index] = new float2(u, v);
        }
    }

    /// <summary>
    ///     并行生成索引缓冲（固定容量）。
    ///     四边形索引映射：quadIndex -> (i=row, j=col)，其中 row=i=quadIndex/(segments-1), col=j=quadIndex%(segments-1)。
    ///     每个四边形写入6个三角索引到 indices[quadIndex*6..quadIndex*6+5]。
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
    ///     并行生成顶点颜色（RGBA最多4层），使用 Strip + Blend 算法与地形路径一致。
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
            // 参数验证
            if (!ValidateInput(index))
                return;

            // 计算索引和参数
            var (i, j, t, signedT, normalizedDist, pathProgress) = CalculateParameters(index);

            // 执行纹理混合
            var (r, g, b, a) = PerformTextureBlending(normalizedDist, pathProgress);

            // 设置最终颜色
            Colors[index] = new float4(r, g, b, a) * BaseColor;
        }

        /// <summary>
        ///     验证输入参数是否有效
        /// </summary>
        /// <param name="index">当前处理的索引</param>
        /// <returns>参数是否有效</returns>
        private bool ValidateInput(int index)
        {
            if (Spine.Length < 2 || Segments < 2)
                return false;

            var i = index / Segments;
            if (i < 0 || i >= Spine.Length)
                return false;

            return true;
        }

        /// <summary>
        ///     计算所需的参数
        /// </summary>
        /// <param name="index">当前处理的索引</param>
        /// <returns>计算得到的参数元组</returns>
        private (int i, int j, float t, float signedT, float normalizedDist, float pathProgress) CalculateParameters(int index)
        {
            var i = index / Segments;
            var j = index % Segments;

            var t = j / (float)(Segments - 1); // 0..1 左->右
            var signedT = t * 2f - 1f; // -1..1 中心为0
            var normalizedDist = t; // 统一为左->右 0..1（不镜像）

            // 新增：计算沿路径的进度 0..1（基于当前脊线索引）
            var segCount = math.max(1, Spine.Length - 1);
            var pathProgress = math.saturate(i / (float)segCount);

            return (i, j, t, signedT, normalizedDist, pathProgress);
        }

        /// <summary>
        ///     执行纹理混合计算
        /// </summary>
        /// <param name="normalizedDist">标准化距离</param>
        /// <param name="pathProgress">路径进度</param>
        /// <returns>RGBA颜色值</returns>
        private (float r, float g, float b, float a) PerformTextureBlending(float normalizedDist, float pathProgress)
        {
            float r = 0f, g = 0f, b = 0f, a = 0f;
            var layerCount = math.min(4, Recipe.Length);

            for (var k = 0; k < layerCount; k++)
            {
                var rawMask = GetLayerMask(k, normalizedDist, pathProgress);
                var opacity = math.saturate(Recipe.Opacities[k]);
                var layerValue = rawMask * opacity; // 统一在混合阶段乘以每层不透明度
                var blendMode = Recipe.BlendModes[k];

                switch (k)
                {
                    case 0: r = TerrainJobsUtility.Blend(r, layerValue, blendMode); break;
                    case 1: g = TerrainJobsUtility.Blend(g, layerValue, blendMode); break;
                    case 2: b = TerrainJobsUtility.Blend(b, layerValue, blendMode); break;
                    case 3: a = TerrainJobsUtility.Blend(a, layerValue, blendMode); break;
                }
            }

            return (r, g, b, a);
        }

        /// <summary>
        ///     获取图层遮罩值
        /// </summary>
        /// <param name="layerIndex">图层索引</param>
        /// <param name="normalizedDist">标准化距离</param>
        /// <param name="pathProgress">路径进度</param>
        /// <returns>图层遮罩值</returns>
        private float GetLayerMask(int layerIndex, float normalizedDist, float pathProgress)
        {
            if (Recipe.MaskAtlas.IsCreated)
            {
                // 使用 2D Mask Atlas 采样，支持沿路径变化
                return TerrainJobsUtility.SampleMaskAtlas(
                    Recipe.MaskAtlas,
                    Recipe.AtlasWidth,
                    Recipe.PathSamples,
                    layerIndex,
                    normalizedDist,
                    pathProgress);
            }
            // 仅返回原始遮罩值，不在此处乘以不透明度
            return TerrainJobsUtility.EvaluateStrip(Recipe.Strips, Recipe.StripSlices[layerIndex], Recipe.StripResolution, normalizedDist);
        }

    }
}
