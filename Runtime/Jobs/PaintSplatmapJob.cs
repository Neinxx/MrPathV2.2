using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    /// 高性能地形纹理绘制作业 - 优化版
    /// 使用Burst编译和优化的算法提供最佳性能
    /// 支持覆盖区域限制以提升性能
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct PaintSplatmapJob : IJobParallelFor
    {
        #region 只读数据

        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] public RecipeData Recipe;
        [ReadOnly] public float3 TerrainPos;
        [ReadOnly] public float3 TerrainSize;
        [ReadOnly] public int AlphamapResolution;
        [ReadOnly] public int AlphamapLayerCount;
        [ReadOnly] public NativeArray<float2> RoadContour;
        [ReadOnly] public float4 ContourBounds;
        
        // 新增：覆盖区域限制
        [ReadOnly] public bool UseCoverageLimit;
        [ReadOnly] public int2 CoverageMin; // 像素坐标范围最小值 (x, y)
        [ReadOnly] public int2 CoverageMax; // 像素坐标范围最大值 (x, y)

        #endregion

        #region 可写数据

        /// <summary>
        /// Alpha贴图数据，每个并行索引写入其对应的整段图层范围
        /// 长度 = alphamapResolution * alphamapResolution * alphamapLayerCount
        /// </summary>
        [NativeDisableParallelForRestriction]
        public NativeArray<float> Alphamaps;

        #endregion

        #region 常量定义

        private const float Epsilon = 1e-6f;
        private const float NormalizationThreshold = 1e-5f;

        #endregion

        #region 核心执行方法

        /// <summary>
        /// 并行执行入口：处理单个像素行
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int y)
        {
            // 覆盖区域剔除：检查Y坐标是否在范围内
            if (UseCoverageLimit && (y < CoverageMin.y || y > CoverageMax.y))
                return;

            // 快速轮廓剔除检查
            if (!IsRowInContourBounds(y)) return;

            // 处理该行的所有像素
            for (int x = 0; x < AlphamapResolution; x++)
            {
                // 覆盖区域剔除：检查X坐标是否在范围内
                if (UseCoverageLimit && (x < CoverageMin.x || x > CoverageMax.x))
                    continue;

                ProcessPixel(x, y);
            }
        }

        #endregion

        #region 私有优化方法

        /// <summary>
        /// 检查行是否与轮廓边界相交
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRowInContourBounds(int y)
        {
            float invResolution = 1f / (AlphamapResolution - 1);
            float worldZ = TerrainPos.z + y * invResolution * TerrainSize.z;
            
            // 检查是否在轮廓边界内
            return worldZ >= ContourBounds.y && worldZ <= ContourBounds.w;
        }

        /// <summary>
        /// 处理单个像素
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPixel(int x, int y)
        {
            int index = y * AlphamapResolution + x;
            
            // 快速轮廓裁剪检查
            if (!IsPixelInRoadContour(x, y, out float2 worldPos2D))
                return;

            // 计算相对脊线的横向位置
            if (!CalculateDistanceFromSpine(worldPos2D, out float normalizedDist))
                return;

            // 应用纹理混合
            ApplyTextureBlending(index, normalizedDist);
        }

        /// <summary>
        /// 快速检查像素是否在道路轮廓内（优化版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPixelInRoadContour(int x, int y, out float2 worldPos2D)
        {
            // 计算世界坐标（优化：避免重复计算）
            float invResolution = 1f / (AlphamapResolution - 1);
            worldPos2D = new float2(
                TerrainPos.x + x * invResolution * TerrainSize.x,
                TerrainPos.z + y * invResolution * TerrainSize.z
            );

            return TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour);
        }

/*
        /// <summary>
        /// 快速检查像素是否在道路轮廓内（兼容旧版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPixelInRoadContour(int index, out float2 worldPos2D)
        {
            // 计算世界坐标（优化：避免重复计算）
            int x = index % AlphamapResolution;
            int y = index / AlphamapResolution;
            
            return IsPixelInRoadContour(x, y, out worldPos2D);
        }
*/

        /// <summary>
        /// 计算点到脊线的标准化距离
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CalculateDistanceFromSpine(float2 worldPos2D, out float normalizedDist)
        {
            normalizedDist = 0f;
            
            // 查找最近的脊线段（优化：减少重复计算）
            float minDistanceSq = float.MaxValue;
            int closestSegmentIndex = -1;
            float tClosest = 0f;

            int spineSegmentCount = Spine.Points.Length - 1;
            for (int i = 0; i < spineSegmentCount; i++)
            {
                float2 segmentStart = Spine.Points[i].xz;
                float2 segmentEnd = Spine.Points[i + 1].xz;
                
                // 计算投影参数
                float2 segmentVector = segmentEnd - segmentStart;
                float2 pointVector = worldPos2D - segmentStart;
                
                float segmentLengthSq = math.dot(segmentVector, segmentVector);
                if (segmentLengthSq < Epsilon) continue;
                
                float t = math.saturate(math.dot(pointVector, segmentVector) / segmentLengthSq);
                float2 closestPoint = segmentStart + t * segmentVector;
                
                float distanceSq = math.distancesq(worldPos2D, closestPoint);
                if (distanceSq < minDistanceSq)
                {
                    minDistanceSq = distanceSq;
                    closestSegmentIndex = i;
                    tClosest = t;
                }
            }

            if (closestSegmentIndex == -1)
                return false;

            // 计算脊线上的插值点和方向向量
            float3 spinePoint = math.lerp(Spine.Points[closestSegmentIndex], Spine.Points[closestSegmentIndex + 1], tClosest);
            float3 spineNormal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex], Spine.Normals[closestSegmentIndex + 1], tClosest));
            float3 spineTangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex], Spine.Tangents[closestSegmentIndex + 1], tClosest));
            
            // 计算右向量
            float3 rightVector = math.normalize(math.cross(
                Profile.ForceHorizontal ? new float3(0, 1, 0) : spineNormal, 
                spineTangent
            ));

            // 计算标准化距离（对称，确保道路两侧一致性）
            float halfRoadWidth = Profile.RoadWidth * 0.5f;
            float2 offsetVector = new float2(worldPos2D.x - spinePoint.x, worldPos2D.y - spinePoint.z);
            float signedDistance = math.dot(offsetVector, rightVector.xz);
            
            normalizedDist = math.saturate(math.abs(signedDistance) / halfRoadWidth);
            return true;
        }

        /// <summary>
        /// 应用纹理混合算法
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTextureBlending(int pixelIndex, float normalizedDist)
        {
            int baseAlphaIndex = pixelIndex * AlphamapLayerCount;
            
            // 边界检查
            if (baseAlphaIndex < 0 || baseAlphaIndex + AlphamapLayerCount > Alphamaps.Length)
                return;

            // 先清零该像素全部图层权重，避免与原地形混合导致显色过淡
            for(int l=0;l<AlphamapLayerCount;l++)
            {
                Alphamaps[baseAlphaIndex + l] = 0f;
            }

            // 应用配方层混合
            bool anyLayerPainted = false;
            int firstValidSplatIndex = -1;
            
            for (int layerIndex = 0; layerIndex < Recipe.Length; layerIndex++)
            {
                int splatIndex = Recipe.TerrainLayerIndices[layerIndex];
                if (splatIndex < 0 || splatIndex >= AlphamapLayerCount) 
                    continue;
                
                if (firstValidSplatIndex == -1)
                    firstValidSplatIndex = splatIndex;

                // 计算遮罩值
                float maskValue;
                if (Recipe.MaskAtlas.IsCreated)
                {
                    maskValue = TerrainJobsUtility.SampleMaskAtlas(Recipe.MaskAtlas, Recipe.AtlasWidth, layerIndex, normalizedDist);
                }
                else
                {
                    maskValue = TerrainJobsUtility.EvaluateStrip(
                        Recipe.Strips,
                        Recipe.StripSlices[layerIndex],
                        Recipe.StripResolution,
                        normalizedDist);
                }

                if (maskValue > Epsilon)
                    anyLayerPainted = true;

                // 应用混合模式
                int alphaMapIndex = baseAlphaIndex + splatIndex;
                float currentValue = Alphamaps[alphaMapIndex];
                float blendedValue = TerrainJobsUtility.Blend(currentValue, maskValue, Recipe.BlendModes[layerIndex]);
                
                Alphamaps[alphaMapIndex] = blendedValue;
            }

            // 保底处理：如果所有层权重为0，设置首个有效层为1
            if (!anyLayerPainted && firstValidSplatIndex >= 0)
            {
                Alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }

            // 归一化处理
            NormalizeAlphaWeights(baseAlphaIndex, firstValidSplatIndex);
        }

        /// <summary>
        /// 归一化Alpha权重，确保总和为1
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NormalizeAlphaWeights(int baseAlphaIndex, int firstValidSplatIndex)
        {
            // 仅当绘制了 2 个及以上图层时才进行归一化，
            // 避免单图层被强行拉升到 1 失去遮罩梯度。
            int paintedCount = 0;
            float totalWeight = 0f;
            for (int i = 0; i < AlphamapLayerCount; i++)
            {
                float v = Alphamaps[baseAlphaIndex + i];
                totalWeight += v;
                if (v > 1e-4f) paintedCount++;
            }

            if (paintedCount > 1 && totalWeight > NormalizationThreshold)
            {
                float invTotalWeight = 1f / totalWeight;
                for (int i = 0; i < AlphamapLayerCount; i++)
                {
                    Alphamaps[baseAlphaIndex + i] *= invTotalWeight;
                }
            }
            else if (paintedCount == 0 && firstValidSplatIndex >= 0)
            {
                // 如果未命中任何图层，设置首个有效层为1
                for (int i = 0; i < AlphamapLayerCount; i++)
                {
                    Alphamaps[baseAlphaIndex + i] = (i == firstValidSplatIndex) ? 1f : 0f;
                }
            }
        }

        #endregion
    }
}