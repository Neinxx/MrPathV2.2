using Unity.Collections;
using Unity.Mathematics;

namespace MrPathV2
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
            bool inside = false; 
            int n = contour.Length;
            
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float2 pi = contour[i]; 
                float2 pj = contour[j];
                
                // 验证轮廓点的有效性
                if (!IsValidPoint(pi) || !IsValidPoint(pj))
                {
                    continue; // 跳过无效的轮廓点
                }
                
                // 增强数值稳定性的交点检测
                bool intersect = ((pi.y > p.y) != (pj.y > p.y));
                if (intersect)
                {
                    float denominator = pj.y - pi.y;
                    // 避免除零，使用更大的epsilon值提高稳定性
                    if (math.abs(denominator) > 1e-6f)
                    {
                        float intersectionX = (pj.x - pi.x) * (p.y - pi.y) / denominator + pi.x;
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

            float2 min = new float2(float.MaxValue, float.MaxValue);
            float2 max = new float2(float.MinValue, float.MinValue);

            for (int i = 0; i < contour.Length; i++)
            {
                float2 point = contour[i];
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

            int start = slice.x; 
            int count = slice.y;
            
            // 验证slice参数
            if (start < 0 || count <= 0 || start >= allKeys.Length)
            {
                return 0f; // 无效slice，返回默认值
            }

            // 确保不会越界
            int actualCount = math.min(count, allKeys.Length - start);
            if (actualCount <= 0) 
                return 0f;
            
            if (actualCount == 1) 
            {
                var key = allKeys[start];
                return math.isnan(key.value) || math.isinf(key.value) ? 0f : key.value;
            }

            int end = start + actualCount - 1;
            
            // 在区间内查找相邻关键帧
            for (int i = start; i < end; i++)
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
                    float dt = k2.time - k1.time;
                    if (dt <= 1e-6f) 
                        return k1.value;
                    
                    float t = (time - k1.time) / dt;
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

            int start = slice.x; 
            int count = slice.y;
            
            // 验证参数有效性
            if (stripResolution <= 1 || count <= 0 || start < 0 || start >= strips.Length)
            {
                return 0f;
            }

            // 确保不会越界
            int actualCount = math.min(count, strips.Length - start);
            if (actualCount <= 0)
                return 0f;

            // 限制stripResolution不超过实际可用数据
            int effectiveResolution = math.min(stripResolution, actualCount);
            if (effectiveResolution <= 1)
            {
                var value = strips[start];
                return math.isnan(value) || math.isinf(value) ? 0f : value;
            }

            // 归一化到 [0, effectiveResolution-1]
            float fIndex = math.saturate(normalizedDist) * (effectiveResolution - 1);
            int idxA = math.clamp((int)math.floor(fIndex), 0, effectiveResolution - 1);
            int idxB = math.clamp(idxA + 1, 0, effectiveResolution - 1);
            
            // 确保索引不会越界
            idxA = math.min(idxA, actualCount - 1);
            idxB = math.min(idxB, actualCount - 1);
            
            float w = fIndex - idxA;
            w = math.saturate(w); // 确保权重在有效范围内
            
            float a = strips[start + idxA];
            float b = strips[start + idxB];
            
            // 验证采样值的有效性
            if (math.isnan(a) || math.isinf(a)) a = 0f;
            if (math.isnan(b) || math.isinf(b)) b = 0f;
            
            return math.lerp(a, b, w);
        }

        /// <summary>
        /// 共享的灰度 Blend 算法，匹配编辑器预览的实现。
        /// BlendMode 枚举序数：需与 Runtime/Core/PathTool.Data.cs 保持一致。
        /// 增强了数值稳定性和错误恢复。
        /// </summary>
        public static float Blend(float baseValue, float layerValue, int blendModeOrdinal)
        {
            // 验证输入值的有效性
            if (math.isnan(baseValue) || math.isinf(baseValue)) baseValue = 0f;
            if (math.isnan(layerValue) || math.isinf(layerValue)) layerValue = 0f;
            
            // 确保值在合理范围内
            baseValue = math.saturate(baseValue);
            layerValue = math.saturate(layerValue);
            
            float result;
            
            // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
            switch (blendModeOrdinal)
            {
                case 1: // Multiply
                    result = baseValue * layerValue;
                    break;
                case 2: // Add
                    result = math.saturate(baseValue + layerValue);
                    break;
                case 3: // Overlay
                    result = baseValue < 0.5f ? 
                        (2f * baseValue * layerValue) : 
                        (1f - 2f * (1f - baseValue) * (1f - layerValue));
                    break;
                case 4: // Screen
                    result = 1f - (1f - baseValue) * (1f - layerValue);
                    break;
                case 5: // Lerp（用 layerValue 作为权重）
                    result = math.lerp(baseValue, layerValue, math.saturate(layerValue));
                    break;
                case 6: // Additive（与 Add 相同并夹取）
                    result = math.saturate(baseValue + layerValue);
                    break;
                default: // Normal：直接覆盖（预合成的遮罩值）
                    result = layerValue;
                    break;
            }
            
            // 最终验证结果的有效性
            if (math.isnan(result) || math.isinf(result))
            {
                result = baseValue; // 如果计算结果无效，回退到基础值
            }
            
            return math.saturate(result);
        }
    }
}