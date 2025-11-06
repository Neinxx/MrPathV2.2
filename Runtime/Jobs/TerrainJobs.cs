// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Jobs/TerrainJobs.cs (包含所有辅助方法的最终完整版)

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2.Runtime.Jobs
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct ModifyHeightsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] public float3 TerrainPos;
        [ReadOnly] public float3 TerrainSize;
        [ReadOnly] public int HeightmapResolution;
        [ReadOnly] public NativeArray<float> OriginalHeights;

        // 可选的轮廓数据，用于性能优化
        [ReadOnly] public NativeArray<float2> RoadContour;
        [ReadOnly] public float4 ContourBounds;

        public NativeArray<float> Heights;

        public void Execute(int index)
        {
            // 输入验证和早期返回
            if (!ValidateInput(index)) return;

            // 计算世界坐标
            var (hmX, hmY, worldPos2D, worldPos3D) = CalculateWorldPositions(index);

            // 轮廓检查
            if (!IsPointWithinContour(worldPos2D)) return;

            // 查找最近的脊线段
            var (closestSegmentIndex, tClosest) = FindClosestSegment(worldPos2D);
            if (closestSegmentIndex == -1) return;

            // 计算最终高度
            var finalWorldHeight = CalculateFinalWorldHeight(worldPos3D, closestSegmentIndex, tClosest);

            // 应用最终高度到高度图
            ApplyHeightToTerrain(index, finalWorldHeight);
        }

// 输入验证方法
        private bool ValidateInput(int index) => Spine.Length >= 2;

// 计算世界坐标位置
        private (int hmX, int hmY, float2 worldPos2D, float3 worldPos3D) CalculateWorldPositions(int index)
        {
            var hmY = index / HeightmapResolution;
            var hmX = index % HeightmapResolution;
            var worldPos3D = new float3(
                TerrainPos.x + hmX / (float)(HeightmapResolution - 1) * TerrainSize.x,
                0,
                TerrainPos.z + hmY / (float)(HeightmapResolution - 1) * TerrainSize.z
            );
            var worldPos2D = worldPos3D.xz;

            return (hmX, hmY, worldPos2D, worldPos3D);
        }

// 检查点是否在轮廓内
        private bool IsPointWithinContour(float2 worldPos2D) => TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour);

// 查找最近的脊线段
        private (int closestSegmentIndex, float tClosest) FindClosestSegment(float2 worldPos2D)
        {
            var min2dDistSq = float.MaxValue;
            var closestSegmentIndex = -1;
            float tClosest = 0;

            for (var i = 0; i < Spine.Length - 1; i++)
            {
                var (t, distSq) = CalculateSegmentDistance(i, worldPos2D);

                if (distSq < min2dDistSq)
                {
                    min2dDistSq = distSq;
                    closestSegmentIndex = i;
                    tClosest = t;
                }
            }

            return (closestSegmentIndex, tClosest);
        }

// 计算线段距离
        private (float t, float distSq) CalculateSegmentDistance(int segmentIndex, float2 worldPos2D)
        {
            var p1 = Spine.Points[segmentIndex].xz;
            var p2 = Spine.Points[segmentIndex + 1].xz;
            var segmentVec = p2 - p1;
            var segLenSq = math.lengthsq(segmentVec);

            if (segLenSq < 0.0001f)
            {
                return (0, math.distancesq(worldPos2D, p1));
            }
            var t = math.saturate(math.dot(worldPos2D - p1, segmentVec) / segLenSq);
            var c = p1 + t * segmentVec;
            var distSq = math.distancesq(worldPos2D, c);
            return (t, distSq);
        }

// 计算最终世界高度
        private float CalculateFinalWorldHeight(float3 worldPos3D, int closestSegmentIndex, float tClosest)
        {
            var worldPos2D = worldPos3D.xz;

            // 计算脊线上最近点
            var closestPointOnSpine = math.lerp(Spine.Points[closestSegmentIndex],
                Spine.Points[closestSegmentIndex + 1], tClosest);

            var normal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex],
                Spine.Normals[closestSegmentIndex + 1], tClosest));
            var tangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex],
                Spine.Tangents[closestSegmentIndex + 1], tClosest));
            var right = math.normalize(math.cross(Profile.ForceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            var signedDistFromSpine = math.dot(worldPos2D - closestPointOnSpine.xz, right.xz);
            var halfRoadWidth = Profile.RoadWidth / 2f;
            var absDist = math.abs(signedDistFromSpine);

            return CalculateHeightByDistance(absDist, signedDistFromSpine, closestPointOnSpine, normal, tangent, right);
        }

// 根据距离计算高度
        private float CalculateHeightByDistance(float absDist, float signedDistFromSpine, float3 closestPointOnSpine,
            float3 normal, float3 tangent, float3 right)
        {
            var halfRoadWidth = Profile.RoadWidth / 2f;

            // 在道路宽度内
            if (absDist <= halfRoadWidth)
            {
                return CalculateRoadInnerHeight(signedDistFromSpine, halfRoadWidth, closestPointOnSpine);
            }
            // 在边缘衰减区域内
            if (absDist <= halfRoadWidth + Profile.FalloffWidth)
            {
                return CalculateFalloffHeight(absDist, signedDistFromSpine, halfRoadWidth, closestPointOnSpine);
            }
            // 超出影响范围
            // 返回原始地形高度
            return GetOriginalTerrainHeight();
        }

// 计算道路内部高度
        private float CalculateRoadInnerHeight(float signedDistFromSpine, float halfRoadWidth, float3 closestPointOnSpine)
        {
            var normalizedDist = signedDistFromSpine / halfRoadWidth;
            var crossSectionHeight = Profile.EvaluateCrossSection(normalizedDist);
            return closestPointOnSpine.y + crossSectionHeight;
        }

// 计算边缘衰减区域高度
        private float CalculateFalloffHeight(float absDist, float signedDistFromSpine, float halfRoadWidth, float3 closestPointOnSpine)
        {
            var normalizedFalloff = (absDist - halfRoadWidth) / Profile.FalloffWidth;
            var blendWeight = Profile.EvaluateFalloff(normalizedFalloff);
            var normalizedEdgeDist = math.sign(signedDistFromSpine);
            var edgeCrossSectionHeight = Profile.EvaluateCrossSection(normalizedEdgeDist);
            var roadEdgeHeight = closestPointOnSpine.y + edgeCrossSectionHeight;
            var originalTerrainHeight = GetOriginalTerrainHeight();
            return math.lerp(originalTerrainHeight, roadEdgeHeight, blendWeight);
        }

// 获取原始地形高度
        private static float GetOriginalTerrainHeight() => 0;


// 应用高度到地形
        private void ApplyHeightToTerrain(int index, float finalWorldHeight)
        {
            Heights[index] = math.saturate((finalWorldHeight - TerrainPos.y) / TerrainSize.y);
        }

    }


}
