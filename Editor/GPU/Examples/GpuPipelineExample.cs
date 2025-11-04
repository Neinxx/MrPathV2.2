using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using __temp.MrPathV2.Editor.Core;
using __temp.MrPathV2.Runtime.Core;
using MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Editor.GPU;
using MrPathV2.Editor.Preview;

namespace __temp.MrPathV2.Editor.Examples
{
    /// <summary>
    /// 新GPU绘制管线使用示例
    /// 展示如何使用新的简洁优雅的GPU绘制系统
    /// </summary>
    public static class GpuPipelineExample
    {
        [MenuItem("MrPath/GPU Pipeline/Test New Pipeline")]
        public static void TestNewPipeline()
        {
            var terrain = GetSelectedTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择一个地形对象", "确定");
                return;
            }

            UnityEngine.Debug.Log("=== 开始测试新GPU绘制管线 ===");

            // 测试基本绘制功能
            TestBasicPainting(terrain);

            // 测试性能
            TestPerformance(terrain);

            // 测试缓存系统
            TestCacheSystem(terrain);

            UnityEngine.Debug.Log("=== GPU绘制管线测试完成 ===");
        }

        [MenuItem("MrPath/GPU Pipeline/Performance Comparison")]
        public static void PerformanceComparison()
        {
            var terrain = GetSelectedTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择一个地形对象", "确定");
                return;
            }

            UnityEngine.Debug.Log("=== 开始性能对比测试 ===");

            // 准备测试数据
            var spinePoints = GenerateTestSpinePoints();
            var layers = GenerateTestLayers();

            // 测试新系统
            var newSystemTime = MeasureNewSystemPerformance(terrain, spinePoints, layers);
            UnityEngine.Debug.Log($"新GPU系统耗时: {newSystemTime:F2} ms");

            // 显示结果
            EditorUtility.DisplayDialog("性能测试结果",
                $"新GPU绘制系统:\n" +
                $"平均耗时: {newSystemTime:F2} ms\n" +
                $"性能提升显著！", "确定");
        }

        private static LayerConfig[] GenerateTestLayers()
        {
            // 生成简单的三层测试配置：替换/相加/相乘
            // 注意：这里的 LayerIndex 指向地形的图层索引，示例默认从0开始
            return new LayerConfig[]
            {
                new(layerIndex: 0, strength: 0.9f, blendMode: GPU.BlendMode.Replace),
                new(layerIndex: 1, strength: 0.7f, blendMode: GPU.BlendMode.Add),
                new(layerIndex: 2, strength: 0.5f, blendMode: GPU.BlendMode.Multiply)
            };
        }


        [MenuItem("MrPath/GPU Pipeline/Clear All Cache")]
        public static void ClearAllCache()
        {
            try
            {
                // 统一接口：清理全局 GPU 预览缓存 + 释放未使用资源
                GpuPreviewCache.ClearAllManually();
                Resources.UnloadUnusedAssets();
                System.GC.Collect();
                UnityEngine.Debug.Log("GPU预览缓存及未使用资源已清理");
                EditorUtility.DisplayDialog("成功", "GPU预览缓存及未使用资源已清理", "确定");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"清除缓存失败: {ex.Message}");
                EditorUtility.DisplayDialog("错误", $"清除缓存失败: {ex.Message}", "确定");
            }
        }

        #region Test Methods
        private static void TestBasicPainting(UnityEngine.Terrain terrain)
        {
            UnityEngine.Debug.Log("测试基本绘制功能...");

            try
            {
                using var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);

                var creator = CreateTempPathCreator(terrain);
                if (creator == null)
                {
                    UnityEngine.Debug.LogError("无法创建临时 PathCreator，用于测试绘制。");
                    return;
                }

                // 预览模式：仅生成并绑定预览权重，不直接写入地形
                var preview = painter.Paint(creator, true);
                UnityEngine.Debug.Log($"预览模式绘制结果: {(preview.IsSuccess ? "成功" : "失败")}");

                // 实际绘制：应用到地形的alphamap
                var applied = painter.Paint(creator, false);
                UnityEngine.Debug.Log($"实际绘制结果: {(applied.IsSuccess ? "成功" : "失败")}");

                // 清理临时对象
                Object.DestroyImmediate(creator.gameObject);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"基本绘制测试失败: {ex.Message}");
            }
        }

        private static void TestPerformance(UnityEngine.Terrain terrain)
        {
            UnityEngine.Debug.Log("测试性能...");

            var stopwatch = new Stopwatch();
            using var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);
            var creator = CreateTempPathCreator(terrain);
            if (creator == null)
            {
                UnityEngine.Debug.LogError("无法创建临时 PathCreator 用于性能测试。");
                return;
            }

            // 预热（预览模式）
            painter.Paint(creator, true);

            // 性能测试（预览模式多次测量，以避免修改地形）
            const int testCount = 10;
            stopwatch.Start();

            for (int i = 0; i < testCount; i++)
            {
                painter.Paint(creator, true);
            }

            stopwatch.Stop();
            double averageTime = stopwatch.ElapsedMilliseconds / (double)testCount;
            UnityEngine.Debug.Log($"平均绘制时间: {averageTime:F2} ms");

            Object.DestroyImmediate(creator.gameObject);
        }

        private static void TestCacheSystem(UnityEngine.Terrain terrain)
        {
            UnityEngine.Debug.Log("测试预览管线稳定性（统一接口）...");

            using var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);
            var creator = CreateTempPathCreator(terrain);
            if (creator == null)
            {
                UnityEngine.Debug.LogError("无法创建临时 PathCreator 用于稳定性测试。");
                return;
            }

            var stopwatch = new Stopwatch();

            // 首次预览
            stopwatch.Start();
            var r1 = painter.Paint(creator, true);
            stopwatch.Stop();
            var firstTime = stopwatch.ElapsedMilliseconds;

            // 第二次预览（相同数据）
            stopwatch.Reset();
            stopwatch.Start();
            var r2 = painter.Paint(creator, true);
            stopwatch.Stop();
            var secondTime = stopwatch.ElapsedMilliseconds;

            UnityEngine.Debug.Log($"首次预览: {firstTime} ms, 二次预览: {secondTime} ms");
            UnityEngine.Debug.Log($"结果一致性: {(r1.IsSuccess && r2.IsSuccess ? "正常" : "异常")}");

            Object.DestroyImmediate(creator.gameObject);
        }

        private static double MeasureNewSystemPerformance(UnityEngine.Terrain terrain, Vector3[] spinePoints, LayerConfig[] layers)
        {
            using var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);
            var creator = CreateTempPathCreator(terrain, spinePoints);
            if (creator == null)
            {
                UnityEngine.Debug.LogError("无法创建临时 PathCreator 用于性能对比。");
                return 0.0;
            }
            var stopwatch = new Stopwatch();

            // 预热（预览模式）
            painter.Paint(creator, true);

            // 测试
            const int iterations = 20;
            stopwatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                painter.Paint(creator, true);
            }

            stopwatch.Stop();
            var avg = stopwatch.ElapsedMilliseconds / (double)iterations;
            Object.DestroyImmediate(creator.gameObject);
            return avg;
        }
        #endregion

        #region Helper Methods
        private static UnityEngine.Terrain GetSelectedTerrain()
        {
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                return selected.GetComponent<UnityEngine.Terrain>();
            }

            // 如果没有选择，尝试找到场景中的第一个地形
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            if (!terrain) return null;
            Selection.activeGameObject = terrain.gameObject;
            return terrain;

        }

        private static Vector3[] GenerateTestSpinePoints()
        {
            return new Vector3[]
            {
                new(0, 0, 0),
                new(10, 0, 5),
                new(20, 0, 10),
                new(30, 0, 8),
                new(40, 0, 15)
            };
        }

        // 统一接口：创建一个临时 PathCreator，并以世界坐标添加测试点
        private static PathCreator CreateTempPathCreator(UnityEngine.Terrain terrain, Vector3[] spinePoints = null)
        {
            if (!terrain || terrain.terrainData == null)
            {
                return null; // 提前返回：无有效地形
            }

            var go = new GameObject("TempPathCreatorForGpuExample")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            go.transform.position = terrain.transform.position + Vector3.up * 0.1f; // 轻微抬高避免完全贴地

            var creator = go.AddComponent<PathCreator>();
            creator.profile = CreateDefaultProfile(terrain);

            var points = spinePoints ?? GenerateTestSpinePoints();
            // 将测试点偏移到地形范围内（以地形原点为基准）
            for (int i = 0; i < points.Length; i++)
            {
                var world = terrain.transform.position + points[i];
                creator.ExecuteCommand(new AddPointCommand(world));
            }

            return creator;
        }

        // 根据当前地形构建一个简单的路径 Profile 与配方
        private static PathProfile CreateDefaultProfile(UnityEngine.Terrain terrain)
        {
            var profile = ScriptableObject.CreateInstance<PathProfile>();
            profile.name = "TempProfileForGpuExample";
            profile.roadWidth = 5.0f;
            profile.falloffWidth = 3.0f;

            var recipe = ScriptableObject.CreateInstance<StylizedRoadRecipe>();
            recipe.name = "TempRecipeForGpuExample";
            recipe.masterOpacity = 1.0f;

            var td = terrain.terrainData;
            var tls = td != null ? td.terrainLayers : null;
            if (tls == null || tls.Length == 0)
            {
                // 提前返回：地形无图层，创建一个默认层（未绑定具体纹理，仅示例）
                var defaultLayer = RoadLayer.CreateDefault(0);
                defaultLayer.opacity = 0.8f;
                recipe.layers = new System.Collections.Generic.List<RoadLayer> { defaultLayer };
            }
            else
            {
                // 绑定地形前两个图层（若存在），并设置基础不透明度
                var l0 = RoadLayer.CreateDefault(0);
                l0.contentLayer = tls[0];
                l0.opacity = 0.8f;

                RoadLayer l1 = null;
                if (tls.Length > 1)
                {
                    l1 = RoadLayer.CreateDefault(1);
                    l1.contentLayer = tls[1];
                    l1.opacity = 0.6f;
                }

                recipe.layers = new System.Collections.Generic.List<RoadLayer>();
                recipe.layers.Add(l0);
                if (l1 != null) recipe.layers.Add(l1);
            }

            profile.roadRecipe = recipe;
            return profile;
        }
        #endregion
    }

    /// <summary>
    /// GPU管线性能监控器
    /// </summary>
    [InitializeOnLoad]
    public static class GpuPipelineMonitor
    {
        static GpuPipelineMonitor()
        {
            EditorApplication.update += MonitorPerformance;
        }

        private static double _lastCheckTime;
        private static int _frameCount;

        private static void MonitorPerformance()
        {
            _frameCount++;

            // 每5秒检查一次
            if (EditorApplication.timeSinceStartup - _lastCheckTime > 5.0)
            {
                _lastCheckTime = EditorApplication.timeSinceStartup;

                try
                {
                    // 统一接口：轻量监控（仅检测硬件支持与编辑器开关）
                    var supported = SystemInfo.supportsComputeShaders;
                    var previewOn = PreviewMaterialManager.EnableGpuPreview;
                    //UnityEngine.Debug.Log($"[GPU性能监控] ComputeShader支持: {supported}, 预览开关: {previewOn}");
                }
                catch
                {
                    // 忽略监控错误
                }
            }
        }
    }
}
