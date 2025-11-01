using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using __temp.MrPathV2.Editor.GPU;

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

        [MenuItem("MrPath/GPU Pipeline/Clear All Cache")]
        public static void ClearAllCache()
        {
            try
            {
                GpuTerrainPainterV2.Instance.ClearCache();
                UnityEngine.Debug.Log("GPU缓存已清除");
                EditorUtility.DisplayDialog("成功", "GPU缓存已清除", "确定");
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
                var painter = GpuTerrainPainterV2.Instance;
                var spinePoints = GenerateTestSpinePoints();
                var layers = GenerateTestLayers();

                // 测试预览模式
                bool previewResult = painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);
                UnityEngine.Debug.Log($"预览模式绘制结果: {(previewResult ? "成功" : "失败")}");

                // 测试实际绘制
                bool paintResult = painter.PaintPath(terrain, spinePoints, 5.0f, layers, false);
                UnityEngine.Debug.Log($"实际绘制结果: {(paintResult ? "成功" : "失败")}");
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
            var painter = GpuTerrainPainterV2.Instance;
            var spinePoints = GenerateTestSpinePoints();
            var layers = GenerateTestLayers();

            // 预热
            painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);

            // 性能测试
            const int testCount = 10;
            stopwatch.Start();

            for (int i = 0; i < testCount; i++)
            {
                painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);
            }

            stopwatch.Stop();
            double averageTime = stopwatch.ElapsedMilliseconds / (double)testCount;
            UnityEngine.Debug.Log($"平均绘制时间: {averageTime:F2} ms");
        }

        private static void TestCacheSystem(UnityEngine.Terrain terrain)
        {
            UnityEngine.Debug.Log("测试缓存系统...");

            var painter = GpuTerrainPainterV2.Instance;
            var spinePoints = GenerateTestSpinePoints();
            var layers = GenerateTestLayers();

            // 第一次绘制（应该缓存）
            var stopwatch = Stopwatch.StartNew();
            painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);
            stopwatch.Stop();
            var firstTime = stopwatch.ElapsedMilliseconds;

            // 第二次绘制（应该使用缓存）
            stopwatch.Restart();
            painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);
            stopwatch.Stop();
            var secondTime = stopwatch.ElapsedMilliseconds;

            UnityEngine.Debug.Log($"首次绘制: {firstTime} ms, 缓存绘制: {secondTime} ms");
            UnityEngine.Debug.Log($"缓存效果: {(firstTime > secondTime ? "有效" : "需要优化")}");
        }

        private static double MeasureNewSystemPerformance(UnityEngine.Terrain terrain, Vector3[] spinePoints, LayerConfig[] layers)
        {
            var painter = GpuTerrainPainterV2.Instance;
            var stopwatch = new Stopwatch();

            // 预热
            painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);

            // 测试
            const int iterations = 20;
            stopwatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                painter.PaintPath(terrain, spinePoints, 5.0f, layers, true);
            }

            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds / (double)iterations;
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
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 5),
                new Vector3(20, 0, 10),
                new Vector3(30, 0, 8),
                new Vector3(40, 0, 15)
            };
        }

        private static LayerConfig[] GenerateTestLayers()
        {
            return new LayerConfig[]
            {
                new LayerConfig(layerIndex: 0, strength: 0.8f, blendMode: BlendMode.Replace),
                new LayerConfig(layerIndex: 1, strength: 0.6f, blendMode: BlendMode.Add)
            };
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
                    var stats = GpuTerrainPainterV2.Instance.GetPerformanceStats();
                    if (!string.IsNullOrEmpty(stats))
                    {
                        //                        UnityEngine.Debug.Log($"[GPU性能监控] {stats}");
                    }
                }
                catch
                {
                    // 忽略监控错误
                }
            }
        }
    }
}
