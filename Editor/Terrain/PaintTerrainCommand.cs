using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Editor.GPU;
using MrPathV2.Editor.Settings;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Terrain
{
    /// <summary>
    ///     绘制纹理命令：桥接旧命令入口与新统一绘制架构。
    ///     - 遵循提前返回与单一职责：输入校验、层解析、绘制执行解耦。
    ///     - 支持 CPU/GPU/Auto 后端选择（项目高级设置）。
    ///     - 针对每块地形仅处理覆盖区域，避免全贴图拷贝。
    /// </summary>
    public sealed class PaintTerrainCommand : TerrainCommandBase
    {
        // 供设置与 UI 引用的后端枚举（保持名称兼容）
        public enum PaintingBackend
        {
            CPUCompute = 0,
            GPUCompute = 1,
            Auto = 2
        }

        public PaintTerrainCommand(PathCreator creator, IHeightProvider heightProvider)
            : base(creator, heightProvider) { }

        public override string GetCommandName() => "绘制纹理 (Paint Terrain)";

        protected override async Task ProcessTerrainsAsync(List<UnityEngine.Terrain> terrains, PathSpine spine, CancellationToken token)
        {
            // 提前返回：无可用地形
            if (terrains == null || terrains.Count == 0) return;

            // 采样数据准备（一次生成，多处复用）
            var spineData = new PathJobsUtility.SpineData(spine, Allocator.Persistent);
            var profileData = new PathJobsUtility.ProfileData(Creator.profile, Allocator.Persistent);
            RoadContourGenerator.GenerateContour(spine, Creator.profile, out var roadContour, out var contourBounds, Allocator.Persistent);

            // 使用预览包围盒或退化包围盒优化覆盖区域计算
            if (PreferredBoundsXZ.HasValue)
            {
                var pb = PreferredBoundsXZ.Value;
                contourBounds = new float4(pb.x, pb.y, pb.z, pb.w);
            }
            else if (!roadContour.IsCreated || roadContour.Length < 3)
            {
                var fb = GetExpandedXZBounds(spine, Creator.profile);
                contourBounds = new float4(fb.x, fb.y, fb.z, fb.w);
            }

            try
            {
                // 统一弹窗：是否将缺失的 TerrainLayer 添加到所有目标地形
                var layerMaps = ResolveLayersWithConfirm(terrains, Creator.profile);
                if (layerMaps == null) return; // 用户取消

                // 逐地形执行绘制
                foreach (var terrain in terrains)
                {
                    token.ThrowIfCancellationRequested();
                    if (!terrain || !terrain.terrainData) continue;

                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, GetCommandName());

                    // 覆盖区域（基于轮廓或退化 AABB），映射到 alphamap 像素坐标
                    CalculateCoverageOnTerrain(terrain, contourBounds, out var covMin, out var covMax, out var pixelCount);
                    if (pixelCount <= 0) continue; // 提前返回：无覆盖区域

                    // 构建 RecipeData（与目标地形图层映射一致）
                    var layerMap = layerMaps.TryGetValue(terrain, out var m) ? m : BuildTerrainLayerMap(terrain);
                    using var recipeData = BuildRecipeData(Creator.profile, layerMap, spine);

                    // 选择并执行后端
                    using var painter = SelectPainter(terrain, covMin, covMax, pixelCount);
                    await painter.ExecuteAsync(
                        terrain,
                        spineData,
                        profileData,
                        recipeData,
                        roadContour,
                        contourBounds,
                        covMin,
                        covMax,
                        token);

                    // 写脏与刷新
                    terrain.Flush();
                    EditorUtility.SetDirty(terrain.terrainData);
                }
            }
            finally
            {
                // 资源清理
                if (spineData.IsCreated) spineData.Dispose();
                if (profileData.IsCreated) profileData.Dispose();
                if (roadContour.IsCreated) roadContour.Dispose();
                HeightProvider?.MarkAsDirty();
            }
        }

        // 单次确认与提前返回：汇总所有地形的缺失层，统一确认一次
        private static Dictionary<UnityEngine.Terrain, Dictionary<TerrainLayer, int>> ResolveLayersWithConfirm(
            List<UnityEngine.Terrain> terrains, PathProfile pathProfile)
        {
            var result = new Dictionary<UnityEngine.Terrain, Dictionary<TerrainLayer, int>>();
            if (terrains == null || terrains.Count == 0 || !pathProfile.roadRecipe) return result;
            var recipe = pathProfile.roadRecipe;
            // 收集缺失层（任一地形缺失即计入）
            var anyMissing = false;
            foreach (var t in terrains)
            {
                var map = LayerResolver.ResolveSmart(t, recipe);
                var recipeLayers = recipe.GetLayers();
                foreach (var rl in recipeLayers)
                {
                    // 提前返回：无效条目不参与检测
                    if (rl == null || !rl.contentLayer) continue;
                    if (!map.ContainsKey(rl.contentLayer))
                    {
                        anyMissing = true;
                        break;
                    }
                }
                if (anyMissing) break;
            }

            // 无缺失：直接智能映射（非交互）
            if (!anyMissing)
            {
                foreach (var t in terrains)
                {
                    result[t] = LayerResolver.ResolveSmart(t, recipe);
                }
                return result;
            }

            // 统一一次确认
            var choice = EditorUtility.DisplayDialogComplex(
                "缺失地形图层",
                "检测到配方引用的 TerrainLayer 在部分地形中缺失。\n\n是否将缺失图层添加到所有目标地形？",
                "添加到所有地形",
                "仅匹配等价图层",
                "取消");

            if (choice == 2) return null; // 取消

            var addMissing = choice == 0;
            foreach (var t in terrains)
            {
                result[t] = addMissing
                    ? LayerResolver.ResolveEnsurePresentSmart(t, pathProfile)
                    : LayerResolver.ResolveSmart(t, recipe);
            }
            return result;
        }

        // 覆盖区域：将世界空间 XZ AABB 映射到 alphamap 像素范围
        private static void CalculateCoverageOnTerrain(UnityEngine.Terrain terrain, float4 boundsXZ,
            out Vector2Int coverageMin, out Vector2Int coverageMax, out int pixelCount)
        {
            var td = terrain.terrainData;
            var res = td.alphamapResolution;
            var pos = terrain.GetPosition();
            var size = td.size;

            // 映射到 [0, res]（半开区间）：与GPU端保持一致
            float ToPixelX(float x)
            {
                return (x - pos.x) / size.x * res;
            }

            float ToPixelY(float z)
            {
                return (z - pos.z) / size.z * res;
            }

            var minX = Mathf.FloorToInt(Mathf.Min(ToPixelX(boundsXZ.x), ToPixelX(boundsXZ.z)));
            var maxX = Mathf.CeilToInt(Mathf.Max(ToPixelX(boundsXZ.x), ToPixelX(boundsXZ.z)));
            var minY = Mathf.FloorToInt(Mathf.Min(ToPixelY(boundsXZ.y), ToPixelY(boundsXZ.w)));
            var maxY = Mathf.CeilToInt(Mathf.Max(ToPixelY(boundsXZ.y), ToPixelY(boundsXZ.w)));

            // 裁剪至有效范围（独占上界）：max 允许等于 res
            minX = Mathf.Clamp(minX, 0, res);
            maxX = Mathf.Clamp(maxX, 0, res);
            minY = Mathf.Clamp(minY, 0, res);
            maxY = Mathf.Clamp(maxY, 0, res);

            coverageMin = new Vector2Int(minX, minY);
            coverageMax = new Vector2Int(maxX, maxY);
            // 采用半开区间：[min,max) => 宽高为差值，不 +1
            pixelCount = Mathf.Max(0, (maxX - minX) * (maxY - minY));
        }

        // 选择 CPU 或 GPU 实现（遵循项目高级设置；Auto 模式按像素阈值与硬件能力切换）
        private static ITerrainPainter SelectPainter(UnityEngine.Terrain terrain, Vector2Int covMin, Vector2Int covMax, int pixelCount)
        {
            var settings = MrPathProjectSettings.GetOrCreateSettings();
            var adv = settings != null ? settings.advancedSettings : null;
            var backend = adv != null ? adv.paintingBackend : PaintingBackend.CPUCompute;
            var threshold = adv != null ? Mathf.Max(1, adv.gpuAutoSwitchThreshold) : 50000; // 默认阈值

            bool UseGpu()
            {
                if (!SystemInfo.supportsComputeShaders) return false;
                if (backend == PaintingBackend.GPUCompute) return true;
                if (backend == PaintingBackend.CPUCompute) return false;
                // Auto：按覆盖像素与硬件能力
                return pixelCount >= threshold;
            }

            return UseGpu() ? GpuTerrainPainterV2.Instance : new CPUJobTwoPass();
        }

        // 配方数据：根据地形图层映射，计算道路宽与近似长度
        private static RecipeData BuildRecipeData(PathProfile pathProfile, Dictionary<TerrainLayer, int> map, PathSpine spine)
        {
            var width = Mathf.Max(0.01f, pathProfile ? pathProfile.roadWidth > 0 ? pathProfile.roadWidth : 0f : 0f);
            // 道路总长度（沿骨架）
            var length = 0f;
            for (var i = 1; i < spine.VertexCount; i++)
            {
                length += Vector3.Distance(spine.Points[i - 1], spine.Points[i]);
            }

            // 若未从 recipe 提供宽度覆盖，则用 Profile 宽度
            if (width <= 0.0001f)
                width = Mathf.Max(0.01f, map != null ? 1f : 1f); // 宽度参与遮罩采样，非 0 即可

            var threshold = pathProfile != null && pathProfile.opaquePreview ? 0.2f : 0f;
            return new RecipeData(pathProfile, map, width, length, Allocator.Persistent, threshold);
        }
    }
}
