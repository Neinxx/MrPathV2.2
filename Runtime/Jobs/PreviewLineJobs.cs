using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     预览线条点生成Job集合（CPU并行）：Bezier与Catmull-Rom。
    ///     - 仅负责数学计算与点生成，不直接使用UnityEditor Handles（主线程绘制）。
    ///     - 与预览渲染器协作：生成 float3 点，再由渲染器转换为 Vector3 并绘制。
    /// </summary>
    public static class PreviewLineJobs
    {
        /// <summary>
        ///     Bezier点生成（分辨率 resolution+1 个点，包含终点）。
        /// </summary>
        public struct GenerateBezierPointsJob : IJobParallelFor
        {
            public float3 P0;
            public float3 P1;
            public float3 P2;
            public float3 P3;
            public int Resolution;
            [WriteOnly]
            public NativeArray<float3> Points; // 长度需为 resolution+1

            public void Execute(int index)
            {
                var t = (float)index / math.max(1, Resolution);
                var u = 1f - t;
                var tt = t * t;
                var uu = u * u;
                var uuu = uu * u;
                var ttt = tt * t;

                var result = uuu * P0 + 3f * uu * t * P1 + 3f * u * tt * P2 + ttt * P3;
                Points[index] = result;
            }
        }

        /// <summary>
        ///     Catmull-Rom样条点生成（每段生成 resolution 个点，不包含段终点）。
        ///     输出总数： (controlPoints.Length - 3) * resolution
        /// </summary>
        public struct GenerateCatmullRomPointsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> ControlPoints; // 至少4个点
            public int Resolution; // 每段点数（不含终点）
            [WriteOnly]
            public NativeArray<float3> Points;        // 总长度：segments * resolution

            public void Execute(int index)
            {
                int segments = math.max(0, ControlPoints.Length - 3);
                if (segments == 0)
                {
                    return;
                }

                int segIndex = index / math.max(1, Resolution);
                int localIdx = index % math.max(1, Resolution);
                segIndex = math.clamp(segIndex, 0, segments - 1);

                var p0 = ControlPoints[segIndex];
                var p1 = ControlPoints[segIndex + 1];
                var p2 = ControlPoints[segIndex + 2];
                var p3 = ControlPoints[segIndex + 3];

                var t = (float)localIdx / math.max(1, Resolution);
                var tt = t * t;
                var ttt = tt * t;

                var result = 0.5f * (
                    2f * p1 +
                    (-p0 + p2) * t +
                    (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                    (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
                );

                Points[index] = result;
            }
        }
    }
}
