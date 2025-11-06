using System;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Jobs;
using MrPathV2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
// 统一使用扩展的安全释放方法
using NativeArrayExtensions = MrPathV2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace MrPathV2.Editor.Terrain
{
    /// <summary>
    ///     两阶段 CPU 绘制实现（FindRoadPixelsJob -> PaintSplatmapJob）。
    ///     - 严格遵循提前返回原则：输入、覆盖区域、RecipeData 校验。
    ///     - 单一职责：调度 Job 与数据转换分离。
    ///     - 现代风格：async/await、CancellationToken、NativeArray 安全释放。
    ///     - 与 CpuTerrainPainter 保持算法一致，修复原 CPUJobTwoPass 的语义与稳定性问题。
    /// </summary>
    public class CPUJobTwoPass : ITerrainPainter, IDisposable
    {
        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData,
            NativeArray<float2> roadContour,
            float4 contourBounds,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            // 提前返回：地形数据校验
            var td = terrain?.terrainData;
            if (td == null)
            {
                Debug.LogError("[CPUJobTwoPass] TerrainData is null.");
                return;
            }

            // 提前返回：RecipeData 校验
            if (!recipeData.IsCreated)
            {
                Debug.LogError("[CPUJobTwoPass] RecipeData is invalid!");
                return;
            }

            var resolution = td.alphamapResolution;
            var layers = td.alphamapLayers;

            // 仅处理覆盖区域，避免整张控制纹理拷贝
            var (startX, startY, numPixelsX, numPixelsY, totalPixelsInBounds) =
                CalculateRegion(td, coverageMin, coverageMax);

            // 提前返回：覆盖区域无效
            if (totalPixelsInBounds <= 0)
            {
                Debug.LogWarning("[CPUJobTwoPass] Coverage area is empty; nothing to paint.");
                return;
            }

            // 读取当前区域的 alphamaps 并转换为一维数组（Job 写入）
            var alphamaps3D = td.GetAlphamaps(startX, startY, numPixelsX, numPixelsY);
            var alphamaps1D = NativeArrayExtensions.CreateTracked<float>(alphamaps3D.Length, Allocator.Persistent);
            ConvertAlphamaps3DTo1D(alphamaps3D, alphamaps1D);

            NativeArray<RoadPixelInfo> pixelInfoMap = default;

            try
            {
                // 第一阶段：定位道路像素与参数
                pixelInfoMap = NativeArrayExtensions.CreateTracked<RoadPixelInfo>(totalPixelsInBounds, Allocator.TempJob);
                var findJob = new FindRoadPixelsJob
                {
                    Spine = spineData,
                    Profile = profileData,
                    TerrainPos = terrain.GetPosition(),
                    TerrainSize = td.size,
                    AlphamapResolution = resolution,
                    RoadContour = roadContour,
                    ContourBounds = contourBounds,
                    CoverageMin = coverageMin,
                    CoverageMax = coverageMax,
                    PixelInfoMap = pixelInfoMap
                };
                var handle1 = findJob.Schedule(totalPixelsInBounds, 128);

                // 第二阶段：按配方混合到 alphamaps
                var paintJob = new PaintSplatmapJob
                {
                    Recipe = recipeData,
                    AlphamapResolution = resolution,
                    AlphamapLayerCount = layers,
                    PixelInfoMap = pixelInfoMap,
                    CoverageMin = coverageMin,
                    CoverageMax = coverageMax,
                    Alphamaps = alphamaps1D,
                    // 与预览保持一致：启用不透明绘制时进行 AlphaClip（阈值 0.2）
                    OpaquePainting = profileData.OpaquePreview,
                    AlphaClipThreshold = profileData.OpaquePreview ? 0.2f : 0f
                };
                var combined = paintJob.Schedule(totalPixelsInBounds, 128, handle1);

                // 等待 Job 完成（可取消）
                while (!combined.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                combined.Complete();

                token.ThrowIfCancellationRequested();

                // 写回地形（主线程）
                await Task.Yield();
                ConvertAlphamaps1DTo3D(alphamaps1D, alphamaps3D);
                td.SetAlphamaps(startX, startY, alphamaps3D);
                terrain.Flush();
                EditorUtility.SetDirty(td);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[CPUJobTwoPass] Operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CPUJobTwoPass] Error during execution: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                // 安全释放 NativeArray
                alphamaps1D.SafeDispose();
                pixelInfoMap.SafeDispose();
            }
        }

        public void Dispose() { }

        #region Helpers

        // 覆盖区域计算与裁剪（提前返回前提参数整理）
        private static (int startX, int startY, int numX, int numY, int total) CalculateRegion(
            TerrainData td, Vector2Int coverageMin, Vector2Int coverageMax)
        {
            var resolution = td.alphamapResolution;

            var startX = Mathf.Clamp(coverageMin.x, 0, resolution - 1);
            var startY = Mathf.Clamp(coverageMin.y, 0, resolution - 1);
            // 半开区间：[min,max) => 宽高为差值，不 +1
            var numPixelsX = Mathf.Clamp(coverageMax.x - coverageMin.x, 0, resolution - startX);
            var numPixelsY = Mathf.Clamp(coverageMax.y - coverageMin.y, 0, resolution - startY);
            var totalPixelsInBounds = numPixelsX * numPixelsY;
            return (startX, startY, numPixelsX, numPixelsY, totalPixelsInBounds);
        }

        // 3D -> 1D 数据转换（Job 友好布局）
        private static void ConvertAlphamaps3DTo1D(float[,,] source, NativeArray<float> destination)
        {
            int h = source.GetLength(0), w = source.GetLength(1), d = source.GetLength(2);
            var idx = 0;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            for (var z = 0; z < d; z++)
            {
                destination[idx++] = source[y, x, z];
            }
        }

        // 1D -> 3D 回写
        private static void ConvertAlphamaps1DTo3D(NativeArray<float> source, float[,,] destination)
        {
            int h = destination.GetLength(0), w = destination.GetLength(1), d = destination.GetLength(2);
            if (!source.IsCreated || source.Length != w * h * d)
            {
                Debug.LogError("[CPUJobTwoPass] ConvertAlphamaps1DTo3D size mismatch!");
                return;
            }
            var idx = 0;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            for (var z = 0; z < d; z++)
            {
                destination[y, x, z] = source[idx++];
            }
        }

        #endregion
    }
}
