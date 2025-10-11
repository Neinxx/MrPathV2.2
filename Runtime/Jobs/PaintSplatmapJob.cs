using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MrPathV2
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct PaintSplatmapJob : IJobParallelFor
    {
        [ReadOnly] public PathJobsUtility.SpineData spine;
        [ReadOnly] public PathJobsUtility.ProfileData profile;
        [ReadOnly] public RecipeData recipe;
        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int alphamapResolution;
        [ReadOnly] public int alphamapLayerCount;

    [ReadOnly] public NativeArray<float2> roadContour;
    [ReadOnly] public float4 contourBounds;
        
        // 每个并行索引需要写入其对应的整段图层范围，解除并行写入限制
        [NativeDisableParallelForRestriction]
        public NativeArray<float> alphamaps; // 长度 = res*res*layers

        public void Execute(int index)
        {
            // 轮廓裁剪
            int x = index % alphamapResolution;
            int y = index / alphamapResolution;
            float2 worldPos2D = new float2(terrainPos.x + (x / (float)(alphamapResolution - 1)) * terrainSize.x,
                                           terrainPos.z + (y / (float)(alphamapResolution - 1)) * terrainSize.z);
            if (!TerrainJobsUtility.IsPointInContour(worldPos2D, contourBounds, roadContour)) return;

            // 计算相对脊线横向位置（与 ModifyHeightsJob 同理）
            float2 worldPosXZ = worldPos2D;
            float min2dDistSq = float.MaxValue; int closestSegmentIndex = -1; float tClosest = 0;
            for (int i = 0; i < spine.points.Length - 1; i++)
            {
                float2 a = spine.points[i].xz; float2 b = spine.points[i + 1].xz;
                float2 ab = b - a; float2 ap = worldPosXZ - a;
                float t = math.saturate(math.dot(ap, ab) / math.dot(ab, ab));
                float2 closest = a + t * ab; float2 diff = worldPosXZ - closest; float distSq = math.dot(diff, diff);
                if (distSq < min2dDistSq) { min2dDistSq = distSq; closestSegmentIndex = i; tClosest = t; }
            }
            if (closestSegmentIndex == -1) return;

            float3 closestPointOnSpine = math.lerp(spine.points[closestSegmentIndex], spine.points[closestSegmentIndex + 1], tClosest);
            float3 normal = math.normalize(math.lerp(spine.normals[closestSegmentIndex], spine.normals[closestSegmentIndex + 1], tClosest));
            float3 tangent = math.normalize(math.lerp(spine.tangents[closestSegmentIndex], spine.tangents[closestSegmentIndex + 1], tClosest));
            float3 right = math.normalize(math.cross(profile.forceHorizontal ? new float3(0, 1, 0) : normal, tangent));

            float halfRoadWidth = profile.roadWidth / 2f;
            float signedDistFromCenter = math.dot(new float2(worldPosXZ.x - closestPointOnSpine.x, worldPosXZ.y - closestPointOnSpine.z), right.xz);
            // 使用对称距离，确保道路两侧一致性：中心为0，边缘为1
            float normalizedDist = math.saturate(math.abs(signedDistFromCenter) / halfRoadWidth);

            int baseAlphaIndex = index * alphamapLayerCount;
            // 防御性检查：确保写入范围在缓冲区之内
            if (baseAlphaIndex < 0 || baseAlphaIndex + alphamapLayerCount > alphamaps.Length) return;

            // 将当前像素权重清零，以便完全由配方重建（道路区域内）。
            for (int i = 0; i < alphamapLayerCount; i++) alphamaps[baseAlphaIndex + i] = 0;

            // 逐配方层叠加权重（共享 Strip + Blend 算法，与预览一致）
            bool anyPainted = false;
            int firstValidSplatIndex = -1;
            for (int i = 0; i < recipe.Length; i++)
            {
                int splatIndex = recipe.terrainLayerIndices[i];
                if (splatIndex < 0 || splatIndex >= alphamapLayerCount) continue;
                if (firstValidSplatIndex == -1) firstValidSplatIndex = splatIndex;

                // 遮罩值（已包含不透明度）
                float layerMask = TerrainJobsUtility.EvaluateStrip(recipe.strips, recipe.stripSlices[i], recipe.stripResolution, normalizedDist);
                if (layerMask > 1e-6f) anyPainted = true;

                // 混合模式应用到当前像素该层值
                int mode = recipe.blendModes[i];
                int pixIdx = baseAlphaIndex + splatIndex;
                float baseValue = alphamaps[pixIdx];
                float blended = TerrainJobsUtility.Blend(baseValue, layerMask, mode);
                alphamaps[pixIdx] = blended;
            }

            // 若所有层的权重均为0，则进行保底填充：选首个有效图层权重为1
            if (!anyPainted && firstValidSplatIndex >= 0)
            {
                alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }

            // 归一化
            float total = 0; for (int i = 0; i < alphamapLayerCount; i++) total += alphamaps[baseAlphaIndex + i];
            if (total > 1e-5f)
            {
                for (int i = 0; i < alphamapLayerCount; i++) alphamaps[baseAlphaIndex + i] /= total;
            }
            else if (firstValidSplatIndex >= 0)
            {
                // 若仍为0（例如所有Gradient返回0且无保底），则将首个有效层置为1
                alphamaps[baseAlphaIndex + firstValidSplatIndex] = 1f;
            }
        }
    }
}