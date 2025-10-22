// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Jobs/TerrainJobs.cs (包含所有辅助方法的最终完整版)

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace __temp.MrPathV2._2.Runtime.Jobs
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
            if (Spine.Length < 2) return;

            int hmY = index / HeightmapResolution;
            int hmX = index % HeightmapResolution;
            float3 worldPos3D = new float3(
                TerrainPos.x + hmX / (float)(HeightmapResolution - 1) * TerrainSize.x,
                0,
                TerrainPos.z + hmY / (float)(HeightmapResolution - 1) * TerrainSize.z
            );
            float2 worldPos2D = worldPos3D.xz;

            // 统一使用工具方法进行裁剪：当轮廓不可用时自动退化为 AABB 粗裁剪
            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour)) return;

            float min2dDistSq = float.MaxValue;
            int closestSegmentIndex = -1;
            float tClosest = 0;
            for (int i = 0; i < Spine.Length - 1; i++)
            {
                float2 p1 = Spine.Points[i].xz; float2 p2 = Spine.Points[i + 1].xz;
                float2 segmentVec = p2 - p1; float segLenSq = math.lengthsq(segmentVec);
                float t; float distSq;
                if (segLenSq < 0.0001f) { t = 0; distSq = math.distancesq(worldPos2D, p1); }
                else { t = math.saturate(math.dot(worldPos2D - p1, segmentVec) / segLenSq); float2 c = p1 + t * segmentVec; distSq = math.distancesq(worldPos2D, c); }
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 closestPointOnSpine = math.lerp(Spine.Points[closestSegmentIndex], Spine.Points[closestSegmentIndex + 1], tClosest);
            float3 normal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex], Spine.Normals[closestSegmentIndex + 1], tClosest));
            float3 tangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex], Spine.Tangents[closestSegmentIndex + 1], tClosest));
            float3 right = math.normalize(math.cross(Profile.ForceHorizontal ? new float3(0,1,0) : normal, tangent));

            float signedDistFromSpine = math.dot(worldPos3D.xz - closestPointOnSpine.xz, right.xz);
            float halfRoadWidth = Profile.RoadWidth / 2f;
            float absDist = math.abs(signedDistFromSpine);
            float finalWorldHeight;

            if (absDist <= halfRoadWidth)
            {
                float normalizedDist = signedDistFromSpine / halfRoadWidth;
                float crossSectionHeight = Profile.EvaluateCrossSection(normalizedDist);
                finalWorldHeight = closestPointOnSpine.y + crossSectionHeight;
            }
            else if (absDist <= halfRoadWidth + Profile.FalloffWidth)
            {
                float normalizedFalloff = (absDist - halfRoadWidth) / Profile.FalloffWidth;
                float blendWeight = Profile.EvaluateFalloff(normalizedFalloff);
                float normalizedEdgeDist = math.sign(signedDistFromSpine);
                float edgeCrossSectionHeight = Profile.EvaluateCrossSection(normalizedEdgeDist);
                float roadEdgeHeight = closestPointOnSpine.y + edgeCrossSectionHeight;
                float originalTerrainHeight = OriginalHeights[index] * TerrainSize.y + TerrainPos.y;
                finalWorldHeight = math.lerp(originalTerrainHeight, roadEdgeHeight, blendWeight);
            }
            else
            {
                return;
            }

            Heights[index] = math.saturate((finalWorldHeight - TerrainPos.y) / TerrainSize.y);
        }
    }


    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct ModifyAlphamapsJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData Spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] public RecipeData Recipe;
        [ReadOnly] public float3 TerrainPos;
        [ReadOnly] public float3 TerrainSize;
        [ReadOnly] public int AlphamapResolution;
        [ReadOnly] public int AlphamapLayerCount;

        [ReadOnly] public NativeArray<float2> RoadContour;
        [ReadOnly] public float4 ContourBounds;

        public NativeArray<float> Alphamaps;

        // 改为统一使用 TerrainJobsUtility.IsPointInContour，以在轮廓不可用时自动回退到 AABB 粗裁剪。

        public void Execute(int index)
        {
            if (Spine.Length < 2) return;

            int y = index / AlphamapResolution;
            int x = index % AlphamapResolution;
            float2 worldPos2D = new float2(
                TerrainPos.x + (x / (float)(AlphamapResolution - 1)) * TerrainSize.x,
                TerrainPos.z + (y / (float)(AlphamapResolution - 1)) * TerrainSize.z
            );

            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour)) return;

            float min2dDistSq = float.MaxValue; int closestSegmentIndex = -1; float tClosest = 0;
            for (int i = 0; i < Spine.Points.Length - 1; i++)
            {
                float2 a = Spine.Points[i].xz; float2 b = Spine.Points[i + 1].xz;
                float2 ab = b - a; float2 ap = worldPos2D - a;
                float t = math.saturate(math.dot(ap, ab) / math.dot(ab, ab));
                float2 closest = a + t * ab; float2 diff = worldPos2D - closest; float distSq = math.dot(diff, diff);
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 closestPointOnSpine = math.lerp(Spine.Points[closestSegmentIndex], Spine.Points[closestSegmentIndex + 1], tClosest);
            float3 normal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex], Spine.Normals[closestSegmentIndex + 1], tClosest));
            float3 tangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex], Spine.Tangents[closestSegmentIndex + 1], tClosest));
            float3 right = math.normalize(math.cross(Profile.ForceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            float halfRoadWidth = Profile.RoadWidth / 2f;
            float signedDistFromCenter = math.dot(new float2(worldPos2D.x - closestPointOnSpine.x, worldPos2D.y - closestPointOnSpine.z), right.xz);
            float normalizedDist = math.saturate(math.abs(signedDistFromCenter) / (halfRoadWidth + 1e-8f));

            int baseAlphaIndex = index * AlphamapLayerCount;
            if (baseAlphaIndex < 0 || baseAlphaIndex + AlphamapLayerCount > Alphamaps.Length) return;
            for (int i = 0; i < AlphamapLayerCount; i++) Alphamaps[baseAlphaIndex + i] = 0;

            bool anyPainted = false; int firstValidSplatIndex = -1;
            for (int i = 0; i < Recipe.Length; i++)
            {
                int splatIndex = Recipe.TerrainLayerIndices[i];
                if (splatIndex < 0 || splatIndex >= AlphamapLayerCount) continue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;

                float layerMask;
                if (Recipe.MaskAtlas.IsCreated)
                {
                    layerMask = TerrainJobsUtility.SampleMaskAtlas(Recipe.MaskAtlas, Recipe.AtlasWidth, i, normalizedDist);
                }

                else
                {
                    layerMask = TerrainJobsUtility.EvaluateStrip(Recipe.Strips, Recipe.StripSlices[i], Recipe.StripResolution, normalizedDist) * Recipe.Opacities[i];
                }
                if (layerMask > 1e-6f) anyPainted = true;

                int mode = Recipe.BlendModes[i];
                int pixIdx = baseAlphaIndex + splatIndex;
                float baseValue = Alphamaps[pixIdx];
                float blended = TerrainJobsUtility.Blend(baseValue, layerMask, mode);
                Alphamaps[pixIdx] = blended;
            }

            if (!anyPainted && firstValidSplatIndex >= 0)
            {
                Alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }

            // 修改归一化逻辑：仅在命中 2 个及以上图层时才归一化，保持单图层遮罩梯度
            int paintedCount = 0;
            float total = 0f;
            for (int i = 0; i < AlphamapLayerCount; i++)
            {
                float v = Alphamaps[baseAlphaIndex + i];
                total += v;
                if (v > 1e-4f) paintedCount++;
            }
            if (paintedCount > 1 && total > 1e-5f)
            {
                float invTotal = 1f / total;
                for (int i = 0; i < AlphamapLayerCount; i++) Alphamaps[baseAlphaIndex + i] *= invTotal;
            }
            else if (paintedCount == 0 && firstValidSplatIndex >= 0)
            {
                Alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }
        }
    }
}