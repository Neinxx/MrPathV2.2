using Unity.Collections;
using Unity.Mathematics;
using NativeArrayExtensions = MrPathV2._2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace MrPathV2._2.Runtime.Core
{
    public static class RoadContourGenerator
    {
        public static void GenerateContour(PathSpine spine, PathProfile profile, out NativeArray<float2> contour, out float4 bounds, Allocator allocator)
        {
            if (spine.VertexCount < 2 || profile == null)
            {
                contour = NativeArrayExtensions.CreateTracked<float2>(0, allocator);
                bounds = float4.zero;
                return;
            }

            // 【核心修正】从新的 Profile 参数中获取道路的总范围
            var maxExtent = profile.roadWidth / 2f + profile.falloffWidth;

            var leftPoints = new NativeList<float2>(spine.VertexCount, Allocator.Temp);
            var rightPoints = new NativeList<float2>(spine.VertexCount, Allocator.Temp);

            for (var i = 0; i < spine.VertexCount; i++)
            {
                var p = new float2(spine.points[i].x, spine.points[i].z);

                var dirToPrev = i > 0
                    ? math.normalize(p - new float2(spine.points[i - 1].x, spine.points[i - 1].z))
                    : math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

                var dirToNext = i < spine.VertexCount - 1
                    ? math.normalize(new float2(spine.points[i + 1].x, spine.points[i + 1].z) - p)
                    : math.normalize(new float2(spine.tangents[i].x, spine.tangents[i].z));

                var tangent = math.normalize(dirToPrev + dirToNext);
                var miter = new float2(-tangent.y, tangent.x);

                var dot = math.dot(miter, new float2(-dirToNext.y, dirToNext.x));
                var miterLimit = maxExtent * 2f;
                var miterLength = math.min(maxExtent / math.max(math.abs(dot), 0.1f), miterLimit);

                leftPoints.Add(p - miter * miterLength);
                rightPoints.Add(p + miter * miterLength);
            }

            var contourList = new NativeList<float2>(leftPoints.Length + rightPoints.Length, allocator);
            contourList.AddRange(rightPoints.AsArray());
            for (var i = leftPoints.Length - 1; i >= 0; i--)
            {
                contourList.Add(leftPoints[i]);
            }

            leftPoints.Dispose();
            rightPoints.Dispose();

            // 创建一个新的 NativeArray 来复制数据，而不是直接使用 AsArray()
            contour = NativeArrayExtensions.CreateTracked<float2>(contourList.Length, allocator);
            for (var i = 0; i < contourList.Length; i++)
            {
                contour[i] = contourList[i];
            }

            // 现在可以安全地释放 contourList
            contourList.Dispose();

            if (contour.Length > 0)
            {
                var min = contour[0];
                var max = contour[0];
                for (var i = 1; i < contour.Length; i++)
                {
                    min = math.min(min, contour[i]);
                    max = math.max(max, contour[i]);
                }
                bounds = new float4(min, max);
            }
            else
            {
                bounds = float4.zero;
            }
        }
    }
}
