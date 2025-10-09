

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【终极纯净版】一个Burst编译的Job，用于高效地修改地形高度图以匹配路径。
    /// - 彻底移除所有边界守护神通，专注核心逻辑。
    /// - 采用平滑羽化算法，确保路径与地形自然融合。
    /// </summary>
    [BurstCompile]
    public struct ModifyHeightsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> spinePoints;
        [ReadOnly] public NativeArray<Vector3> spineTangents;
        [ReadOnly] public NativeArray<Vector3> spineNormals;
        [ReadOnly] public NativeArray<ProfileSegmentData> pathLayers;

        [ReadOnly] public Vector3 terrainPos;
        [ReadOnly] public int heightmapRes;
        [ReadOnly] public Vector2 heightmapSize;
        [ReadOnly] public float terrainYSize;

        public NativeArray<float> heights;

        public void Execute(int index)
        {
            if (pathLayers.Length == 0 || spinePoints.Length < 2) return;

            // 1. 计算当前地形点的世界坐标 (X, Z)
            int y_coord = index / heightmapRes;
            int x_coord = index % heightmapRes;
            float normX = x_coord / (float)(heightmapRes - 1);
            float normZ = y_coord / (float)(heightmapRes - 1);
            Vector2 worldPos2D = new Vector2(normX * heightmapSize.x + terrainPos.x, normZ * heightmapSize.y + terrainPos.z);

            // 2. 寻找最近线段
            float minSqrDist2D = float.MaxValue;
            int closestSegmentIndex = -1;
            for (int i = 0; i < spinePoints.Length - 1; i++)
            {
                Vector2 p1 = new Vector2(spinePoints[i].x, spinePoints[i].z);
                Vector2 p2 = new Vector2(spinePoints[i + 1].x, spinePoints[i + 1].z);
                Vector2 nearestPt2D = FindNearestPointOnLineSegment2D(p1, p2, worldPos2D);
                float sqrDist2D = (worldPos2D - nearestPt2D).sqrMagnitude;
                if (sqrDist2D < minSqrDist2D)
                {
                    minSqrDist2D = sqrDist2D;
                    closestSegmentIndex = i;
                }
            }
            if (closestSegmentIndex == -1) return;

            // 【核心修正：移除边界杂念】
            // 之前在此处有一个判断路径点是否在地形内的守护神通，但它思虑不周，反成心魔，导致了孔洞。
            // FindAffectedTerrains 已为我们精准筛选，此处无需再判，只需专注核心演算。

            // 3. 精确插值
            Vector3 p_start = spinePoints[closestSegmentIndex];
            Vector3 p_end = spinePoints[closestSegmentIndex + 1];
            Vector2 p_start_2D = new Vector2(p_start.x, p_start.z);
            Vector2 p_end_2D = new Vector2(p_end.x, p_end.z);
            Vector2 pointOnSpine2D = FindNearestPointOnLineSegment2D(p_start_2D, p_end_2D, worldPos2D);
            float segmentLength2D = (p_end_2D - p_start_2D).magnitude;
            float t = segmentLength2D > 0.001f ? (pointOnSpine2D - p_start_2D).magnitude / segmentLength2D : 0;
            Vector3 pointOnSpine = Vector3.Lerp(p_start, p_end, t);
            Vector3 tangent = Vector3.Slerp(spineTangents[closestSegmentIndex], spineTangents[closestSegmentIndex + 1], t);
            Vector3 normal = Vector3.Slerp(spineNormals[closestSegmentIndex], spineNormals[closestSegmentIndex + 1], t);
            Vector3 right = Vector3.Cross(tangent, normal).normalized;

            // 4. 计算横截面位置
            Vector3 worldPos3D = new Vector3(worldPos2D.x, pointOnSpine.y, worldPos2D.y);
            Vector3 vecToPoint = worldPos3D - pointOnSpine;
            float lateralDist = Vector3.Dot(vecToPoint, right);

            // 5. 计算最终高度
            float finalHeight = -1f;
            float originalHeight = heights[index] * terrainYSize + terrainPos.y;
            bool isCovered = false;
            for (int i = 0; i < pathLayers.Length; i++)
            {
                ProfileSegmentData layer = pathLayers[i];
                float halfWidth = layer.width / 2f;
                if (Mathf.Abs(lateralDist - layer.horizontalOffset) <= halfWidth)
                {
                    finalHeight = Mathf.Max(finalHeight, pointOnSpine.y + layer.verticalOffset);
                    isCovered = true;
                }
            }

            // 6. 应用高度与羽化
            if (isCovered)
            {
                float maxPathHalfWidth = 0;
                for (int i = 0; i < pathLayers.Length; i++)
                    maxPathHalfWidth = Mathf.Max(maxPathHalfWidth, pathLayers[i].width / 2f + Mathf.Abs(pathLayers[i].horizontalOffset));
                if (maxPathHalfWidth > 0)
                {
                    float falloffRatio = 0.25f;
                    float falloffStartDist = maxPathHalfWidth * (1 - falloffRatio);
                    float dist2D = Mathf.Sqrt(minSqrDist2D);
                    float blendFactor = 1f;
                    if (dist2D > falloffStartDist)
                        blendFactor = Mathf.InverseLerp(maxPathHalfWidth, falloffStartDist, dist2D);
                    float blendedHeight = Mathf.Lerp(originalHeight, finalHeight, blendFactor);
                    heights[index] = (blendedHeight - terrainPos.y) / terrainYSize;
                }
            }
        }

        private Vector2 FindNearestPointOnLineSegment2D(Vector2 start, Vector2 end, Vector2 point)
        {
            Vector2 lineDir = end - start;
            float lineLengthSqr = lineDir.sqrMagnitude;
            if (lineLengthSqr < 0.00001f) return start;
            float t = Vector2.Dot(point - start, lineDir) / lineLengthSqr;
            t = Mathf.Clamp01(t);
            return start + lineDir * t;
        }
    }
}