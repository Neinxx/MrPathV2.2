// 文件: __temp.MrPathV2._2.Runtime.Jobs.PaintSplatmapJob.cs

using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace __temp.MrPathV2.Runtime.Jobs
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

            // 根据开关：不透明时使用 alpha clip；否则保持透明混合
            var (anyLayerPainted, firstValidSplatIndex) = ApplyLayerBlending(baseAlphaIndex, normalizedDist, pathProgress);

            // 不透明绘制：当确实涂绘到图层时，将非配方层权重清零以避免与底层混合
            if (OpaquePainting && anyLayerPainted)
            {
                ZeroOutNonRecipeLayers(baseAlphaIndex);
            }

            // 若未涂绘任何图层，直接返回以避免不必要的标准化开销
            if (!anyLayerPainted) return;

            // 标准化alpha权重，保持总和为 1
            NormalizeAlphaWeights(baseAlphaIndex, firstValidSplatIndex);
        }

       /// <summary>
       /// 计算是否应当对该像素进行涂绘：总权重未达到门槛则视为未涂绘
       /// </summary>
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       private bool ShouldPaintPixel(float normalizedDist, float pathProgress, out int firstValidSplatIndex)
       {
           firstValidSplatIndex = -1;
           float totalMask = 0f;
           // 改为从上到下（索引高到低）遍历，以获取最上层有效索引
           for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
           {
               var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
               if (!ValidateSplatIndex(splatIndex)) continue;
               if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;
   
               var maskValue = GetMaskValue(layerIndex, normalizedDist, pathProgress);
               if (maskValue > SmallWeightCutoff)
                   totalMask += maskValue;
           }
           return totalMask > PaintGateThreshold;
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

            // 顺序不限：每个通道独立按当前alpha与遮罩alpha进行混合
            for (var layerIndex = 0; layerIndex < Recipe.Length; layerIndex++)
            {
                var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (!ValidateSplatIndex(splatIndex))
                    continue;

                if (firstValidSplatIndex == -1)
                    firstValidSplatIndex = splatIndex;

                var maskAlpha = math.saturate(GetMaskValue(layerIndex, normalizedDist, pathProgress));
                if (!OpaquePainting)
                {
                    // 透明混合：小权重直接忽略，使用统一 Blend 函数
                    if (maskAlpha <= SmallWeightCutoff)
                        continue;
                    anyLayerPainted = true;
                    var alphaMapIndex = baseAlphaIndex + splatIndex;
                    var baseAlpha = Alphamaps[alphaMapIndex];
                    var blended = TerrainJobsUtility.Blend(baseAlpha, maskAlpha, Recipe.BlendModes[layerIndex]);
                    // 截断极小权重，避免黑边
                    if (blended < SmallWeightCutoff) blended = 0f;
                    Alphamaps[alphaMapIndex] = blended;
                }
                else
                {
                    // 不透明绘制：应用 alpha clip，超过阈值直接写满（1），否则忽略
                    if (maskAlpha < AlphaClipThreshold)
                        continue;
                    anyLayerPainted = true;
                    var alphaMapIndex = baseAlphaIndex + splatIndex;
                    Alphamaps[alphaMapIndex] = 1f;
                }
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

            // 截断极小权重以避免基图层被清零后出现黑边
            if (blendedValue < SmallWeightCutoff) blendedValue = 0f;

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
