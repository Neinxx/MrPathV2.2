using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct SnapSpineToTerrainJob : IJobParallelFor
{
    // 输入数据 (设置为ReadOnly来提升性能)
    [ReadOnly] public NativeArray<Vector3> initialPoints;
    [ReadOnly] public float snapStrength;
    [ReadOnly] public Vector3 terrainPosition;
    [ReadOnly] public Vector2 terrainSize;
    [ReadOnly] public NativeArray<float> terrainHeights; // 将地形高度图数据传入
    [ReadOnly] public int heightmapResolution;

    // 输出数据
    public NativeArray<Vector3> snappedPoints;

    public void Execute (int index)
    {
        Vector3 worldPos = initialPoints[index];

        // 将世界坐标转换为地形的局部百分比坐标
        float normX = (worldPos.x - terrainPosition.x) / terrainSize.x;
        float normZ = (worldPos.z - terrainPosition.z) / terrainSize.y;

        // 检查点是否在地形范围内
        if (normX >= 0 && normX <= 1 && normZ >= 0 && normZ <= 1)
        {
            // 根据百分比坐标，计算在高度图上的采样坐标
            int heightmapX = Mathf.FloorToInt (normX * (heightmapResolution - 1));
            int heightmapY = Mathf.FloorToInt (normZ * (heightmapResolution - 1));

            // 从高度图数据中读取归一化的高度值
            float normalizedHeight = terrainHeights[heightmapY * heightmapResolution + heightmapX];

            // 计算世界空间中的地形高度
            float terrainHeight = normalizedHeight * terrainSize.y + terrainPosition.y;

            Vector3 snappedPos = new Vector3 (worldPos.x, terrainHeight, worldPos.z);
            snappedPoints[index] = Vector3.Lerp (worldPos, snappedPos, snapStrength);
        }
        else
        {
            // 如果点在地形外，则不进行吸附
            snappedPoints[index] = worldPos;
        }
    }
}
