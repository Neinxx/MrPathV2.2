// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Jobs/TerrainJobs.cs (包含所有辅助方法的最终完整版)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct ModifyHeightsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;
        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int heightmapResolution;
        [ReadOnly] public NativeArray<float> originalHeights;
        
        // 可选的轮廓数据，用于性能优化
        [ReadOnly] public NativeArray<float2> roadContour;
        [ReadOnly] public float4 contourBounds;

        public NativeArray<float> heights;

        public void Execute(int index)
        {
            if (spine.Length < 2) return;

            int hmY = index / heightmapResolution;
            int hmX = index % heightmapResolution;
            float3 worldPos3D = new float3(
                terrainPos.x + hmX / (float)(heightmapResolution - 1) * terrainSize.x,
                0,
                terrainPos.z + hmY / (float)(heightmapResolution - 1) * terrainSize.z
            );
            float2 worldPos2D = worldPos3D.xz;

            if (roadContour.IsCreated && (worldPos2D.x < contourBounds.x || worldPos2D.y < contourBounds.y ||
                worldPos2D.x > contourBounds.z || worldPos2D.y > contourBounds.w))
            {
                return;
            }

            float min2dDistSq = float.MaxValue;
            int closestSegmentIndex = -1;
            float tClosest = 0;
            for (int i = 0; i < spine.Length - 1; i++)
            {
                float2 p1 = spine.points[i].xz; float2 p2 = spine.points[i + 1].xz;
                float2 segmentVec = p2 - p1; float segLenSq = math.lengthsq(segmentVec);
                float t; float distSq;
                if (segLenSq < 0.0001f) { t = 0; distSq = math.distancesq(worldPos2D, p1); }
                else { t = math.saturate(math.dot(worldPos2D - p1, segmentVec) / segLenSq); float2 c = p1 + t * segmentVec; distSq = math.distancesq(worldPos2D, c); }
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 closestPointOnSpine = math.lerp(spine.points[closestSegmentIndex], spine.points[closestSegmentIndex + 1], tClosest);
            float3 normal = math.normalize(math.lerp(spine.normals[closestSegmentIndex], spine.normals[closestSegmentIndex + 1], tClosest));
            float3 tangent = math.normalize(math.lerp(spine.tangents[closestSegmentIndex], spine.tangents[closestSegmentIndex + 1], tClosest));
            float3 right = math.normalize(math.cross(profile.forceHorizontal ? new float3(0,1,0) : normal, tangent));

            float signedDistFromSpine = math.dot(worldPos3D.xz - closestPointOnSpine.xz, right.xz);
            float halfRoadWidth = profile.roadWidth / 2f;
            float absDist = math.abs(signedDistFromSpine);
            float finalWorldHeight;

            if (absDist <= halfRoadWidth)
            {
                float normalizedDist = signedDistFromSpine / halfRoadWidth;
                float crossSectionHeight = profile.EvaluateCrossSection(normalizedDist);
                finalWorldHeight = closestPointOnSpine.y + crossSectionHeight;
            }
            else if (absDist <= halfRoadWidth + profile.falloffWidth)
            {
                float normalizedFalloff = (absDist - halfRoadWidth) / profile.falloffWidth;
                float blendWeight = profile.EvaluateFalloff(normalizedFalloff);
                float normalizedEdgeDist = math.sign(signedDistFromSpine);
                float edgeCrossSectionHeight = profile.EvaluateCrossSection(normalizedEdgeDist);
                float roadEdgeHeight = closestPointOnSpine.y + edgeCrossSectionHeight;
                float originalTerrainHeight = originalHeights[index] * terrainSize.y + terrainPos.y;
                finalWorldHeight = math.lerp(originalTerrainHeight, roadEdgeHeight, blendWeight);
            }
            else
            {
                return;
            }

            heights[index] = math.saturate((finalWorldHeight - terrainPos.y) / terrainSize.y);
        }
    }


    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct ModifyAlphamapsJob : IJobParallelFor
    {
        // ... (ModifyAlphamapsJob 的所有代码保持不变，因为它也需要 IsPointInContour)
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;
        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int alphamapResolution;
        [ReadOnly] public int alphamapLayerCount;

        [ReadOnly] public NativeArray<float2> roadContour;
        [ReadOnly] public float4 contourBounds;

        public NativeArray<float> alphamaps;

        /// <summary>
        /// 【完整实现】点在多边形内检测 (Ray Casting 算法)。
        /// </summary>
        private bool IsPointInContour(float2 p)
        {
            int n = roadContour.Length;
            if (n < 3) return false;
            bool isInside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((roadContour[i].y > p.y) != (roadContour[j].y > p.y)) &&
                    (p.x < (roadContour[j].x - roadContour[i].x) * (p.y - roadContour[i].y) / (roadContour[j].y - roadContour[i].y) + roadContour[i].x))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        public void Execute(int index)
        {
            if (spine.Length < 2 || roadContour.Length < 3) return;

            int amY = index / alphamapResolution;
            int amX = index % alphamapResolution;
            float3 worldPos3D = new float3(
                terrainPos.x + amX / (float)(alphamapResolution - 1) * terrainSize.x, 0,
                terrainPos.z + amY / (float)(alphamapResolution - 1) * terrainSize.z
            );
            float2 worldPos2D = worldPos3D.xz;

            if (worldPos2D.x < contourBounds.x || worldPos2D.y < contourBounds.y ||
                worldPos2D.x > contourBounds.z || worldPos2D.y > contourBounds.w)
            {
                return;
            }

            if (!IsPointInContour(worldPos2D))
            {
                return;
            }

            float min2dDistSq = float.MaxValue; int closestSegmentIndex = -1; float tClosest = 0;
            for (int i = 0; i < spine.Length - 1; i++)
            {
                float2 p1 = spine.points[i].xz; float2 p2 = spine.points[i + 1].xz;
                float2 segmentVec = p2 - p1; float segLenSq = math.lengthsq(segmentVec);
                float t; float distSq;
                if (segLenSq < 0.0001f) { t = 0; distSq = math.distancesq(worldPos2D, p1); }
                else { t = math.saturate(math.dot(worldPos2D - p1, segmentVec) / segLenSq); float2 c = p1 + t * segmentVec; distSq = math.distancesq(worldPos2D, c); }
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 segStart = spine.points[closestSegmentIndex]; float3 segEnd = spine.points[closestSegmentIndex + 1];
            float3 segNormalStart = spine.normals[closestSegmentIndex]; float3 segNormalEnd = spine.normals[closestSegmentIndex + 1];
            float3 segTangentStart = spine.tangents[closestSegmentIndex]; float3 segTangentEnd = spine.tangents[closestSegmentIndex + 1];
            float3 closestPointOnSpine = math.lerp(segStart, segEnd, tClosest);
            float3 normal = math.normalize(math.lerp(segNormalStart, segNormalEnd, tClosest));
            float3 tangent = math.normalize(math.lerp(segTangentStart, segTangentEnd, tClosest));
            float3 upVector = profile.forceHorizontal ? new float3(0, 1, 0) : normal;
            float3 right = math.normalize(math.cross(upVector, tangent));
            float3 vectorToPoint = worldPos3D - closestPointOnSpine;
            float signedDistFromSpine = math.dot(vectorToPoint, right);

            for (int i = 0; i < profile.Length; i++)
            {
                var layer = profile.layers[i]; int splatIndex = profile.terrainLayerIndices[i];
                if (splatIndex == -1) continue;
                float halfWidth = layer.width / 2f;
                float distFromLayerCenter = math.abs(signedDistFromSpine - layer.horizontalOffset);
                if (distFromLayerCenter <= halfWidth)
                {
                    float coreWidth = halfWidth * layer.falloff;
                    float falloffBandwidth = halfWidth - coreWidth;
                    float blend = 1.0f;
                    if (falloffBandwidth > 0.001f) { float p = math.saturate((distFromLayerCenter - coreWidth) / falloffBandwidth); blend = 1.0f - p * p; }
                    int baseAlphaIndex = index * alphamapLayerCount;
                    float currentWeight = alphamaps[baseAlphaIndex + splatIndex];
                    alphamaps[baseAlphaIndex + splatIndex] = math.max(currentWeight, blend);
                }
            }
            //int
            int finalBaseAlphaIndex = index * alphamapLayerCount;
            float totalWeight = 0;
            for (int i = 0; i < alphamapLayerCount; i++) { totalWeight += alphamaps[finalBaseAlphaIndex + i]; }
            if (totalWeight > 0.001f) { for (int i = 0; i < alphamapLayerCount; i++) { alphamaps[finalBaseAlphaIndex + i] /= totalWeight; } }
        }
    }
}