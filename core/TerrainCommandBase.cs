// TerrainCommandBase.cs (内存安全版)
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using System.Linq;

public abstract class TerrainCommandBase
{
    protected readonly PathCreator Creator;
    protected readonly TerrainHeightProvider HeightProvider;

    protected TerrainCommandBase(PathCreator creator, TerrainHeightProvider heightProvider)
    {
        Creator = creator;
        HeightProvider = heightProvider;
    }

    public abstract string GetCommandName();

    public async Task ExecuteAsync()
    {
        if (!Validate(out var spine, out var terrains)) return;

        // 【修正】使用 Allocator.Persistent 来避免 TempJob 的4帧超时问题
        RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);
        var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);

        try
        {
            await ProcessTerrains(terrains, spineData, roadContour, contourBounds);
        }
        finally
        {
            // 依赖我们强大的 try-finally 结构来确保内存被释放
            if (spineData.IsCreated) spineData.Dispose();
            if (roadContour.IsCreated) roadContour.Dispose();
        }

        StitchTerrains(terrains);
        Debug.Log($"[Mr. Path] {GetCommandName()} 操作已成功应用到地形！");
    }

    protected abstract Task ProcessTerrains(List<Terrain> terrains, PathJobsUtility.SpineData spineData, NativeArray<float2> roadContour, float4 contourBounds);

    // ... FindAffectedTerrains 和其他所有辅助方法都保持不变 ...
    protected bool Validate(out PathSpine spine, out List<Terrain> affectedTerrains)
    {
        spine = default; affectedTerrains = null;
        if (Creator == null || Creator.profile == null || Creator.pathData.KnotCount < 2) { Debug.LogError("路径无效或未配置 Profile。"); return false; }
        spine = PathSampler.SamplePath(Creator, HeightProvider);
        if (spine.VertexCount < 2) { Debug.LogWarning("路径采样点不足，无法应用。"); return false; }
        affectedTerrains = FindAffectedTerrains(spine);
        if (affectedTerrains.Count == 0) { return false; }
        return true;
    }
    private List<Terrain> FindAffectedTerrains(PathSpine spine)
    {
        Bounds projectedBounds = GetProjectedSpineBounds(spine);
        var affectedTerrains = new List<Terrain>();
        foreach (var terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Bounds terrainBounds = new Bounds(terrain.GetPosition() + terrain.terrainData.size / 2f, terrain.terrainData.size);
            if (projectedBounds.Intersects(terrainBounds)) { affectedTerrains.Add(terrain); }
        }
        return affectedTerrains;
    }
    private Bounds GetProjectedSpineBounds(PathSpine spine)
    {
        if (spine.VertexCount == 0) return new Bounds();
        var pathBounds = new Bounds(spine.points[0], Vector3.zero);
        for (int i = 1; i < spine.VertexCount; i++) { pathBounds.Encapsulate(spine.points[i]); }
        float maxExtent = 0;
        if (Creator.profile != null && Creator.profile.layers.Count > 0) { maxExtent = Creator.profile.layers.Max(l => Mathf.Abs(l.horizontalOffset) + l.width / 2f); }
        float totalExpansion = maxExtent + 5f;
        pathBounds.Expand(new Vector3(totalExpansion * 2, 0, totalExpansion * 2));
        return new Bounds(new Vector3(pathBounds.center.x, pathBounds.center.y, pathBounds.center.z), new Vector3(pathBounds.size.x, float.MaxValue, pathBounds.size.z));
    }
    protected Dictionary<TerrainLayer, int> BuildTerrainLayerMap(Terrain terrain)
    {
        var terrainLayers = terrain.terrainData.terrainLayers;
        var layerToIndexMap = new Dictionary<TerrainLayer, int>();
        for (int i = 0; i < terrainLayers.Length; i++) { if (terrainLayers[i] != null) layerToIndexMap[terrainLayers[i]] = i; }
        return layerToIndexMap;
    }
    private void StitchTerrains(List<Terrain> terrains)
    {
        foreach (var t in terrains) t.Flush();
        foreach (var t in terrains) t.SetNeighbors(t.leftNeighbor, t.topNeighbor, t.rightNeighbor, t.bottomNeighbor);
    }
}