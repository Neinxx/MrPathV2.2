// 文件: __temp.MrPathV2._2.Runtime.Jobs.PaintSplatmapJob.cs
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    /// 两阶段地形绘制的第二阶段：读取缓存信息并执行混合。
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
        /// 应用纹理混合算法 (使用缓存的 Dist 和 Progress)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTextureBlending(int pixelIndex, float normalizedDist, float pathProgress)
        {
            var baseAlphaIndex = pixelIndex * AlphamapLayerCount;
            
            if (baseAlphaIndex < 0 || baseAlphaIndex + AlphamapLayerCount > Alphamaps.Length)
                return;

            var anyPainted = false;
            var firstValidSplatIndex = -1;
            
            for (var i = 0; i < Recipe.Length; i++)
            {
                var splatIndex = Recipe.TerrainLayerIndices[i];
                if (splatIndex < 0 || splatIndex >= AlphamapLayerCount) continue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;

                float maskValue;
                if (Recipe.MaskAtlas.IsCreated)
                {
                    maskValue = TerrainJobsUtility.SampleMaskAtlas(
                        Recipe.MaskAtlas, Recipe.AtlasWidth,  Recipe.PathSamples,
                        i, normalizedDist, pathProgress, Recipe.MaskThreshold);
                }
                else if (Recipe.Strips.IsCreated)
                {
                     maskValue = TerrainJobsUtility.EvaluateStrip(
                        Recipe.Strips, Recipe.StripSlices[i],
                        Recipe.StripResolution, normalizedDist);
                }
                else
                {
                    maskValue = 1f;
                }

                if (maskValue > Epsilon)
                {
                    anyPainted = true;
                    var alphaMapIndex = baseAlphaIndex + splatIndex;
                    var currentValue = Alphamaps[alphaMapIndex];
                    var mode = Recipe.BlendModes[i];
                    var blended = TerrainJobsUtility.Blend(currentValue, maskValue * Recipe.Opacities[i], mode);
                    Alphamaps[alphaMapIndex] = blended;
                }
            }

            // Normalize weights to keep sum=1
            NormalizeAlphaWeights(baseAlphaIndex, firstValidSplatIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NormalizeAlphaWeights(int baseAlphaIndex, int firstValidSplatIndex)
        {
            TerrainJobsUtility.NormalizeWeightsKeep(Alphamaps, baseAlphaIndex, AlphamapLayerCount, firstValidSplatIndex);
        }

        #endregion
    }
}