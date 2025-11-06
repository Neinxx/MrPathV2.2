using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
        // 不透明绘制开关与阈值（当开启时应用 alpha clip）
        [ReadOnly] public bool OpaquePainting;
        [ReadOnly] public float AlphaClipThreshold; // 建议与预览保持一致：0.2f

        // --- 核心输入：缓存的像素信息 ---
        [ReadOnly] public NativeArray<RoadPixelInfo> PixelInfoMap;

        [ReadOnly] public Vector2Int CoverageMin;
        [ReadOnly] public Vector2Int CoverageMax;

        #endregion

        #region 可写数据

        [NativeDisableParallelForRestriction]
        public NativeArray<float> Alphamaps;

        #endregion

        #region 常量定义

        private const float Epsilon = 1e-6f;
        private const float NormalizationThreshold = 1e-5f;
        // 与 GPU 预览保持一致的小权重截断阈值，避免边缘产生微小残留导致的黑边
        private const float SmallWeightCutoff = 4e-4f;
        // 总权重门槛：当所有配方层权重之和小于该值时，视为未涂绘
        private const float PaintGateThreshold = 1e-3f;

        #endregion

        #region 核心执行方法

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index) // 索引范围 [0, totalPixelsInBounds - 1]
        {
            var info = PixelInfoMap[index];
            if (!info.IsInside) return;

            // 基于局部数组布局：index 即为 (localY * width + localX)
            var baseAlphaIndex = index * AlphamapLayerCount;
            ApplyTextureBlending(baseAlphaIndex, info.NormalizedDist, info.PathProgress);
        }

        #endregion

        #region 私有优化方法

        /// <summary>
        ///     应用纹理混合算法 (使用缓存的 Dist 和 Progress)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTextureBlending(int baseAlphaIndex, float normalizedDist, float pathProgress)
        {
            // 验证索引范围
            if (!ValidateAlphaIndexRange(baseAlphaIndex))
                return;

            // 先判断该像素是否需要涂绘，并取一个有效 splat 供归一化使用
            if (!ShouldPaintPixel(normalizedDist, pathProgress, out var firstValidSplatIndex))
                return;

            // 根据开关：不透明时使用 alpha clip；否则按有序覆盖进行透明混合
            var anyLayerPainted = ApplyLayerBlending(baseAlphaIndex, normalizedDist, pathProgress, firstValidSplatIndex);

            // 不透明绘制：当确实涂绘到图层时，将非配方层权重清零以避免与底层混合
            if (OpaquePainting && anyLayerPainted)
            {
                ZeroOutNonRecipeLayers(baseAlphaIndex);
            }

            // 若未涂绘任何图层，直接返回以避免不必要的标准化开销
            if (!anyLayerPainted)
            {
                HandleUnpaintedLayers(baseAlphaIndex, anyLayerPainted, firstValidSplatIndex);
                return;
            }

            // 标准化alpha权重，保持总和为 1
            NormalizeAlphaWeights(baseAlphaIndex, firstValidSplatIndex);
        }

        /// <summary>
        ///     计算是否应当对该像素进行涂绘：总权重未达到门槛则视为未涂绘
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldPaintPixel(float normalizedDist, float pathProgress, out int firstValidSplatIndex)
        {
            firstValidSplatIndex = -1;
            var totalMask = 0f;
            // 从上到下（索引高到低）遍历，且当 OpaquePainting 开启时采用 alpha clip 门控
            for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
            {
                var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (!ValidateSplatIndex(splatIndex)) continue;

                var maskValue = GetMaskValue(layerIndex, normalizedDist, pathProgress);
                if (OpaquePainting && maskValue < AlphaClipThreshold)
                {
                    continue; // 门控：低于裁剪阈值即视为不参与本次绘制
                }

                if (maskValue <= SmallWeightCutoff) continue;

                totalMask += maskValue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;
            }
            return totalMask > PaintGateThreshold && firstValidSplatIndex != -1;
        }

        /// <summary>
        ///     验证alpha索引范围是否有效
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateAlphaIndexRange(int baseAlphaIndex) => baseAlphaIndex >= 0 && baseAlphaIndex + AlphamapLayerCount <= Alphamaps.Length;

        /// <summary>
        ///     应用图层混合
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ApplyLayerBlending(int baseAlphaIndex, float normalizedDist, float pathProgress, int firstValidSplatIndex)
        {
            var anyLayerPainted = false;

            // 统一有序覆盖（与 GPU/预览一致），区别：当 OpaquePainting 开启时对 mask 进行 alpha clip 门控

            // 预先累计“其他层”的原始总和（非配方层与配方层的旧值）
            var originalOthersSum = 0f;
            for (var i = 0; i < AlphamapLayerCount; i++)
            {
                if (!IsRecipeSplatIndex(i))
                    originalOthersSum += Alphamaps[baseAlphaIndex + i];
            }

            // 第一步：计算配方层总贡献（不写回），用于缩放其他层
            var remaining = 1f;
            var sumRecipe = 0f;
            for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
            {
                var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (!ValidateSplatIndex(splatIndex)) continue;

                var maskAlpha = math.saturate(GetMaskValue(layerIndex, normalizedDist, pathProgress));
                if (OpaquePainting && maskAlpha < AlphaClipThreshold)
                {
                    continue; // clip 门控
                }

                var strength = Recipe.Opacities.IsCreated && layerIndex >= 0 && layerIndex < Recipe.Opacities.Length
                    ? math.saturate(Recipe.Opacities[layerIndex])
                    : 1f;
                var selfAlpha = math.saturate(maskAlpha * strength);
                var contribute = math.min(selfAlpha, remaining);
                if (contribute > SmallWeightCutoff)
                {
                    sumRecipe += contribute;
                    remaining = math.max(0f, remaining - contribute);
                }
                if (remaining <= Epsilon) break;
            }

            // 第二步：缩放非配方层，使其总和变为 (1 - sumRecipe)
            if (originalOthersSum > SmallWeightCutoff)
            {
                var targetOthers = math.saturate(1f - sumRecipe);
                var scale = targetOthers / originalOthersSum;
                if (scale < 1f - Epsilon)
                {
                    for (var i = 0; i < AlphamapLayerCount; i++)
                    {
                        if (IsRecipeSplatIndex(i)) continue;
                        var idx = baseAlphaIndex + i;
                        Alphamaps[idx] *= scale;
                    }
                }
            }

            // 第三步：写入配方层的贡献
            remaining = 1f;
            for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
            {
                var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (!ValidateSplatIndex(splatIndex)) continue;

                var maskAlpha = math.saturate(GetMaskValue(layerIndex, normalizedDist, pathProgress));
                if (OpaquePainting && maskAlpha < AlphaClipThreshold)
                {
                    // Alphamaps[baseAlphaIndex + splatIndex] = 0f;
                    continue;
                }

                var strength = Recipe.Opacities.IsCreated && layerIndex >= 0 && layerIndex < Recipe.Opacities.Length
                    ? math.saturate(Recipe.Opacities[layerIndex])
                    : 1f;
                var selfAlpha = math.saturate(maskAlpha * strength);
                var contribute = math.min(selfAlpha, remaining);
                if (contribute > SmallWeightCutoff)
                {
                    anyLayerPainted = true;
                    Alphamaps[baseAlphaIndex + splatIndex] = contribute;
                    remaining = math.max(0f, remaining - contribute);
                }
                else
                {
                    // 保留原始权重，不做清零以避免把已有底图层抹成 0 导致黑边
                    // no-op
                }

                if (remaining <= Epsilon) break;
            }

            return anyLayerPainted;
        }

        /// <summary>
        ///     验证splat索引是否有效
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateSplatIndex(int splatIndex) => splatIndex >= 0 && splatIndex < AlphamapLayerCount;

        /// <summary>
        ///     在道路覆盖区清除非配方图层的权重
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
        ///     获取遮罩值
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
            if (Recipe.Strips.IsCreated)
            {
                // 仅返回遮罩值（不含不透明度），在混合处再乘以 Recipe.Opacities
                return TerrainJobsUtility.EvaluateStrip(
                    Recipe.Strips, Recipe.StripSlices[layerIndex],
                    Recipe.StripResolution, normalizedDist);
            }
            // 无遮罩数据：返回 1（全通），最终在混合处乘以 Recipe.Opacities
            return 1f;
        }

        /// <summary>
        ///     应用混合到指定图层
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyBlendingToLayer(int baseAlphaIndex, int splatIndex, float maskValue, int layerIndex)
        {
            var alphaMapIndex = baseAlphaIndex + splatIndex;
            var currentValue = Alphamaps[alphaMapIndex];
            var blendedValue = TerrainJobsUtility.Blend(currentValue, maskValue, Recipe.BlendModes[layerIndex]);

            // 截断极小权重以避免基图层被清零后出现黑边
            if (blendedValue < SmallWeightCutoff) blendedValue = 0f;

            Alphamaps[alphaMapIndex] = blendedValue;
        }

        /// <summary>
        ///     未绘制任何图层时保持原权重不变
        /// </summary>

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleUnpaintedLayers(int baseAlphaIndex, bool anyLayerPainted, int firstValidSplatIndex)
        {
            const float NormalizationThreshold = 1e-5f;
            var total = 0f;
            var maxVal = -1f;
            var maxIdx = -1;

            for (var i = 0; i < AlphamapLayerCount; i++)
            {
                var v = Alphamaps[baseAlphaIndex + i];
                total += v;
                if (!(v > maxVal)) continue;
                maxVal = v;
                maxIdx = i;
            }

            if (!(total < NormalizationThreshold)) return;
            {
                // 若全为0，回退到本次计算得到的首个有效配方层；再不行则回退到0号图层
                if (maxIdx < 0) maxIdx = ValidateSplatIndex(firstValidSplatIndex) ? firstValidSplatIndex : 0;
                for (var i = 0; i < AlphamapLayerCount; i++)
                {
                    Alphamaps[baseAlphaIndex + i] = i == maxIdx ? 1f : 0f;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NormalizeAlphaWeights(int baseAlphaIndex, int firstValidSplatIndex)
        {
            TerrainJobsUtility.NormalizeWeightsKeep(Alphamaps, baseAlphaIndex, AlphamapLayerCount, firstValidSplatIndex);
        }

        #endregion
    }
}
