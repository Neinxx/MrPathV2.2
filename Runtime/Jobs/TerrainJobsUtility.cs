using Unity.Collections;
using Unity.Mathematics;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    /// Terrain 作业的通用辅助函数：曲线评估与轮廓点检测。
    /// 供 PaintSplatmapJob 与 ModifyAlphamapsJob 等复用，避免重复逻辑。
    /// 增强了边界条件验证和错误恢复机制。
    /// </summary>
    public static class TerrainJobsUtility
    {
        /// <summary>
        /// Ray Casting 多边形包含检测，带包围盒提前剔除。
        /// 增强了边界条件验证和错误恢复。
        /// </summary>
        public static bool IsPointInContour(float2 p, float4 bounds, NativeArray<float2> contour)
        {
            // 验证输入参数
            if (!IsValidPoint(p))
            {
                return false; // 无效点直接返回false
            }

            if (!IsValidBounds(bounds))
            {
                // 如果边界无效，但轮廓有效，尝试从轮廓计算边界
                if (contour.IsCreated && contour.Length >= 3)
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
            }

            // 当多边形轮廓不可用（长度不足）时，退化为仅使用 AABB 粗裁剪。
            if (!contour.IsCreated || contour.Length < 3)
            {
                return !(p.x < bounds.x || p.y < bounds.y || p.x > bounds.z || p.y > bounds.w);
            }

            // AABB 预检查
            if (p.x < bounds.x || p.y < bounds.y || p.x > bounds.z || p.y > bounds.w) 
                return false;

            // Ray casting算法，增加数值稳定性
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
                var intersect = ((pi.y > p.y) != (pj.y > p.y));
                if (intersect)
                {
                    var denominator = pj.y - pi.y;
                    // 避免除零，使用更大的epsilon值提高稳定性
                    if (math.abs(denominator) > 1e-6f)
                    {
                        var intersectionX = (pj.x - pi.x) * (p.y - pi.y) / denominator + pi.x;
                        if (p.x < intersectionX)
                        {
                            inside = !inside;
                        }
                    }
                }
            }
            return inside;
        }

        /// <summary>
        /// 验证点是否有效（不包含NaN或无穷大）
        /// </summary>
        private static bool IsValidPoint(float2 point)
        {
            return !math.isnan(point.x) && !math.isnan(point.y) && 
                   !math.isinf(point.x) && !math.isinf(point.y);
        }

        /// <summary>
        /// 验证边界是否有效
        /// </summary>
        private static bool IsValidBounds(float4 bounds)
        {
            return !math.isnan(bounds.x) && !math.isnan(bounds.y) && 
                   !math.isnan(bounds.z) && !math.isnan(bounds.w) &&
                   !math.isinf(bounds.x) && !math.isinf(bounds.y) && 
                   !math.isinf(bounds.z) && !math.isinf(bounds.w) &&
                   bounds.x <= bounds.z && bounds.y <= bounds.w; // min <= max
        }

        /// <summary>
        /// 从轮廓点计算边界框
        /// </summary>
        private static float4 CalculateBoundsFromContour(NativeArray<float2> contour)
        {
            if (!contour.IsCreated || contour.Length == 0)
            {
                return new float4(0, 0, 0, 0);
            }

            var min = new float2(float.MaxValue, float.MaxValue);
            var max = new float2(float.MinValue, float.MinValue);

            for (var i = 0; i < contour.Length; i++)
            {
                var point = contour[i];
                if (IsValidPoint(point))
                {
                    min = math.min(min, point);
                    max = math.max(max, point);
                }
            }

            // 如果没有找到有效点，返回零边界
            if (min.x == float.MaxValue || min.y == float.MaxValue)
            {
                return new float4(0, 0, 0, 0);
            }

            return new float4(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// 在 Job 中对合并后的关键帧数组进行安全线性评估。
        /// slice.x 为起始偏移，slice.y 为关键帧数量。
        /// 增强了边界条件验证和错误恢复。
        /// </summary>
        public static float EvaluateCurve(NativeArray<UnityEngine.Keyframe> allKeys, int2 slice, float time)
        {
            // 验证输入参数
            if (!allKeys.IsCreated)
            {
                return 0f; // 数组未创建，返回默认值
            }

            if (math.isnan(time) || math.isinf(time))
            {
                time = 0f; // 无效时间，使用默认值
            }

            var start = slice.x; 
            var count = slice.y;
            
            // 验证slice参数
            if (start < 0 || count <= 0 || start >= allKeys.Length)
            {
                return 0f; // 无效slice，返回默认值
            }

            // 确保不会越界
            var actualCount = math.min(count, allKeys.Length - start);
            if (actualCount <= 0) 
                return 0f;
            
            if (actualCount == 1) 
            {
                var key = allKeys[start];
                return math.isnan(key.value) || math.isinf(key.value) ? 0f : key.value;
            }

            var end = start + actualCount - 1;
            
            // 在区间内查找相邻关键帧
            for (var i = start; i < end; i++)
            {
                var k1 = allKeys[i]; 
                var k2 = allKeys[i + 1];
                
                // 验证关键帧数据的有效性
                if (math.isnan(k1.time) || math.isinf(k1.time) || 
                    math.isnan(k2.time) || math.isinf(k2.time) ||
                    math.isnan(k1.value) || math.isinf(k1.value) ||
                    math.isnan(k2.value) || math.isinf(k2.value))
                {
                    continue; // 跳过无效的关键帧
                }
                
                if (k1.time <= time && k2.time >= time)
                {
                    var dt = k2.time - k1.time;
                    if (dt <= 1e-6f) 
                        return k1.value;
                    
                    var t = (time - k1.time) / dt;
                    t = math.saturate(t); // 确保插值参数在有效范围内
                    return math.lerp(k1.value, k2.value, t);
                }
            }
            
            // 边界外回退到端点值，确保返回有效值
            var startKey = allKeys[start];
            var endKey = allKeys[end];
            
            if (time < startKey.time)
            {
                return math.isnan(startKey.value) || math.isinf(startKey.value) ? 0f : startKey.value;
            }
            
            return math.isnan(endKey.value) || math.isinf(endKey.value) ? 0f : endKey.value;
        }

        /// <summary>
        /// 从采样条读取遮罩值：normalizedDist(0..1) 映射到条带索引。
        /// 增强了边界条件验证和错误恢复。
        /// </summary>
        public static float EvaluateStrip(NativeArray<float> strips, int2 slice, int stripResolution, float normalizedDist)
        {
            // 验证输入参数
            if (!strips.IsCreated)
            {
                return 0f; // 数组未创建
            }

            if (math.isnan(normalizedDist) || math.isinf(normalizedDist))
            {
                normalizedDist = 0f; // 无效距离，使用默认值
            }

            var start = slice.x; 
            var count = slice.y;
            
            // 验证参数有效性
            if (stripResolution <= 1 || count <= 0 || start < 0 || start >= strips.Length)
            {
                return 0f;
            }

            // 确保不会越界
            var actualCount = math.min(count, strips.Length - start);
            if (actualCount <= 0)
                return 0f;

            // 限制stripResolution不超过实际可用数据
            var effectiveResolution = math.min(stripResolution, actualCount);
            if (effectiveResolution <= 1)
            {
                var value = strips[start];
                return math.isnan(value) || math.isinf(value) ? 0f : value;
            }

            // 归一化到 [0, effectiveResolution-1]
            var fIndex = math.saturate(normalizedDist) * (effectiveResolution - 1);
            var idxA = math.clamp((int)math.floor(fIndex), 0, effectiveResolution - 1);
            var idxB = math.clamp(idxA + 1, 0, effectiveResolution - 1);
            
            // 确保索引不会越界
            idxA = math.min(idxA, actualCount - 1);
            idxB = math.min(idxB, actualCount - 1);
            
            var w = fIndex - idxA;
            w = math.saturate(w); // 确保权重在有效范围内
            
            var a = strips[start + idxA];
            var b = strips[start + idxB];
            
            // 验证采样值的有效性
            if (math.isnan(a) || math.isinf(a)) a = 0f;
            if (math.isnan(b) || math.isinf(b)) b = 0f;
            
            return math.lerp(a, b, w);
        }

        /// <summary>
        /// 共享的灰度 Blend 算法，匹配编辑器预览与 GPU 权重库实现。
        /// BlendMode 枚举序数：需与 Runtime/Core/PathTool.Data.cs 保持一致。
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
                        ? (2f * baseValue * layerValue)
                        : (1f - 2f * (1f - baseValue) * (1f - layerValue));
                case 4: // Screen (float variant)
                    return 1f - (1f - baseValue) * (1f - layerValue);
                case 5: // Lerp (use layer as alpha)
                    return math.lerp(baseValue, layerValue, math.saturate(layerValue));
                default: // Normal (override)
                    return layerValue;
            }
        }

        /// <summary>
        /// 在 Job 中从 2D MaskAtlas 采样遮罩值。
        /// atlas: 行优先存储，行数 = layerCount * pathSamples，列数 = atlasWidth。
        /// layerIndex: 遮罩层索引 (0-based)
        /// normalizedDist: 0..1 横向坐标 (左0, 右1，非对称)
        /// pathProgress: 0..1 沿路径的进度 (0=起点,1=终点)
        /// pathSamples: atlas 中纵向采样行数。
        /// maskThreshold: 遮罩阈值，用于匹配GPU着色器逻辑。
        /// </summary>
        public static float SampleMaskAtlas(NativeArray<float> atlas, int atlasWidth, int pathSamples, int layerIndex, float normalizedDist, float pathProgress, float maskThreshold = 0f)
        {
            if (!atlas.IsCreated || atlas.Length == 0 || atlasWidth <= 0 || pathSamples <= 0)
                return 0f;

            // 先做参数校验
            normalizedDist = (math.isnan(normalizedDist) || math.isinf(normalizedDist)) ? 0f : normalizedDist;
            pathProgress = (math.isnan(pathProgress) || math.isinf(pathProgress)) ? 0.5f : pathProgress;

            var layerRowStart = layerIndex * pathSamples;
            var atlasHeight = atlas.Length / atlasWidth;
            if (layerRowStart < 0 || layerRowStart >= atlasHeight)
                return 0f;

            // 计算双线性插值坐标
            var maxX = atlasWidth - 1;
            var maxY = pathSamples - 1;

            var fX = math.saturate(normalizedDist) * maxX;
            var xA = (int)math.floor(fX);
            var xB = math.min(xA + 1, maxX);
            var wx = fX - xA;

            var fY = math.saturate(pathProgress) * maxY;
            var yA = (int)math.floor(fY);
            var yB = math.min(yA + 1, maxY);
            var wy = fY - yA;

            // 取四个邻居
            int rowAOffset = (layerRowStart + yA) * atlasWidth;
            int rowBOffset = (layerRowStart + yB) * atlasWidth;

            var v00 = atlas[rowAOffset + xA];
            var v10 = atlas[rowAOffset + xB];
            var v01 = atlas[rowBOffset + xA];
            var v11 = atlas[rowBOffset + xB];

            // 处理无效值
            if (math.isnan(v00) || math.isinf(v00)) v00 = 0f;
            if (math.isnan(v10) || math.isinf(v10)) v10 = 0f;
            if (math.isnan(v01) || math.isinf(v01)) v01 = 0f;
            if (math.isnan(v11) || math.isinf(v11)) v11 = 0f;

            // 双线性插值
            var v0 = math.lerp(v00, v10, wx);
            var v1 = math.lerp(v01, v11, wx);
            var v = math.lerp(v0, v1, wy);
            
            // 应用遮罩阈值调整，匹配GPU着色器逻辑
            // mask = saturate((mask - maskThreshold) / max(1e-5, 1.0 - maskThreshold))
            var thresholded = math.saturate((v - maskThreshold) / math.max(1e-5f, 1.0f - maskThreshold));
            return thresholded;
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

            if (paintedCount > 1 && total > 1e-5f)
            {
                var invTotal = 1f / total;
                for (var i = 0; i < layerCount; i++)
                {
                    alphamaps[baseIndex + i] *= invTotal;
                }
            }
            else if (paintedCount == 0 && firstValidSplatIndex >= 0)
            {
                for (var i = 0; i < layerCount; i++)
                {
                    alphamaps[baseIndex + i] = (i == firstValidSplatIndex) ? 1f : 0f;
                }
            }
        }

        // 向后兼容的一维版本：假设 pathSamples==1 ，pathProgress=0.5
        public static float SampleMaskAtlas(NativeArray<float> atlas, int atlasWidth, int layerIndex, float normalizedDist)
        {
            return SampleMaskAtlas(atlas, atlasWidth, 1, layerIndex, normalizedDist, 0.5f, 0f);
        }

    }
}