// 文件: Runtime/Jobs/FindRoadPixelsJob.cs

using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace __temp.MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     两阶段地形绘制的第一阶段：计算每个像素与路径的关系并缓存。
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct FindRoadPixelsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] public float3 TerrainPos;
        [ReadOnly] public float3 TerrainSize;
        [ReadOnly] public int AlphamapResolution;
        [ReadOnly] public NativeArray<float2> RoadContour;
        [ReadOnly] public float4 ContourBounds;
        [ReadOnly] public Vector2Int CoverageMin;
        [ReadOnly] public Vector2Int CoverageMax;

        [WriteOnly] public NativeArray<RoadPixelInfo> PixelInfoMap;

        private const float Epsilon = 1e-6f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index) // 索引范围 [0, totalPixelsInBounds - 1]
        {
            var numPixelsX = CoverageMax.x - CoverageMin.x + 1;
            var localY = index / numPixelsX;
            var localX = index % numPixelsX;
            var x = CoverageMin.x + localX;
            var y = CoverageMin.y + localY;

            if (y > CoverageMax.y || !IsPixelInRoadContour(x, y, out var worldPos2D) || !CalculateDistanceAndProgress(worldPos2D, out var normalizedDist, out var pathProgress))
            {
                PixelInfoMap[index] = new RoadPixelInfo
                {
                    IsInside = false
                };
                return;
            }

            PixelInfoMap[index] = new RoadPixelInfo
            {
                IsInside = true,
                NormalizedDist = normalizedDist,
                PathProgress = pathProgress
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPixelInRoadContour(int x, int y, out float2 worldPos2D)
        {
            var invResolution = 1f / (AlphamapResolution - 1);
            worldPos2D = new float2(
                TerrainPos.x + x * invResolution * TerrainSize.x,
                TerrainPos.z + y * invResolution * TerrainSize.z
            );
            return TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CalculateDistanceAndProgress(float2 worldPos2D, out float normalizedDist, out float pathProgress)
        {
            normalizedDist = 1.0f;
            pathProgress = 0.0f;

            var minDistanceSq = float.MaxValue;
            var closestSegmentIndex = -1;
            var tClosest = 0f;
            // 记录弧长：与 GPU 端 compute 一致，按真实路径长度归一化
            var totalLen = 0f;
            var accumLenBeforeClosest = 0f;
            var closestSegLen = 0f;

            var spineSegmentCount = Spine.Points.Length - 1;
            if (spineSegmentCount < 0) return false;

            // 单次遍历：在统计最短距离的同时累积总弧长，并在更新最近段时记录该段前的累计长度与该段长度
            var accumLen = 0f;
            for (var i = 0; i < spineSegmentCount; i++)
            {
                var segmentStart = Spine.Points[i].xz;
                var segmentEnd = Spine.Points[i + 1].xz;
                var segmentVector = segmentEnd - segmentStart;
                var pointVector = worldPos2D - segmentStart;
                var segmentLengthSq = math.dot(segmentVector, segmentVector);
                var segmentLen = math.sqrt(math.max(segmentLengthSq, 0f));
                totalLen += segmentLen;

                var t = 0f;
                float distanceSq;

                if (segmentLengthSq < Epsilon)
                {
                    distanceSq = math.distancesq(worldPos2D, segmentStart);
                    t = 0f;
                }
                else
                {
                    t = math.saturate(math.dot(pointVector, segmentVector) / segmentLengthSq);
                    var closestPoint = segmentStart + t * segmentVector;
                    distanceSq = math.distancesq(worldPos2D, closestPoint);
                }

                if (distanceSq < minDistanceSq)
                {
                    minDistanceSq = distanceSq;
                    closestSegmentIndex = i;
                    tClosest = t;
                    accumLenBeforeClosest = accumLen;
                    closestSegLen = segmentLen;
                }

                // 将当前段长度累加入起点到该段末尾
                accumLen += segmentLen;
            }

            if (closestSegmentIndex == -1) return false;

            var spinePoint = math.lerp(Spine.Points[closestSegmentIndex], Spine.Points[closestSegmentIndex + 1], tClosest);
            var spineNormal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex], Spine.Normals[closestSegmentIndex + 1], tClosest));
            var spineTangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex], Spine.Tangents[closestSegmentIndex + 1], tClosest));

            var rightVector = math.normalize(math.cross(
                Profile.ForceHorizontal ? new float3(0, 1, 0) : spineNormal,
                spineTangent
            ));

            var halfRoadWidth = Profile.RoadWidth * 0.5f;
            if (halfRoadWidth < Epsilon) halfRoadWidth = Epsilon;

            var offsetVector = worldPos2D - spinePoint.xz;
            var signedDistance = math.dot(offsetVector, rightVector.xz);

            normalizedDist = math.saturate(0.5f * (signedDistance / halfRoadWidth + 1f));
            // 以弧长归一化进度，匹配 GPU 端 calculate_distance_progress_signed 实现
            var alongLen = accumLenBeforeClosest + tClosest * closestSegLen;
            var denom = math.max(totalLen, Epsilon);
            pathProgress = math.saturate(alongLen / denom);

            return true;
        }
    }
}
