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

            var hmY = index / HeightmapResolution;
            var hmX = index % HeightmapResolution;
            var worldPos3D = new float3(
                TerrainPos.x + hmX / (float)(HeightmapResolution - 1) * TerrainSize.x,
                0,
                TerrainPos.z + hmY / (float)(HeightmapResolution - 1) * TerrainSize.z
            );
            var worldPos2D = worldPos3D.xz;

            // 统一使用工具方法进行裁剪：当轮廓不可用时自动退化为 AABB 粗裁剪
            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour)) return;

            var min2dDistSq = float.MaxValue;
            var closestSegmentIndex = -1;
            float tClosest = 0;
            for (var i = 0; i < Spine.Length - 1; i++)
            {
                var p1 = Spine.Points[i].xz;
                var p2 = Spine.Points[i + 1].xz;
                var segmentVec = p2 - p1;
                var segLenSq = math.lengthsq(segmentVec);
                float t;
                float distSq;
                if (segLenSq < 0.0001f)
                {
                    t = 0;
                    distSq = math.distancesq(worldPos2D, p1);
                }
                else
                {
                    t = math.saturate(math.dot(worldPos2D - p1, segmentVec) / segLenSq);
                    var c = p1 + t * segmentVec;
                    distSq = math.distancesq(worldPos2D, c);
                }

                if (distSq < min2dDistSq)
                {
                    min2dDistSq = distSq;
                    closestSegmentIndex = i;
                    tClosest = t;
                }
            }

            if (closestSegmentIndex == -1) return;

            // 计算脊线上最近点
            var closestPointOnSpine = math.lerp(Spine.Points[closestSegmentIndex],
                Spine.Points[closestSegmentIndex + 1], tClosest);

            var normal = math.normalize(math.lerp(Spine.Normals[closestSegmentIndex],
                Spine.Normals[closestSegmentIndex + 1], tClosest));
            var tangent = math.normalize(math.lerp(Spine.Tangents[closestSegmentIndex],
                Spine.Tangents[closestSegmentIndex + 1], tClosest));
            var right = math.normalize(math.cross(Profile.ForceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            var signedDistFromSpine = math.dot(worldPos3D.xz - closestPointOnSpine.xz, right.xz);
            var halfRoadWidth = Profile.RoadWidth / 2f;
            var absDist = math.abs(signedDistFromSpine);
            float finalWorldHeight;

            if (absDist <= halfRoadWidth)
            {
                var normalizedDist = signedDistFromSpine / halfRoadWidth;
                var crossSectionHeight = Profile.EvaluateCrossSection(normalizedDist);
                finalWorldHeight = closestPointOnSpine.y + crossSectionHeight;
            }
            else if (absDist <= halfRoadWidth + Profile.FalloffWidth)
            {
                var normalizedFalloff = (absDist - halfRoadWidth) / Profile.FalloffWidth;
                var blendWeight = Profile.EvaluateFalloff(normalizedFalloff);
                var normalizedEdgeDist = math.sign(signedDistFromSpine);
                var edgeCrossSectionHeight = Profile.EvaluateCrossSection(normalizedEdgeDist);
                var roadEdgeHeight = closestPointOnSpine.y + edgeCrossSectionHeight;
                var originalTerrainHeight = OriginalHeights[index] * TerrainSize.y + TerrainPos.y;
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
        [ReadOnly] private PathJobsUtility.SpineData _spine;
        [ReadOnly] public PathJobsUtility.ProfileData Profile;
        [ReadOnly] private RecipeData _recipe;
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
            if (_spine.Length < 2) return;

            var y = index / AlphamapResolution;
            var x = index % AlphamapResolution;
            var worldPos2D = new float2(
                TerrainPos.x + (x / (float)(AlphamapResolution - 1)) * TerrainSize.x,
                TerrainPos.z + (y / (float)(AlphamapResolution - 1)) * TerrainSize.z
            );

            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, ContourBounds, RoadContour)) return;

            var min2dDistSq = float.MaxValue;
            var closestSegmentIndex = -1;
            float tClosest = 0;
            for (var i = 0; i < _spine.Points.Length - 1; i++)
            {
                var a = _spine.Points[i].xz;
                var b = _spine.Points[i + 1].xz;
                var ab = b - a;
                var ap = worldPos2D - a;
                var t = math.saturate(math.dot(ap, ab) / math.dot(ab, ab));
                var closest = a + t * ab;
                var diff = worldPos2D - closest;
                var distSq = math.dot(diff, diff);
                if (distSq < min2dDistSq)
                {
                    min2dDistSq = distSq;
                    closestSegmentIndex = i;
                    tClosest = t;
                }
            }

            if (closestSegmentIndex == -1) return;

            var closestPointOnSpine = math.lerp(_spine.Points[closestSegmentIndex],
                _spine.Points[closestSegmentIndex + 1], tClosest);

            // 计算沿路径的进度 0..1
            var segCount = math.max(1, _spine.Points.Length - 1);
            var pathProgress = (closestSegmentIndex + tClosest) / segCount;
            var normal = math.normalize(math.lerp(_spine.Normals[closestSegmentIndex],
                _spine.Normals[closestSegmentIndex + 1], tClosest));
            var tangent = math.normalize(math.lerp(_spine.Tangents[closestSegmentIndex],
                _spine.Tangents[closestSegmentIndex + 1], tClosest));
            var right = math.normalize(math.cross(Profile.ForceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            var halfRoadWidth = Profile.RoadWidth / 2f;
            var signedDistFromCenter =
                math.dot(new float2(worldPos2D.x - closestPointOnSpine.x, worldPos2D.y - closestPointOnSpine.z),
                    right.xz);
            var normalizedDist = math.saturate(math.abs(signedDistFromCenter) / (halfRoadWidth + 1e-8f));

            var baseAlphaIndex = index * AlphamapLayerCount;
            if (baseAlphaIndex < 0 || baseAlphaIndex + AlphamapLayerCount > Alphamaps.Length) return;
            for (var i = 0; i < AlphamapLayerCount; i++) Alphamaps[baseAlphaIndex + i] = 0;

            var anyPainted = false;
            var firstValidSplatIndex = -1;
            for (var i = 0; i < _recipe.Length; i++)
            {
                var splatIndex = _recipe.TerrainLayerIndices[i];
                if (splatIndex < 0 || splatIndex >= AlphamapLayerCount) continue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;

                float layerMask;
                if (_recipe.MaskAtlas.IsCreated)
                {
                    layerMask = TerrainJobsUtility.SampleMaskAtlas(
                        _recipe.MaskAtlas,
                        _recipe.AtlasWidth,
                        _recipe.PathSamples,
                        i,
                        normalizedDist,
                        pathProgress);
                }

                else
                {
                    layerMask = TerrainJobsUtility.EvaluateStrip(_recipe.Strips, _recipe.StripSlices[i],
                        _recipe.StripResolution, normalizedDist) * _recipe.Opacities[i];
                }

                if (layerMask > 1e-6f) anyPainted = true;

                var mode = _recipe.BlendModes[i];
                var pixIdx = baseAlphaIndex + splatIndex;
                var baseValue = Alphamaps[pixIdx];
                var blended = TerrainJobsUtility.Blend(baseValue, layerMask, mode);
                Alphamaps[pixIdx] = blended;
            }

            if (!anyPainted && firstValidSplatIndex >= 0)
            {
                Alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }

            // 修改归一化逻辑：仅在命中 2 个及以上图层时才归一化，保持单图层遮罩梯度
            var paintedCount = 0;
            var total = 0f;
            for (var i = 0; i < AlphamapLayerCount; i++)
            {
                var v = Alphamaps[baseAlphaIndex + i];
                total += v;
                if (v > 1e-4f) paintedCount++;
            }

            if (paintedCount > 1 && total > 1e-5f)
            {
                var invTotal = 1f / total;
                for (var i = 0; i < AlphamapLayerCount; i++) Alphamaps[baseAlphaIndex + i] *= invTotal;
            }
            else if (paintedCount == 0 && firstValidSplatIndex >= 0)
            {
                Alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }
        }
    }
}