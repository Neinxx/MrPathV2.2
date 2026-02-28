using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace MrPathV2
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

        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;
        [ReadOnly] public RecipeData recipe;
        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int alphamapResolution;
        [ReadOnly] public int alphamapLayerCount;
        [ReadOnly] public NativeArray<float2> roadContour;
        [ReadOnly] public float4 contourBounds;
        
        // 新增：覆盖区域限制
        [ReadOnly] public bool useCoverageLimit;
        [ReadOnly] public int2 coverageMin; // 像素坐标范围最小值 (x, y)
        [ReadOnly] public int2 coverageMax; // 像素坐标范围最大值 (x, y)

        #endregion

        #region 可写数据

        /// <summary>
        /// Alpha贴图数据，每个并行索引写入其对应的整段图层范围
        /// 长度 = alphamapResolution * alphamapResolution * alphamapLayerCount
        /// </summary>
        [NativeDisableParallelForRestriction]
        public NativeArray<float> alphamaps;

        #endregion

        #region 常量定义

        private const float EPSILON = 1e-6f;
        private const float SAFE_DIVISION_EPSILON = 1e-8f;
        private const float NORMALIZATION_THRESHOLD = 1e-5f;

        #endregion

        #region 核心执行方法

        /// <summary>
        /// 并行执行入口：处理单个像素行
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int y)
        {
            // 覆盖区域剔除：检查Y坐标是否在范围内
            if (useCoverageLimit && (y < coverageMin.y || y > coverageMax.y))
                return;

            // 快速轮廓剔除检查
            if (!IsRowInContourBounds(y)) return;

            // 处理该行的所有像素
            for (int x = 0; x < alphamapResolution; x++)
            {
                // 覆盖区域剔除：检查X坐标是否在范围内
                if (useCoverageLimit && (x < coverageMin.x || x > coverageMax.x))
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
            float invResolution = 1f / (alphamapResolution - 1);
            float worldZ = terrainPos.z + y * invResolution * terrainSize.z;
            
            // 检查是否在轮廓边界内
            return worldZ >= contourBounds.y && worldZ <= contourBounds.w;
        }

        /// <summary>
        /// 处理单个像素
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPixel(int x, int y)
        {
            int index = y * alphamapResolution + x;
            
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
            float invResolution = 1f / (alphamapResolution - 1);
            worldPos2D = new float2(
                terrainPos.x + x * invResolution * terrainSize.x,
                terrainPos.z + y * invResolution * terrainSize.z
            );

            return TerrainJobsUtility.IsPointInContour(worldPos2D, contourBounds, roadContour);
        }

        /// <summary>
        /// 快速检查像素是否在道路轮廓内（兼容旧版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPixelInRoadContour(int index, out float2 worldPos2D)
        {
            // 计算世界坐标（优化：避免重复计算）
            int x = index % alphamapResolution;
            int y = index / alphamapResolution;
            
            return IsPixelInRoadContour(x, y, out worldPos2D);
        }

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

            int spineSegmentCount = spine.points.Length - 1;
            for (int i = 0; i < spineSegmentCount; i++)
            {
                float2 segmentStart = spine.points[i].xz;
                float2 segmentEnd = spine.points[i + 1].xz;
                
                // 计算投影参数
                float2 segmentVector = segmentEnd - segmentStart;
                float2 pointVector = worldPos2D - segmentStart;
                
                float segmentLengthSq = math.dot(segmentVector, segmentVector);
                if (segmentLengthSq < EPSILON) continue;
                
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
            float3 spinePoint = math.lerp(spine.points[closestSegmentIndex], spine.points[closestSegmentIndex + 1], tClosest);
            float3 spineNormal = math.normalize(math.lerp(spine.normals[closestSegmentIndex], spine.normals[closestSegmentIndex + 1], tClosest));
            float3 spineTangent = math.normalize(math.lerp(spine.tangents[closestSegmentIndex], spine.tangents[closestSegmentIndex + 1], tClosest));
            
            // 计算右向量
            float3 rightVector = math.normalize(math.cross(
                profile.forceHorizontal ? new float3(0, 1, 0) : spineNormal, 
                spineTangent
            ));

            // 计算标准化距离（对称，确保道路两侧一致性）
            float halfRoadWidth = profile.roadWidth * 0.5f;
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
            int baseAlphaIndex = pixelIndex * alphamapLayerCount;
            
            // 边界检查
            if (baseAlphaIndex < 0 || baseAlphaIndex + alphamapLayerCount > alphamaps.Length)
                return;

            // 先清零该像素全部图层权重，避免与原地形混合导致显色过淡
            for(int l=0;l<alphamapLayerCount;l++)
            {
                alphamaps[baseAlphaIndex + l] = 0f;
            }

            // 应用配方层混合
            bool anyLayerPainted = false;
            int firstValidSplatIndex = -1;
            
            for (int layerIndex = 0; layerIndex < recipe.Length; layerIndex++)
            {
                int splatIndex = recipe.terrainLayerIndices[layerIndex];
                if (splatIndex < 0 || splatIndex >= alphamapLayerCount) 
                    continue;
                
                if (firstValidSplatIndex == -1)
                    firstValidSplatIndex = splatIndex;

                // 计算遮罩值
                float maskValue;
                if (recipe.maskLUT256.IsCreated)
                {
                    maskValue = TerrainJobsUtility.SampleMaskLUT(recipe.maskLUT256, layerIndex, normalizedDist);
                }
                else
                {
                    // 当 LUT 不可用时退化为 Strip 采样（已包含不透明度）
                    maskValue = TerrainJobsUtility.EvaluateStrip(
                        recipe.strips,
                        recipe.stripSlices[layerIndex],
                        recipe.stripResolution,
                        normalizedDist);
                }

                if (maskValue > EPSILON)
                    anyLayerPainted = true;

                // 应用混合模式
                int alphaMapIndex = baseAlphaIndex + splatIndex;
                float currentValue = alphamaps[alphaMapIndex];
                float blendedValue = TerrainJobsUtility.Blend(currentValue, maskValue, recipe.blendModes[layerIndex]);
                
                alphamaps[alphaMapIndex] = blendedValue;
            }

            // 保底处理：如果所有层权重为0，设置首个有效层为1
            if (!anyLayerPainted && firstValidSplatIndex >= 0)
            {
                alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
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
            for (int i = 0; i < alphamapLayerCount; i++)
            {
                float v = alphamaps[baseAlphaIndex + i];
                totalWeight += v;
                if (v > 1e-4f) paintedCount++;
            }

            if (paintedCount > 1 && totalWeight > NORMALIZATION_THRESHOLD)
            {
                float invTotalWeight = 1f / totalWeight;
                for (int i = 0; i < alphamapLayerCount; i++)
                {
                    alphamaps[baseAlphaIndex + i] *= invTotalWeight;
                }
            }
            else if (paintedCount == 0 && firstValidSplatIndex >= 0)
            {
                // 如果未命中任何图层，设置首个有效层为1
                for (int i = 0; i < alphamapLayerCount; i++)
                {
                    alphamaps[baseAlphaIndex + i] = (i == firstValidSplatIndex) ? 1f : 0f;
                }
            }
        }

        #endregion
    }
}