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
        [ReadOnly] public NativeArray<float3> meshVertices;
        [ReadOnly] public NativeArray<int> meshTriangles;

        [ReadOnly] public float3 terrainPos;
        [ReadOnly] public float3 terrainSize;
        [ReadOnly] public int heightmapResolution;
        [ReadOnly] public NativeArray<float2> roadContour;
        [ReadOnly] public float4 contourBounds;

        public NativeArray<float> heights;

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

        /// <summary>
        /// 【完整实现】计算一个点是否在一个2D三角形内部。
        /// </summary>
        private bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;

            if ((s < 0) != (t < 0) && s != 0 && t != 0)
                return false;

            float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;

            return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
        }

        /// <summary>
        /// 手动实现的重心坐标计算函数。
        /// </summary>
        private float3 Barycentric(float2 a, float2 b, float2 c, float2 p)
        {
            float2 v0 = b - a;
            float2 v1 = c - a;
            float2 v2 = p - a;

            float d00 = math.dot(v0, v0);
            float d01 = math.dot(v0, v1);
            float d11 = math.dot(v1, v1);
            float d20 = math.dot(v2, v0);
            float d21 = math.dot(v2, v1);

            float denom = d00 * d11 - d01 * d01;

            if (math.abs(denom) < 0.0001f)
            {
                return new float3(-1, -1, -1);
            }

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1.0f - v - w;

            return new float3(u, v, w);
        }

        public void Execute(int index)
        {
            int hmY = index / heightmapResolution;
            int hmX = index % heightmapResolution;
            float3 worldPos3D = new float3(
                terrainPos.x + hmX / (float)(heightmapResolution - 1) * terrainSize.x,
                0,
                terrainPos.z + hmY / (float)(heightmapResolution - 1) * terrainSize.z
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

            for (int i = 0; i < meshTriangles.Length; i += 3)
            {
                float3 vA_3D = meshVertices[meshTriangles[i]];
                float3 vB_3D = meshVertices[meshTriangles[i + 1]];
                float3 vC_3D = meshVertices[meshTriangles[i + 2]];

                float2 vA_2D = vA_3D.xz;
                float2 vB_2D = vB_3D.xz;
                float2 vC_2D = vC_3D.xz;

                if (IsPointInTriangle(worldPos2D, vA_2D, vB_2D, vC_2D))
                {
                    float3 barycentricCoords = Barycentric(vA_2D, vB_2D, vC_2D, worldPos2D);
                    float finalWorldHeight = vA_3D.y * barycentricCoords.x + vB_3D.y * barycentricCoords.y + vC_3D.y * barycentricCoords.z;
                    float normalizedHeight = math.saturate((finalWorldHeight - terrainPos.y) / terrainSize.y);
                    heights[index] = normalizedHeight;
                    return;
                }
            }
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

            int finalBaseAlphaIndex = index * alphamapLayerCount;
            float totalWeight = 0;
            for (int i = 0; i < alphamapLayerCount; i++) { totalWeight += alphamaps[finalBaseAlphaIndex + i]; }
            if (totalWeight > 0.001f) { for (int i = 0; i < alphamapLayerCount; i++) { alphamaps[finalBaseAlphaIndex + i] /= totalWeight; } }
        }
    }
}