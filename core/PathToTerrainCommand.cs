using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;


namespace MrPathV2.Commands
{
    public interface ICommand { Task ExecuteAsync(); }

    /// <summary>
    /// 【最终定稿 • 大师级】将路径应用到地形的命令。
    /// 
    /// 经过最终打磨，它现在具备：
    /// - 优雅的异步流程：与Unity的异步之道完美融合。
    /// - 绝对安全的资源管理：通过 IDisposable 和 using 语句，杜绝内存泄漏。
    /// - 清晰的叙事结构：将复杂的流程拆分为易于理解的步骤。
    /// </summary>
    public class ApplyPathToTerrainCommand : ICommand
    {
        private readonly PathCreator _creator;
        private readonly TerrainHeightProvider _heightProvider;

        public ApplyPathToTerrainCommand(PathCreator creator, TerrainHeightProvider heightProvider)
        {
            _creator = creator;
            _heightProvider = heightProvider;
        }

        public async Task ExecuteAsync()
        {
            // 步骤 1: 验证与准备
            if (!ValidateInput(out var spine, out var affectedTerrains)) return;

            // 步骤 2: 封装原生数据并执行核心处理
            // 【【【 封装的艺术：`using`之美 】】】
            // 使用 'using' 语句，可以确保无论成功还是异常，所有NativeArray都会被自动Dispose。
            using (var jobData = new PathJobData(spine, _creator.profile.layers))
            {
                await ProcessTerrainsAsync(affectedTerrains, jobData);
            }

            // 步骤 3: 拼接地形接缝
            StitchTerrains(affectedTerrains);
        }

        #region 核心流程拆分 (Core Logic Breakdown)

        private bool ValidateInput(out PathSpine spine, out List<Terrain> affectedTerrains)
        {
            spine = default;
            affectedTerrains = null;

            if (_creator == null || _creator.profile == null || _creator.pathData.KnotCount < 2) return false;

            spine = PathSampler.SamplePath(_creator, _heightProvider);
            if (spine.VertexCount < 2) return false;

            affectedTerrains = FindAffectedTerrains(spine);
            return affectedTerrains.Count > 0;
        }

        private async Task ProcessTerrainsAsync(List<Terrain> terrains, PathJobData jobData)
        {
            var jobHandles = new List<JobHandle>(terrains.Count);
            var terrainJobDataList = new List<TerrainJobWorkData>(terrains.Count);

            // --- 步骤 2a: 分别为每个地形准备Job和数据 ---
            foreach (var terrain in terrains)
            {
                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Apply Path To Terrain");
                var terrainData = terrain.terrainData;
                int resolution = terrainData.heightmapResolution;
                var heights2D = terrainData.GetHeights(0, 0, resolution, resolution);

                var heightsNative = new NativeArray<float>(heights2D.Length, Allocator.TempJob);
                Copy2DTo1D(heights2D, heightsNative, resolution);

                var heightJob = new ModifyHeightsJob
                {
                    spinePoints = jobData.SpinePoints,
                    spineTangents = jobData.SpineTangents,
                    spineNormals = jobData.SpineNormals,
                    pathLayers = jobData.LayerData,
                    terrainPos = terrain.GetPosition(),
                    heightmapRes = resolution,
                    heightmapSize = new Vector2(terrainData.size.x, terrainData.size.z),
                    terrainYSize = terrainData.size.y,
                    heights = heightsNative
                };

                jobHandles.Add(heightJob.Schedule(heightsNative.Length, 256));
                terrainJobDataList.Add(new TerrainJobWorkData(terrain, heights2D, heightsNative));
            }

            // --- 步骤 2b: 统一调度与等待 ---
            var combinedHandle = JobHandle.CombineDependencies(new NativeArray<JobHandle>(jobHandles.ToArray(), Allocator.Temp));

            // 【【【 与Unity的异步之道合一 】】】
            // 不再使用Task.Run阻塞线程，而是以非阻塞的方式“轮询”Job的完成状态。
            // 这使得C#的异步流程可以与Unity的Job System无缝协作。
            while (!combinedHandle.IsCompleted)
            {
                await Task.Yield(); // 让出当前帧，下一帧再回来检查
            }
            combinedHandle.Complete(); // 保证所有Job都已完成

            // --- 步骤 2c: 将计算结果写回地形 ---
            foreach (var workData in terrainJobDataList)
            {
                Copy1DTo2D(workData.HeightsNative, workData.Heights2D, workData.Terrain.terrainData.heightmapResolution);
                workData.Terrain.terrainData.SetHeights(0, 0, workData.Heights2D);
                workData.HeightsNative.Dispose(); // 释放TempJob内存
            }
        }

        private void StitchTerrains(List<Terrain> terrains)
        {
            foreach (var terrain in terrains) terrain.Flush();
            foreach (var terrain in terrains)
            {
                terrain.SetNeighbors(terrain.leftNeighbor, terrain.topNeighbor, terrain.rightNeighbor, terrain.bottomNeighbor);
            }
        }

        #endregion

        /// <summary>
        /// 将地形的二维高度图(float[,])拷贝到一维原生数组(NativeArray)中，以供Job使用。
        /// </summary>
        private void Copy2DTo1D(float[,] source, NativeArray<float> dest, int resolution)
        {
            if (dest.Length != resolution * resolution)
            {
                Debug.LogError("Copy2DTo1D: 数组尺寸不匹配!");
                return;
            }

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // 核心转换逻辑：将二维索引(y, x)映射到一维索引(y * resolution + x)
                    dest[y * resolution + x] = source[y, x];
                }
            }
        }

        /// <summary>
        /// 将Job计算后的一维原生数组(NativeArray)结果，写回到地形的二维高度图(float[,])中。
        /// </summary>
        private void Copy1DTo2D(NativeArray<float> source, float[,] dest, int resolution)
        {
            if (source.Length != resolution * resolution)
            {
                Debug.LogError("Copy1DTo2D: 数组尺寸不匹配!");
                return;
            }

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // 核心转换逻辑：将一维索引(y * resolution + x)的结果写回到二维索引(y, x)
                    dest[y, x] = source[y * resolution + x];
                }
            }
        }

        // 一个辅助结构体，用于在处理地形时临时持有相关数据
        private readonly struct TerrainJobWorkData
        {
            public readonly Terrain Terrain;
            public readonly float[,] Heights2D;
            public readonly NativeArray<float> HeightsNative;
            public TerrainJobWorkData(Terrain t, float[,] h2d, NativeArray<float> hn)
            {
                Terrain = t; Heights2D = h2d; HeightsNative = hn;
            }
        }

        // 一个实现了IDisposable的结构体，用于统一管理Job所需的所有常驻NativeArray
        private readonly struct PathJobData : IDisposable
        {
            public readonly NativeArray<Vector3> SpinePoints;
            public readonly NativeArray<Vector3> SpineTangents;
            public readonly NativeArray<Vector3> SpineNormals;
            public readonly NativeArray<ProfileSegmentData> LayerData;

            public PathJobData(PathSpine spine, List<PathLayer> layers)
            {
                SpinePoints = new NativeArray<Vector3>(spine.points, Allocator.Persistent);
                SpineTangents = new NativeArray<Vector3>(spine.tangents, Allocator.Persistent);
                SpineNormals = new NativeArray<Vector3>(spine.surfaceNormals, Allocator.Persistent);

                LayerData = new NativeArray<ProfileSegmentData>(layers.Count, Allocator.Persistent);
                for (int i = 0; i < layers.Count; i++)
                {
                    LayerData[i] = new ProfileSegmentData
                    {
                        width = layers[i].width,
                        horizontalOffset = layers[i].horizontalOffset,
                        verticalOffset = layers[i].verticalOffset
                    };
                }
            }

            public void Dispose()
            {
                if (SpinePoints.IsCreated) SpinePoints.Dispose();
                if (SpineTangents.IsCreated) SpineTangents.Dispose();
                if (SpineNormals.IsCreated) SpineNormals.Dispose();
                if (LayerData.IsCreated) LayerData.Dispose();
            }
        }

        private List<Terrain> FindAffectedTerrains(PathSpine spine)
        {
            var terrains = new List<Terrain>();
            if (spine.VertexCount == 0) return terrains;

            Vector2 firstPoint2D = new Vector2(spine.points[0].x, spine.points[0].z);
            Rect pathBounds2D = new Rect(firstPoint2D, Vector2.zero);
            foreach (var point3D in spine.points)
            {
                pathBounds2D.xMin = Mathf.Min(pathBounds2D.xMin, point3D.x);
                pathBounds2D.yMin = Mathf.Min(pathBounds2D.yMin, point3D.z);
                pathBounds2D.xMax = Mathf.Max(pathBounds2D.xMax, point3D.x);
                pathBounds2D.yMax = Mathf.Max(pathBounds2D.yMax, point3D.z);
            }

            float maxWidth = 0;
            if (_creator.profile.layers.Count > 0)
                maxWidth = _creator.profile.layers.Max(l => l.width + Mathf.Abs(l.horizontalOffset) * 2);

            pathBounds2D = new Rect(
                pathBounds2D.x - maxWidth, pathBounds2D.y - maxWidth,
                pathBounds2D.width + maxWidth * 2, pathBounds2D.height + maxWidth * 2
            );

            foreach (var terrain in Terrain.activeTerrains)
            {
                var terrainData = terrain.terrainData;
                var terrainPos = terrain.GetPosition();
                var terrainBounds2D = new Rect(terrainPos.x, terrainPos.z, terrainData.size.x, terrainData.size.z);
                if (terrainBounds2D.Overlaps(pathBounds2D, true))
                {
                    terrains.Add(terrain);
                }
            }
            return terrains;
        }
    }
}