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

            // 统一使用工具方法进行裁剪：当轮廓不可用时自动退化为 AABB 粗裁剪
            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, contourBounds, roadContour)) return;

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
        [ReadOnly] public RecipeData recipe;
        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int alphamapResolution;
        [ReadOnly] public int alphamapLayerCount;

        [ReadOnly] public NativeArray<float2> roadContour;
        [ReadOnly] public float4 contourBounds;

        public NativeArray<float> alphamaps;

        // 改为统一使用 TerrainJobsUtility.IsPointInContour，以在轮廓不可用时自动回退到 AABB 粗裁剪。

        public void Execute(int index)
        {
            if (spine.Length < 2) return;

            int y = index / alphamapResolution;
            int x = index % alphamapResolution;
            float2 worldPos2D = new float2(
                terrainPos.x + (x / (float)(alphamapResolution - 1)) * terrainSize.x,
                terrainPos.z + (y / (float)(alphamapResolution - 1)) * terrainSize.z
            );

            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, contourBounds, roadContour)) return;

            float min2dDistSq = float.MaxValue; int closestSegmentIndex = -1; float tClosest = 0;
            for (int i = 0; i < spine.points.Length - 1; i++)
            {
                float2 a = spine.points[i].xz; float2 b = spine.points[i + 1].xz;
                float2 ab = b - a; float2 ap = worldPos2D - a;
                float t = math.saturate(math.dot(ap, ab) / math.dot(ab, ab));
                float2 closest = a + t * ab; float2 diff = worldPos2D - closest; float distSq = math.dot(diff, diff);
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 closestPointOnSpine = math.lerp(spine.points[closestSegmentIndex], spine.points[closestSegmentIndex + 1], tClosest);
            float3 normal = math.normalize(math.lerp(spine.normals[closestSegmentIndex], spine.normals[closestSegmentIndex + 1], tClosest));
            float3 tangent = math.normalize(math.lerp(spine.tangents[closestSegmentIndex], spine.tangents[closestSegmentIndex + 1], tClosest));
            float3 right = math.normalize(math.cross(profile.forceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            float halfRoadWidth = profile.roadWidth / 2f;
            float signedDistFromCenter = math.dot(new float2(worldPos2D.x - closestPointOnSpine.x, worldPos2D.y - closestPointOnSpine.z), right.xz);
            float normalizedDist = math.saturate(math.abs(signedDistFromCenter) / (halfRoadWidth + 1e-8f));

            int baseAlphaIndex = index * alphamapLayerCount;
            if (baseAlphaIndex < 0 || baseAlphaIndex + alphamapLayerCount > alphamaps.Length) return;
            for (int i = 0; i < alphamapLayerCount; i++) alphamaps[baseAlphaIndex + i] = 0;

            bool anyPainted = false; int firstValidSplatIndex = -1;
            for (int i = 0; i < recipe.Length; i++)
            {
                int splatIndex = recipe.terrainLayerIndices[i];
                if (splatIndex < 0 || splatIndex >= alphamapLayerCount) continue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;

                float layerMask = TerrainJobsUtility.EvaluateStrip(recipe.strips, recipe.stripSlices[i], recipe.stripResolution, normalizedDist);
                if (layerMask > 1e-6f) anyPainted = true;

                int mode = recipe.blendModes[i];
                int pixIdx = baseAlphaIndex + splatIndex;
                float baseValue = alphamaps[pixIdx];
                float blended = TerrainJobsUtility.Blend(baseValue, layerMask, mode);
                alphamaps[pixIdx] = blended;
            }

            if (!anyPainted && firstValidSplatIndex >= 0)
            {
                alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }

            float total = 0; for (int i = 0; i < alphamapLayerCount; i++) total += alphamaps[baseAlphaIndex + i];
            if (total > 1e-5f)
            {
                for (int i = 0; i < alphamapLayerCount; i++) alphamaps[baseAlphaIndex + i] /= total;
            }
            else if (firstValidSplatIndex >= 0)
            {
                alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }
        }
    }
}