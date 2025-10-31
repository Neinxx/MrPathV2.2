using System;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.GPU;

namespace __temp.MrPathV2.Editor.Examples
{
    /// <summary>
    /// GPU绘制管线性能测试工具
    /// 用于验证新管线的性能和稳定性
    /// </summary>
    public class PerformanceTest : EditorWindow
    {
        [MenuItem("MrPath/GPU Pipeline/Performance Test Window")]
        public static void ShowWindow()
        {
            GetWindow<PerformanceTest>("GPU性能测试");
        }

        private UnityEngine.Terrain _testTerrain;
        private int _testIterations = 50;
        private float _roadWidth = 5.0f;
        private bool _usePreview = true;
        private bool _testRunning = false;

        private List<double> _testResults = new List<double>();
        private string _lastTestReport = "";

        private void OnGUI()
        {
            GUILayout.Label("GPU绘制管线性能测试", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 测试配置
            EditorGUILayout.LabelField("测试配置", EditorStyles.boldLabel);
            _testTerrain = (UnityEngine.Terrain)EditorGUILayout.ObjectField("测试地形", _testTerrain, typeof(UnityEngine.Terrain), true);
            _testIterations = EditorGUILayout.IntSlider("测试次数", _testIterations, 10, 200);
            _roadWidth = EditorGUILayout.Slider("道路宽度", _roadWidth, 1.0f, 20.0f);
            _usePreview = EditorGUILayout.Toggle("预览模式", _usePreview);

            EditorGUILayout.Space();

            // 测试按钮
            EditorGUI.BeginDisabledGroup(_testRunning || _testTerrain == null);
            if (GUILayout.Button("开始性能测试", GUILayout.Height(30)))
            {
                StartPerformanceTest();
            }
            EditorGUI.EndDisabledGroup();

            if (_testRunning)
            {
                EditorGUILayout.HelpBox("测试进行中，请稍候...", MessageType.Info);
            }

            EditorGUILayout.Space();

            // 测试结果
            if (!string.IsNullOrEmpty(_lastTestReport))
            {
                EditorGUILayout.LabelField("测试结果", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_lastTestReport, GUILayout.Height(200));

                if (GUILayout.Button("复制结果到剪贴板"))
                {
                    EditorGUIUtility.systemCopyBuffer = _lastTestReport;
                }
            }

            // 实时性能监控
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("实时性能监控", EditorStyles.boldLabel);

            try
            {
                var stats = GpuTerrainPainterV2.Instance.GetPerformanceStats();
                if (!string.IsNullOrEmpty(stats))
                {
                    EditorGUILayout.TextArea(stats, GUILayout.Height(60));
                }
                else
                {
                    EditorGUILayout.HelpBox("暂无性能数据", MessageType.Info);
                }
            }
            catch
            {
                EditorGUILayout.HelpBox("性能监控不可用", MessageType.Warning);
            }

            // 缓存管理
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("缓存管理", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清除缓存"))
            {
                try
                {
                    GpuTerrainPainterV2.Instance.ClearCache();
                    UnityEngine.Debug.Log("GPU缓存已清除");
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"清除缓存失败: {ex.Message}");
                }
            }

            if (GUILayout.Button("强制GC"))
            {
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                UnityEngine.Debug.Log("垃圾回收完成");
            }
            EditorGUILayout.EndHorizontal();
        }

        private async void StartPerformanceTest()
        {
            if (_testTerrain == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个测试地形", "确定");
                return;
            }

            _testRunning = true;
            _testResults.Clear();

            try
            {
                UnityEngine.Debug.Log($"开始GPU性能测试 - 迭代次数: {_testIterations}");

                var painter = GpuTerrainPainterV2.Instance;
                var spinePoints = GenerateTestPath();
                var layers = GenerateTestLayers();

                // 预热
                UnityEngine.Debug.Log("预热GPU管线...");
                for (int i = 0; i < 3; i++)
                {
                    await painter.PaintPathAsync(_testTerrain, spinePoints, _roadWidth, layers, _usePreview);
                }

                // 性能测试
                UnityEngine.Debug.Log("开始性能测试...");
                var stopwatch = new Stopwatch();

                for (int i = 0; i < _testIterations; i++)
                {
                    stopwatch.Restart();

                    var result = await painter.PaintPathAsync(_testTerrain, spinePoints, _roadWidth, layers, _usePreview);

                    stopwatch.Stop();

                    if (result.Success)
                    {
                        _testResults.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"测试迭代 {i + 1} 失败");
                    }

                    // 更新进度
                    if (i % 10 == 0)
                    {
                        float progress = (float)i / _testIterations;
                        EditorUtility.DisplayProgressBar("性能测试", $"进度: {i}/{_testIterations}", progress);
                    }
                }

                EditorUtility.ClearProgressBar();
                GenerateTestReport();

                UnityEngine.Debug.Log("GPU性能测试完成");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"性能测试失败: {ex.Message}");
                EditorUtility.DisplayDialog("测试失败", $"性能测试失败: {ex.Message}", "确定");
            }
            finally
            {
                _testRunning = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void GenerateTestReport()
        {
            if (_testResults.Count == 0)
            {
                _lastTestReport = "没有有效的测试结果";
                return;
            }

            var results = _testResults.ToArray();
            var average = results.Average();
            var min = results.Min();
            var max = results.Max();
            var median = results.OrderBy(x => x).ElementAt(results.Length / 2);

            // 计算标准差
            var variance = results.Select(x => Math.Pow(x - average, 2)).Average();
            var stdDev = Math.Sqrt(variance);

            // 计算百分位数
            var sorted = results.OrderBy(x => x).ToArray();
            var p95 = sorted[(int)(sorted.Length * 0.95)];
            var p99 = sorted[(int)(sorted.Length * 0.99)];

            _lastTestReport = $@"=== GPU绘制管线性能测试报告 ===

测试配置:
- 地形: {_testTerrain.name}
- 测试次数: {_testIterations}
- 道路宽度: {_roadWidth:F1}m
- 预览模式: {(_usePreview ? "是" : "否")}
- 成功次数: {_testResults.Count}

性能统计 (毫秒):
- 平均耗时: {average:F2} ms
- 最小耗时: {min:F2} ms
- 最大耗时: {max:F2} ms
- 中位数: {median:F2} ms
- 标准差: {stdDev:F2} ms
- 95百分位: {p95:F2} ms
- 99百分位: {p99:F2} ms

性能评估:
- 平均FPS等效: {1000.0 / average:F1} FPS
- 稳定性: {(stdDev < average * 0.2 ? "优秀" : stdDev < average * 0.5 ? "良好" : "需要优化")}
- 性能等级: {GetPerformanceGrade(average)}

系统信息:
- Unity版本: {Application.unityVersion}
- 平台: {Application.platform}
- GPU: {SystemInfo.graphicsDeviceName}
- 显存: {SystemInfo.graphicsMemorySize} MB

测试时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}
";

            UnityEngine.Debug.Log(_lastTestReport);
        }

        private string GetPerformanceGrade(double averageMs)
        {
            if (averageMs < 5) return "A+ (优秀)";
            if (averageMs < 10) return "A (良好)";
            if (averageMs < 20) return "B (一般)";
            if (averageMs < 50) return "C (较差)";
            return "D (需要优化)";
        }

        private Vector3[] GenerateTestPath()
        {
            // 生成一个复杂的测试路径
            var points = new List<Vector3>();
            var terrainSize = _testTerrain.terrainData.size;
            var center = new Vector3(terrainSize.x * 0.5f, 0, terrainSize.z * 0.5f);

            // 生成螺旋路径
            int pointCount = 20;
            float radius = Mathf.Min(terrainSize.x, terrainSize.z) * 0.3f;

            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / (pointCount - 1);
                float angle = t * Mathf.PI * 4; // 2圈螺旋
                float r = radius * (0.2f + 0.8f * t);

                var point = center + new Vector3(
                    Mathf.Cos(angle) * r,
                    0,
                    Mathf.Sin(angle) * r
                );

                points.Add(point);
            }

            return points.ToArray();
        }

        private static LayerConfig[] GenerateTestLayers()
        {
            return new LayerConfig[]
            {
                new LayerConfig(layerIndex: 0, strength: 0.9f, blendMode: BlendMode.Replace),
                new LayerConfig(layerIndex: 1, strength: 0.7f, blendMode: BlendMode.Add),
                new LayerConfig(layerIndex: 2, strength: 0.5f, blendMode: BlendMode.Multiply)
            };
        }
    }
}
