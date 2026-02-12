// 文件: __temp.MrPathV2._2.Runtime.Jobs.PaintSplatmapJob.cs

using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     两阶段地形绘制的第二阶段：读取缓存信息并执行混合。
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct PaintSplatmapJob : IJobParallelFor
    {
        #region 只读数据

        [ReadOnly] public RecipeData Recipe;
        [ReadOnly] public int AlphamapResolution;
        [ReadOnly] public int AlphamapLayerCount;

        // --- 核心输入：缓存的像素信息 ---
        [ReadOnly] public NativeArray<RoadPixelInfo> PixelInfoMap;

        [ReadOnly] public int2 CoverageMin;
        [ReadOnly] public int2 CoverageMax;

        #endregion

        #region 可写数据

        [NativeDisableParallelForRestriction]
        public NativeArray<float> Alphamaps;

        #endregion

        #region 常量定义

        private const float Epsilon = 1e-6f;
        private const float NormalizationThreshold = 1e-5f;

        #endregion

        #region 核心执行方法

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index) // 索引范围 [0, totalPixelsInBounds - 1]
        {
            var info = PixelInfoMap[index];
            if (!info.IsInside) return;

            var numPixelsX = CoverageMax.x - CoverageMin.x + 1;
            var localY = index / numPixelsX;
            var localX = index % numPixelsX;
            var x = CoverageMin.x + localX;
            var y = CoverageMin.y + localY;
            var globalPixelIndex = y * AlphamapResolution + x;

            ApplyTextureBlending(globalPixelIndex, info.NormalizedDist, info.PathProgress);
        }

        #endregion

        #region 私有优化方法

        /// <summary>
        ///     应用纹理混合算法 (使用缓存的 Dist 和 Progress)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTextureBlending(int pixelIndex, float normalizedDist, float pathProgress)
        {
            var baseAlphaIndex = pixelIndex * AlphamapLayerCount;

            // 验证索引范围
            if (!ValidateAlphaIndexRange(baseAlphaIndex))
                return;

            // 注意：不再重置 alpha，保留原有权重以实现透明剔除

            // 应用纹理混合
            var (anyLayerPainted, firstValidSplatIndex) = ApplyLayerBlending(baseAlphaIndex, normalizedDist, pathProgress);

            // 若本像素涂绘了至少一个配方图层，则剔除所有非配方图层的权重（仅在道路覆盖区）
            if (anyLayerPainted)
            {
                ZeroOutNonRecipeLayers(baseAlphaIndex);
            }

            // 未绘制任何图层时，保持原权重，不做强制填充
            HandleUnpaintedLayers(baseAlphaIndex, anyLayerPainted, firstValidSplatIndex);

            // 标准化alpha权重，保持总和为 1
            NormalizeAlphaWeights(baseAlphaIndex, firstValidSplatIndex);
        }

        /// <summary>
        /// 验证alpha索引范围是否有效
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateAlphaIndexRange(int baseAlphaIndex)
        {
            return baseAlphaIndex >= 0 && baseAlphaIndex + AlphamapLayerCount <= Alphamaps.Length;
        }

        /// <summary>
        /// 应用图层混合
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (bool anyLayerPainted, int firstValidSplatIndex) ApplyLayerBlending(int baseAlphaIndex, float normalizedDist, float pathProgress)
        {
            var anyLayerPainted = false;
            var firstValidSplatIndex = -1;

            for (var layerIndex = 0; layerIndex < Recipe.Length; layerIndex++)
            {
                var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (!ValidateSplatIndex(splatIndex))
                    continue;

                // 更新第一个有效的splat索引
                if (firstValidSplatIndex == -1)
                    firstValidSplatIndex = splatIndex;

                // 获取遮罩值
                var maskValue = GetMaskValue(layerIndex, normalizedDist, pathProgress);

                // 检查是否有图层被绘制
                if (maskValue > Epsilon)
                    anyLayerPainted = true;

                // 应用混合
                ApplyBlendingToLayer(baseAlphaIndex, splatIndex, maskValue, layerIndex);
            }

            return (anyLayerPainted, firstValidSplatIndex);
        }

        /// <summary>
        /// 验证splat索引是否有效
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateSplatIndex(int splatIndex)
        {
            return splatIndex >= 0 && splatIndex < AlphamapLayerCount;
        }

        /// <summary>
        /// 在道路覆盖区清除非配方图层的权重
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ZeroOutNonRecipeLayers(int baseAlphaIndex)
        {
            for (var i = 0; i < AlphamapLayerCount; i++)
            {
                if (!IsRecipeSplatIndex(i))
                {
                    Alphamaps[baseAlphaIndex + i] = 0f;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRecipeSplatIndex(int splatIndex)
        {
            for (var k = 0; k < Recipe.Length; k++)
            {
                if (Recipe.TerrainLayerIndices[k] == splatIndex) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取遮罩值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetMaskValue(int layerIndex, float normalizedDist, float pathProgress)
        {
            // 优先使用 MaskAtlas（支持 pathProgress）
            if (Recipe.MaskAtlas.IsCreated)
            {
                return TerrainJobsUtility.SampleMaskAtlas(
                    Recipe.MaskAtlas, Recipe.AtlasWidth, Recipe.PathSamples,
                    layerIndex, normalizedDist, pathProgress);
            }
            else if (Recipe.Strips.IsCreated)
            {
                // Strips (1D) 已在生成时预乘 opacity，这里不再乘
                return TerrainJobsUtility.EvaluateStrip(
                    Recipe.Strips, Recipe.StripSlices[layerIndex],
                    Recipe.StripResolution, normalizedDist);
            }
            else
            {
                // 没有遮罩时不涂绘
                return 0f;
            }
        }

        /// <summary>
        /// 应用混合到指定图层
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyBlendingToLayer(int baseAlphaIndex, int splatIndex, float maskValue, int layerIndex)
        {
            var alphaMapIndex = baseAlphaIndex + splatIndex;
            var currentValue = Alphamaps[alphaMapIndex];
            var blendedValue = TerrainJobsUtility.Blend(currentValue, maskValue, Recipe.BlendModes[layerIndex]);

            Alphamaps[alphaMapIndex] = blendedValue;
        }

        /// <summary>
        /// 未绘制任何图层时保持原权重不变
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleUnpaintedLayers(int baseAlphaIndex, bool anyLayerPainted, int firstValidSplatIndex)
        {
            // 保持空实现：不强制写 1，避免清空原地形纹理
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NormalizeAlphaWeights(int baseAlphaIndex, int firstValidSplatIndex)
        {
            TerrainJobsUtility.NormalizeWeightsKeep(Alphamaps, baseAlphaIndex, AlphamapLayerCount, firstValidSplatIndex);
        }

        #endregion
    }
}
