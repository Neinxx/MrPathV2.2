// RoadContourGenerator.cs (终极稳定版)
using Unity.Collections;
using Unity.Mathematics;

public static class RoadContourGenerator
{
    public static void GenerateContour(PathSpine spine, PathProfile profile, out NativeArray<float2> contour, out float4 bounds, Allocator allocator)
    {
        if (spine.VertexCount < 2 || profile.layers.Count == 0)
        {
            contour = new NativeArray<float2>(0, allocator);
            bounds = float4.zero;
            return;
        }

        float maxExtent = 0;
        foreach (var layer in profile.layers)
        {
            maxExtent = math.max(maxExtent, math.abs(layer.horizontalOffset) + layer.width / 2f);
        }

        var leftPoints = new NativeList<float2>(Allocator.Temp);
        var rightPoints = new NativeList<float2>(Allocator.Temp);

        // --- 核心思想：不再使用不稳定的 per-point tangent ---
        // 我们使用更稳定的线段方向来计算法线，并为每个拐角计算一个平分向量（miter vector）
        for (int i = 0; i < spine.VertexCount; i++)
        {
            float2 p = new float2(spine.points[i].x, spine.points[i].z);

            // 【核心修正】
            float2 dirToPrev = (i > 0) ?
                math.normalize(p - new float2(spine.points[i - 1].x, spine.points[i - 1].z)) :
                math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

            float2 dirToNext = (i < spine.VertexCount - 1) ?
                math.normalize(new float2(spine.points[i + 1].x, spine.points[i + 1].z) - p) :
                math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

            float2 tangent = math.normalize(dirToPrev + dirToNext);
            float2 miter = new float2(-tangent.y, tangent.x);

            // 计算斜接长度，并将其限制在合理范围内，防止产生尖刺
            float dot = math.dot(miter, new float2(-dirToNext.y, dirToNext.x));
            // 将最大斜接长度限制为道路宽度的2倍，这是一个安全的上限
            float miterLimit = maxExtent * 2f;
            float miterLength = math.min(maxExtent / math.max(math.abs(dot), 0.1f), miterLimit);

            leftPoints.Add(p - miter * miterLength);
            rightPoints.Add(p + miter * miterLength);
        }

        var contourList = new NativeList<float2>(leftPoints.Length + rightPoints.Length, allocator);
        contourList.AddRange(rightPoints.AsArray());
        for (int i = leftPoints.Length - 1; i >= 0; i--)
        {
            contourList.Add(leftPoints[i]);
        }

        contour = contourList.AsArray();

        if (contour.Length > 0)
        {
            float2 min = contour[0]; float2 max = contour[0];
            for (int i = 1; i < contour.Length; i++) { min = math.min(min, contour[i]); max = math.max(max, contour[i]); }
            bounds = new float4(min, max);
        }
        else { bounds = float4.zero; }

        leftPoints.Dispose();
        rightPoints.Dispose();
    }
}