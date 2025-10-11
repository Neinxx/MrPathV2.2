using Unity.Collections;
using Unity.Mathematics;

namespace MrPathV2
{
    /// <summary>
    /// Terrain 作业的通用辅助函数：曲线评估与轮廓点检测。
    /// 供 PaintSplatmapJob 与 ModifyAlphamapsJob 等复用，避免重复逻辑。
    /// </summary>
    public static class TerrainJobsUtility
    {
        /// <summary>
        /// Ray Casting 多边形包含检测，带包围盒提前剔除。
        /// </summary>
        public static bool IsPointInContour(float2 p, float4 bounds, NativeArray<float2> contour)
        {
            // 当多边形轮廓不可用（长度不足）时，退化为仅使用 AABB 粗裁剪。
            if (contour.Length < 3)
            {
                return !(p.x < bounds.x || p.y < bounds.y || p.x > bounds.z || p.y > bounds.w);
            }
            if (p.x < bounds.x || p.y < bounds.y || p.x > bounds.z || p.y > bounds.w) return false;

            bool inside = false; int n = contour.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float2 pi = contour[i]; float2 pj = contour[j];
                bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                                 (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-8f) + pi.x);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// 在 Job 中对合并后的关键帧数组进行安全线性评估。
        /// slice.x 为起始偏移，slice.y 为关键帧数量。
        /// </summary>
        public static float EvaluateCurve(NativeArray<UnityEngine.Keyframe> allKeys, int2 slice, float time)
        {
            int start = slice.x; int count = slice.y;
            if (count <= 0) return 0f;
            if (count == 1) return allKeys[start].value;

            int end = start + count - 1;
            // 在区间内查找相邻关键帧
            for (int i = start; i < end; i++)
            {
                var k1 = allKeys[i]; var k2 = allKeys[i + 1];
                if (k1.time <= time && k2.time >= time)
                {
                    float dt = k2.time - k1.time;
                    if (dt <= 1e-6f) return k1.value;
                    float t = (time - k1.time) / dt;
                    return math.lerp(k1.value, k2.value, t);
                }
            }
            // 边界外回退到端点值
            if (time < allKeys[start].time) return allKeys[start].value;
            return allKeys[end].value;
        }

        /// <summary>
        /// 从采样条读取遮罩值：normalizedDist(0..1) 映射到条带索引。
        /// </summary>
        public static float EvaluateStrip(NativeArray<float> strips, int2 slice, int stripResolution, float normalizedDist)
        {
            int start = slice.x; int count = slice.y;
            if (stripResolution <= 1 || count <= 0) return 0f;
            // 归一化到 [0, stripResolution-1]
            float fIndex = math.saturate(normalizedDist) * (stripResolution - 1);
            int idxA = math.clamp((int)math.floor(fIndex), 0, stripResolution - 1);
            int idxB = math.clamp(idxA + 1, 0, stripResolution - 1);
            float w = fIndex - idxA;
            float a = strips[start + idxA];
            float b = strips[start + idxB];
            return math.lerp(a, b, w);
        }

        /// <summary>
        /// 共享的灰度 Blend 算法，匹配编辑器预览的实现。
        /// BlendMode 枚举序数：需与 Runtime/Core/PathTool.Data.cs 保持一致。
        /// </summary>
        public static float Blend(float baseValue, float layerValue, int blendModeOrdinal)
        {
            // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
            switch (blendModeOrdinal)
            {
                case 1: return baseValue * layerValue; // Multiply
                case 2: return math.saturate(baseValue + layerValue); // Add
                case 3: // Overlay
                    return baseValue < 0.5f ? (2f * baseValue * layerValue) : (1f - 2f * (1f - baseValue) * (1f - layerValue));
                case 4: return 1f - (1f - baseValue) * (1f - layerValue); // Screen
                case 5: return math.lerp(baseValue, layerValue, math.saturate(layerValue)); // Lerp（用 layerValue 作为权重）
                case 6: return math.saturate(baseValue + layerValue); // Additive（与 Add 相同并夹取）
                default: return layerValue; // Normal：直接覆盖（预合成的遮罩值）
            }
        }
    }
}