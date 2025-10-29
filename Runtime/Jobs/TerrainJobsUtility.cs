using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     Terrain 作业的通用辅助函数：曲线评估与轮廓点检测。
    ///     供 PaintSplatmapJob 与 ModifyAlphamapsJob 等复用，避免重复逻辑。
    ///     增强了边界条件验证和错误恢复机制。
    /// </summary>
    public static class TerrainJobsUtility
    {
        /// <summary>
        ///     Ray Casting 多边形包含检测，带包围盒提前剔除。
        ///     增强了边界条件验证和错误恢复。
        /// </summary>
        public static bool IsPointInContour(float2 p, float4 bounds, NativeArray<float2> contour)
        {
            // 验证输入参数
            if (!IsValidPoint(p))
            {
                return false; // 无效点直接返回false
            }

            // 处理边界无效的情况：尝试从轮廓重建边界并继续后续检查
            if (!IsValidBounds(bounds))
            {
                if (!HandleInvalidBounds(contour, ref bounds))
                {
                    return false; // 无法修复边界则判定为不在轮廓内
                }
                // 如果成功修复边界，不要提前返回，继续下面的 AABB 和轮廓检测
            }

            // 当多边形轮廓不可用（长度不足）时，退化为仅使用 AABB 粗裁剪。
            if (!contour.IsCreated || contour.Length < 3)
            {
                return IsPointInAABB(p, bounds);
            }

            // AABB 预检查
            if (!IsPointInAABB(p, bounds))
                return false;

            // 使用Ray casting算法检查点是否在轮廓内
            return PerformRayCasting(p, contour);
        }

        /// <summary>
        ///     处理边界无效的情况
        /// </summary>
        /// <param name="contour">轮廓点数组</param>
        /// <param name="bounds">边界框</param>
        /// <returns>点是否在轮廓内</returns>
        private static bool HandleInvalidBounds(NativeArray<float2> contour, ref float4 bounds)
        {
            // 如果边界无效，但轮廓有效，尝试从轮廓计算边界
            if (contour is { IsCreated: true, Length: >= 3 })
            {
                bounds = CalculateBoundsFromContour(contour);
                if (!IsValidBounds(bounds))
                {
                    return false; // 仍然无效则返回false
                }
            }
            else
            {
                return false;
            }
            
            return true;
        }

        /// <summary>
        ///     检查点是否在轴对齐边界框(AABB)内
        /// </summary>
        /// <param name="p">检查的点</param>
        /// <param name="bounds">边界框 (xmin, ymin, xmax, ymax)</param>
        /// <returns>点是否在边界框内</returns>
        private static bool IsPointInAABB(float2 p, float4 bounds)
        {
            return !(p.x < bounds.x || p.y < bounds.y || p.x > bounds.z || p.y > bounds.w);
        }

        /// <summary>
        ///     使用Ray casting算法检查点是否在轮廓内
        /// </summary>
        /// <param name="p">检查的点</param>
        /// <param name="contour">轮廓点数组</param>
        /// <returns>点是否在轮廓内</returns>
        private static bool PerformRayCasting(float2 p, NativeArray<float2> contour)
        {
            var inside = false;
            var n = contour.Length;
        
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = contour[i];
                var pj = contour[j];
        
                // 验证轮廓点的有效性
                if (!IsValidPoint(pi) || !IsValidPoint(pj))
                {
                    continue; // 跳过无效的轮廓点
                }
        
                // 增强数值稳定性的交点检测
                if (!CheckIntersection(p, pi, pj))
                {
                    continue;
                }
        
                // 计算交点X坐标
                var intersectionX = CalculateIntersectionX(p, pi, pj);
                
                // 更新内部状态
                if (p.x < intersectionX)
                {
                    inside = !inside;
                }
            }
            
            return inside;
        }

        /// <summary>
        /// 检查点与线段是否相交
        /// </summary>
        /// <param name="p">检查的点</param>
        /// <param name="pi">线段起点</param>
        /// <param name="pj">线段终点</param>
        /// <returns>是否相交</returns>
        private static bool CheckIntersection(float2 p, float2 pi, float2 pj)
        {
            var intersect = pi.y > p.y != pj.y > p.y;
            if (!intersect)
            {
                return false;
            }
        
            var denominator = pj.y - pi.y;
            // 避免除零，使用更大的epsilon值提高稳定性
            return math.abs(denominator) > 1e-6f;
        }

        /// <summary>
        /// 计算交点的X坐标
        /// </summary>
        /// <param name="p">检查的点</param>
        /// <param name="pi">线段起点</param>
        /// <param name="pj">线段终点</param>
        /// <returns>交点的X坐标</returns>
        private static float CalculateIntersectionX(float2 p, float2 pi, float2 pj)
        {
            var denominator = pj.y - pi.y;
            return (pj.x - pi.x) * (p.y - pi.y) / denominator + pi.x;
        }

        /// <summary>
        ///     验证点是否有效（不包含NaN或无穷大）
        /// </summary>
        private static bool IsValidPoint(float2 point) => !math.isnan(point.x) && !math.isnan(point.y) &&
                                                          !math.isinf(point.x) && !math.isinf(point.y);

        /// <summary>
        ///     验证边界是否有效
        /// </summary>
        private static bool IsValidBounds(float4 bounds) => !math.isnan(bounds.x) && !math.isnan(bounds.y) &&
                                                            !math.isnan(bounds.z) && !math.isnan(bounds.w) &&
                                                            !math.isinf(bounds.x) && !math.isinf(bounds.y) &&
                                                            !math.isinf(bounds.z) && !math.isinf(bounds.w) &&
                                                            bounds.x <= bounds.z && bounds.y <= bounds.w; // min <= max

        /// <summary>
        ///     从轮廓点计算边界框
        /// </summary>
        private static float4 CalculateBoundsFromContour(NativeArray<float2> contour)
        {
            if (!contour.IsCreated || contour.Length == 0)
            {
                return new float4(0, 0, 0, 0);
            }

            var min = new float2(float.MaxValue, float.MaxValue);
            var max = new float2(float.MinValue, float.MinValue);

            foreach (var point in contour)
            {
                if (IsValidPoint(point))
                {
                    min = math.min(min, point);
                    max = math.max(max, point);
                }
            }

            // 如果没有找到有效点，返回零边界
            if (Mathf.Approximately(min.x, float.MaxValue) || Mathf.Approximately(min.y, float.MaxValue))
            {
                return new float4(0, 0, 0, 0);
            }

            return new float4(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        ///     从采样条读取遮罩值：normalizedDist(0..1) 映射到条带索引。
        ///     增强了边界条件验证和错误恢复。
        /// </summary>
        public static float EvaluateStrip(NativeArray<float> strips, int2 slice, int stripResolution, float normalizedDist)
        {
            // 验证输入参数
            if (!ValidateInputParameters(strips, slice, stripResolution))
            {
                return 0f; // 参数无效，返回默认值
            }
        
            // 处理无效的归一化距离
            normalizedDist = HandleInvalidNormalizedDistance(normalizedDist);
        
            // 计算实际参数
            var (start, _, actualCount, effectiveResolution) = CalculateActualParameters(slice, stripResolution, strips.Length);
        
            // 检查是否需要返回默认值
            if (ShouldReturnDefaultValue(actualCount, effectiveResolution))
            {
                return GetDefaultValue(strips, start);
            }
        
            // 计算插值参数
            var (idxA, idxB, w) = CalculateInterpolationParameters(normalizedDist, effectiveResolution, actualCount);
        
            // 获取采样值
            var (a, b) = GetSampleValues(strips, start, idxA, idxB);
        
            // 执行插值并返回结果
            return PerformInterpolation(a, b, w);
        }
        
        /// <summary>
        ///     验证输入参数的有效性
        ///     </summary>
        private static bool ValidateInputParameters(NativeArray<float> strips, int2 slice, int stripResolution)
        {
            return strips.IsCreated && stripResolution > 1 && slice is { y: > 0, x: >= 0 } && slice.x < strips.Length;
        }
        
        /// <summary>
        ///     处理无效的归一化距离值
        ///     </summary>
        private static float HandleInvalidNormalizedDistance(float normalizedDist)
        {
            return math.isnan(normalizedDist) || math.isinf(normalizedDist) ? 0f : normalizedDist;
        }
        
        /// <summary>
        ///     计算实际参数
        ///     </summary>
        private static (int start, int count, int actualCount, int effectiveResolution) CalculateActualParameters(int2 slice, int stripResolution, int stripsLength)
        {
            var start = slice.x;
            var count = slice.y;
            var actualCount = math.min(count, stripsLength - start);
            var effectiveResolution = math.min(stripResolution, actualCount);
            
            return (start, count, actualCount, effectiveResolution);
        }
        
        /// <summary>
        ///     检查是否应该返回默认值
        ///     </summary>
        private static bool ShouldReturnDefaultValue(int actualCount, int effectiveResolution)
        {
            return actualCount <= 0 || effectiveResolution <= 1;
        }
        
        /// <summary>
        ///     获取默认值
        ///     </summary>
        private static float GetDefaultValue(NativeArray<float> strips, int start)
        {
            var value = strips[start];
            return math.isnan(value) || math.isinf(value) ? 0f : value;
        }
        
        /// <summary>
        ///     计算插值参数
        ///     </summary>
        private static (int idxA, int idxB, float w) CalculateInterpolationParameters(float normalizedDist, int effectiveResolution, int actualCount)
        {
            // 归一化到 [0, effectiveResolution-1]
            var fIndex = math.saturate(normalizedDist) * (effectiveResolution - 1);
            var idxA = math.clamp((int)math.floor(fIndex), 0, effectiveResolution - 1);
            var idxB = math.clamp(idxA + 1, 0, effectiveResolution - 1);
        
            // 确保索引不会越界
            idxA = math.min(idxA, actualCount - 1);
            idxB = math.min(idxB, actualCount - 1);
        
            var w = fIndex - idxA;
            w = math.saturate(w); // 确保权重在有效范围内
            
            return (idxA, idxB, w);
        }
        
        /// <summary>
        ///     获取采样值
        ///     </summary>
        private static (float a, float b) GetSampleValues(NativeArray<float> strips, int start, int idxA, int idxB)
        {
            var a = strips[start + idxA];
            var b = strips[start + idxB];
        
            // 验证采样值的有效性
            if (math.isnan(a) || math.isinf(a)) a = 0f;
            if (math.isnan(b) || math.isinf(b)) b = 0f;
            
            return (a, b);
        }
        
        /// <summary>
        ///     执行插值计算
        ///     </summary>
        private static float PerformInterpolation(float a, float b, float w)
        {
            return math.lerp(a, b, w);
        }

        /// <summary>
        ///     共享的灰度 Blend 算法，匹配编辑器预览与 GPU 权重库实现。
        ///     BlendMode 枚举序数：需与 Runtime/Core/PathTool.Data.cs 保持一致。
        /// </summary>
        public static float Blend(float baseValue, float layerValue, int blendModeOrdinal)
        {
            // 规范化输入
            if (math.isnan(baseValue) || math.isinf(baseValue)) baseValue = 0f;
            if (math.isnan(layerValue) || math.isinf(layerValue)) layerValue = 0f;
            baseValue = math.clamp(baseValue, 0f, 1f);
            layerValue = math.clamp(layerValue, 0f, 1f);

            switch (blendModeOrdinal)
            {
                case 1: // Multiply
                    return baseValue * layerValue;
                case 2: // Add
                case 6: // Additive
                    return math.saturate(baseValue + layerValue);
                case 3: // Overlay (float variant)
                    return baseValue < 0.5f
                        ? 2f * baseValue * layerValue
                        : 1f - 2f * (1f - baseValue) * (1f - layerValue);
                case 4: // Screen (float variant)
                    return 1f - (1f - baseValue) * (1f - layerValue);
                case 5: // Lerp (use layer as alpha)
                    return math.lerp(baseValue, layerValue, math.saturate(layerValue));
                default: // Normal (override)
                    return layerValue;
            }
        }

        /// <summary>
        ///     在 Job 中从 2D MaskAtlas 采样遮罩值。
        ///     atlas: 行优先存储，行数 = layerCount * pathSamples，列数 = atlasWidth。
        ///     layerIndex: 遮罩层索引 (0-based)
        ///     normalizedDist: 0..1 横向坐标 (左0, 右1，非对称)
        ///     pathProgress: 0..1 沿路径的进度 (0=起点,1=终点)
        ///     pathSamples: atlas 中纵向采样行数。
        ///     </summary>
        public static float SampleMaskAtlas(NativeArray<float> atlas, int atlasWidth, int pathSamples, int layerIndex, float normalizedDist, float pathProgress)
        {
            // 验证输入参数
            if (!ValidateAtlasParameters(atlas, atlasWidth, pathSamples))
                return 0f;
        
            // 处理无效的输入值
            normalizedDist = HandleInvalidValue(normalizedDist, 0f);
            pathProgress = HandleInvalidValue(pathProgress, 0.5f);
        
            // 计算图层行起始位置
            var layerRowStart = layerIndex * pathSamples;
            var atlasHeight = atlas.Length / atlasWidth;
            
            // 验证图层索引
            if (!ValidateLayerIndex(layerRowStart, atlasHeight))
                return 0f;
        
            // 计算双线性插值坐标
            var (maxX, maxY) = CalculateMaxCoordinates(atlasWidth, pathSamples);
            var (_, xA, xB, wx) = CalculateXCoordinates(normalizedDist, maxX);
            var (_, yA, yB, wy) = CalculateYCoordinates(pathProgress, maxY);
        
            // 获取四个邻居值
            var (v00, v10, v01, v11) = GetNeighborValues(atlas, atlasWidth, layerRowStart, xA, xB, yA, yB);
        
            // 处理无效值
            v00 = HandleInvalidValue(v00, 0f);
            v10 = HandleInvalidValue(v10, 0f);
            v01 = HandleInvalidValue(v01, 0f);
            v11 = HandleInvalidValue(v11, 0f);
        
            // 执行双线性插值并返回结果
            return PerformBilinearInterpolation(v00, v10, v01, v11, wx, wy);
        }
        
        /// <summary>
        ///     验证Atlas参数的有效性
        ///     </summary>
        private static bool ValidateAtlasParameters(NativeArray<float> atlas, int atlasWidth, int pathSamples)
        {
            return atlas is { IsCreated: true, Length: > 0 } && atlasWidth > 0 && pathSamples > 0;
        }
        
        /// <summary>
        ///     处理无效值
        ///     </summary>
        private static float HandleInvalidValue(float value, float defaultValue)
        {
            return math.isnan(value) || math.isinf(value) ? defaultValue : value;
        }
        
        /// <summary>
        ///     验证图层索引的有效性
        ///     </summary>
        private static bool ValidateLayerIndex(int layerRowStart, int atlasHeight)
        {
            return layerRowStart >= 0 && layerRowStart < atlasHeight;
        }
        
        /// <summary>
        ///     计算最大坐标值
        ///     </summary>
        private static (int maxX, int maxY) CalculateMaxCoordinates(int atlasWidth, int pathSamples)
        {
            return (atlasWidth - 1, pathSamples - 1);
        }
        
        /// <summary>
        ///     计算X轴坐标参数
        ///     </summary>
        private static (float fX, int xA, int xB, float wx) CalculateXCoordinates(float normalizedDist, int maxX)
        {
            var fX = math.saturate(normalizedDist) * maxX;
            var xA = (int)math.floor(fX);
            var xB = math.min(xA + 1, maxX);
            var wx = fX - xA;
            
            return (fX, xA, xB, wx);
        }
        
        /// <summary>
        ///     计算Y轴坐标参数
        ///     </summary>
        private static (float fY, int yA, int yB, float wy) CalculateYCoordinates(float pathProgress, int maxY)
        {
            var fY = math.saturate(pathProgress) * maxY;
            var yA = (int)math.floor(fY);
            var yB = math.min(yA + 1, maxY);
            var wy = fY - yA;
            
            return (fY, yA, yB, wy);
        }
        
        /// <summary>
        ///     获取四个邻居值
        ///     </summary>
        private static (float v00, float v10, float v01, float v11) GetNeighborValues(
            NativeArray<float> atlas, int atlasWidth, int layerRowStart, int xA, int xB, int yA, int yB)
        {
            var rowAOffset = (layerRowStart + yA) * atlasWidth;
            var rowBOffset = (layerRowStart + yB) * atlasWidth;
        
            var v00 = atlas[rowAOffset + xA];
            var v10 = atlas[rowAOffset + xB];
            var v01 = atlas[rowBOffset + xA];
            var v11 = atlas[rowBOffset + xB];
            
            return (v00, v10, v01, v11);
        }
        
        /// <summary>
        ///     执行双线性插值
        ///     </summary>
        private static float PerformBilinearInterpolation(float v00, float v10, float v01, float v11, float wx, float wy)
        {
            var v0 = math.lerp(v00, v10, wx);
            var v1 = math.lerp(v01, v11, wx);
            var v = math.lerp(v0, v1, wy);
            return v;
        }

        public static void NormalizeWeightsKeep(NativeArray<float> alphamaps, int baseIndex, int layerCount, int firstValidSplatIndex)
        {
            var paintedCount = 0;
            var total = 0f;
            for (var i = 0; i < layerCount; i++)
            {
                var v = alphamaps[baseIndex + i];
                total += v;
                if (v > 1e-4f) paintedCount++;
            }

            switch (paintedCount)
            {
                case > 1 when total > 1e-5f:
                {
                    var invTotal = 1f / total;
                    for (var i = 0; i < layerCount; i++)
                    {
                        alphamaps[baseIndex + i] *= invTotal;
                    }
                    break;
                }
                case 0 when firstValidSplatIndex >= 0:
                {
                    for (var i = 0; i < layerCount; i++)
                    {
                        alphamaps[baseIndex + i] = i == firstValidSplatIndex ? 1f : 0f;
                    }
                    break;
                }
            }
        }

       
    }
}
