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
        private bool ApplyLayerBlending(int baseAlphaIndex, float normalizedDist, float pathProgress, int firstValidSplatIndex)
        {
            var anyLayerPainted = false;

            if (!OpaquePainting)
            {
                // 有序覆盖（Ordered Over）：
                // 1) 自身透明度 = 遮罩透明度（已含 layer opacity/预乘）
                // 2) 从“下到上”（更靠下的层优先，占据剩余空间）
                // 3) 其余层按剩余量缩放（保持原有相对比例），最后归一化

                // 预先累计“其他层”的原始总和（非配方层与配方层的旧值）
                float originalOthersSum = 0f;
                for (var i = 0; i < AlphamapLayerCount; i++)
                {
                    if (!IsRecipeSplatIndex(i))
                        originalOthersSum += Alphamaps[baseAlphaIndex + i];
                }

                // 1) 从下到上分配“剩余”
                var remaining = 1f;
                float sumRecipe = 0f;

                for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
                {
                    var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                    if (!ValidateSplatIndex(splatIndex))
                        continue;

                    var maskAlpha = math.saturate(GetMaskValue(layerIndex, normalizedDist, pathProgress));
                    if (maskAlpha <= SmallWeightCutoff)
                    {
                        // 完全被覆盖或自身很小，直接写 0 以避免残留
                        Alphamaps[baseAlphaIndex + splatIndex] = 0f;
                        continue;
                    }

                    var selfAlpha = maskAlpha; // 自透明度（已考虑 mask/opacity）
                    var contribute = math.min(selfAlpha, remaining);
                    if (contribute > SmallWeightCutoff)
                    {
                        anyLayerPainted = true;
                        Alphamaps[baseAlphaIndex + splatIndex] = contribute;
                        sumRecipe += contribute;
                        remaining = math.max(0f, remaining - contribute);
                    }
                    else
                    {
                        Alphamaps[baseAlphaIndex + splatIndex] = 0f;
                    }
                }

                // 2) 缩放非配方层，使其总和按剩余量衰减（保持相对比例），避免突兀断层
                if (originalOthersSum > SmallWeightCutoff)
                {
                    var scale = math.saturate(1f - sumRecipe);
                    if (scale < 1f - Epsilon)
                    {
                        for (var i = 0; i < AlphamapLayerCount; i++)
                        {
                            if (!IsRecipeSplatIndex(i))
                            {
                                var idx = baseAlphaIndex + i;
                                Alphamaps[idx] *= scale;
                            }
                        }
                    }
                }
            }
            else
            {
                // 不透明绘制：有序 clip，最靠下的层优先吃满
                var remaining = 1f;
                for (var layerIndex = Recipe.Length - 1; layerIndex >= 0; layerIndex--)
                {
                    var splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                    if (!ValidateSplatIndex(splatIndex)) continue;

                    var maskAlpha = math.saturate(GetMaskValue(layerIndex, normalizedDist, pathProgress));
                    if (maskAlpha < AlphaClipThreshold)
                    {
                        Alphamaps[baseAlphaIndex + splatIndex] = 0f;
                        continue;
                    }

                    var contribute = remaining; // clip 模式，达阈值即吃掉所有剩余
                    if (contribute > SmallWeightCutoff)
                    {
                        anyLayerPainted = true;
                        Alphamaps[baseAlphaIndex + splatIndex] = contribute;
                        remaining = 0f;
                    }
                    else
                    {
                        Alphamaps[baseAlphaIndex + splatIndex] = 0f;
                    }

                    if (remaining <= Epsilon) break;
                }
            }

            return anyLayerPainted;
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
                // 没有任何遮罩数据（既无 MaskAtlas 也无 Strips）时：
                // 约定“空遮罩 = 全通道”，且需与其它路径保持一致（在构建阶段已将遮罩值预乘了opacity）。
                // 因此这里直接返回该图层的不透明度，等价于“显示该图层，除非显式降低其透明度”。
                return (Recipe.Opacities.IsCreated && layerIndex >= 0 && layerIndex < Recipe.Opacities.Length)
                    ? Recipe.Opacities[layerIndex]
                    : 1f;
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
