
using Unity.Collections;
using Unity.Mathematics;

namespace MrPathV2
{
    public static class RoadContourGenerator
    {
        public static void GenerateContour(PathSpine spine, PathProfile profile, out NativeArray<float2> contour, out float4 bounds, Allocator allocator)
        {
            if (spine.VertexCount < 2 || profile == null)
            {
                contour = new NativeArray<float2>(0, allocator);
                bounds = float4.zero;
                return;
            }

            // 【核心修正】从新的 Profile 参数中获取道路的总范围
            float maxExtent = profile.roadWidth / 2f + profile.falloffWidth;

            var leftPoints = new NativeList<float2>(Allocator.Temp);
            var rightPoints = new NativeList<float2>(Allocator.Temp);

            for (int i = 0; i < spine.VertexCount; i++)
            {
                float2 p = new float2(spine.points[i].x, spine.points[i].z);

                float2 dirToPrev = (i > 0) 
                    ? math.normalize(p - new float2(spine.points[i - 1].x, spine.points[i - 1].z)) 
                    : math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

                float2 dirToNext = (i < spine.VertexCount - 1) 
                    ? math.normalize(new float2(spine.points[i + 1].x, spine.points[i + 1].z) - p) 
                    : math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

                float2 tangent = math.normalize(dirToPrev + dirToNext);
                float2 miter = new float2(-tangent.y, tangent.x);

                float dot = math.dot(miter, new float2(-dirToNext.y, dirToNext.x));
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
}