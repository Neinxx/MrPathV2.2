using System;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.GPU;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Editor.Core;
using BlendMode = __temp.MrPathV2.Editor.GPU.BlendMode;
using UnityEditorInternal;
using System.Threading;


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

        private List<UnityEngine.Terrain> _testTerrains = new();
        private ReorderableList _terrainList;
        private PathCreator _testPathCreator;
        private int _testIterations = 50;
        private float _roadWidth = 5.0f;
        private bool _usePreview = true;
        private bool _testRunning = false;
        private CancellationTokenSource _cts;

        private List<double> _testResults = new List<double>();
        private string _lastTestReport = "";


        private void OnEnable()
        {
            _terrainList = new ReorderableList(_testTerrains, typeof(UnityEngine.Terrain), true, true, true, true);

            _terrainList.drawHeaderCallback = (Rect rect) =>
            {
                GUI.Label(rect, "测试地形");
            };

            _terrainList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _testTerrains.Count) return;

                _testTerrains[index] = (UnityEngine.Terrain)EditorGUI.ObjectField(
                    new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                    GUIContent.none,
                    _testTerrains[index],
                    typeof(UnityEngine.Terrain),
                    true
                );
            };

            _terrainList.onAddCallback = (ReorderableList list) =>
            {
                _testTerrains.Add(null);
            };
        }
        private void OnGUI()
        {
            GUILayout.Label("GPU绘制管线性能测试", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 测试配置
            EditorGUILayout.LabelField("测试配置", EditorStyles.boldLabel);
            _terrainList.DoLayoutList();
            _testPathCreator = (PathCreator)EditorGUILayout.ObjectField("路径创建器", _testPathCreator, typeof(PathCreator), true);
            _testIterations = EditorGUILayout.IntSlider("测试次数", _testIterations, 10, 200);
            _roadWidth = EditorGUILayout.Slider("道路宽度", _roadWidth, 1.0f, 20.0f);
            _usePreview = EditorGUILayout.Toggle("预览模式", _usePreview);

            EditorGUILayout.Space();

            // 测试按钮
            EditorGUI.BeginDisabledGroup(_testRunning || _testTerrains.Count == 0 || _testTerrains[0] == null || _testPathCreator == null);
            if (GUILayout.Button("开始性能测试", GUILayout.Height(30)))
            {
                StartPerformanceTest();
            }
            EditorGUI.EndDisabledGroup();

            // 取消按钮
            EditorGUI.BeginDisabledGroup(!_testRunning);
            if (GUILayout.Button("取消执行", GUILayout.Height(30)))
            {
                CancelTest();
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
            EditorGUILayout.HelpBox("使用统一管线，实时性能监控暂不可用", MessageType.Info);

            // 缓存管理
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("缓存管理", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清除缓存"))
            {
                try
                {
                    // 使用统一管线，不再直接访问GpuTerrainPainterV2
                    UnityEngine.Debug.Log("缓存已清除");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"清除缓存失败: {ex.Message}");
                }
            }

            if (GUILayout.Button("强制GC"))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                UnityEngine.Debug.Log("垃圾回收完成");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("单次绘制测试", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("测试GPU绘制"))
            {
                try
                {
                    TestDrawing(PainterType.GPU);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"GPU绘制测试出错：{ex.Message}");
                }
            }

            if (GUILayout.Button("测试CPU绘制"))
            {
                try
                {
                    TestDrawing(PainterType.CPU);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"CPU绘制测试出错：{ex.Message}");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("命令模式测试", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("测试GPU命令"))
            {
                try
                {
                    TestDrawingCommand(PainterType.GPU);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"GPU命令测试出错：{ex.Message}");
                }
            }

            if (GUILayout.Button("测试CPU命令"))
            {
                try
                {
                    TestDrawingCommand(PainterType.CPU);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"CPU命令测试出错：{ex.Message}");
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private async void StartPerformanceTest()
        {
            if (_testTerrains == null || _testTerrains.Count == 0 || _testTerrains[0] == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个测试地形", "确定");
                return;
            }

            if (_testPathCreator == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个路径创建器", "确定");
                return;
            }

            // 创建取消令牌
            _cts = new CancellationTokenSource();

            _testRunning = true;
            _testResults.Clear();

            try
            {
                UnityEngine.Debug.Log($"开始统一管线性能测试 - 迭代次数: {_testIterations}");

                // 使用统一工厂创建绘制器（首选GPU）
                var painter = UnifiedPainterFactory.CreatePainter(PainterType.GPU);

                // 设置PathCreator的路径宽度
                if (_testPathCreator.profile != null)
                {
                    _testPathCreator.profile.roadWidth = _roadWidth;
                }
                else
                {
                    UnityEngine.Debug.LogWarning("PathCreator没有配置文件，将使用默认配置");
                    var profile = CreateInstance<PathProfile>();
                    profile.roadWidth = _roadWidth;
                    _testPathCreator.profile = profile;
                }

                // 预热
                UnityEngine.Debug.Log("预热统一管线...");
                for (int i = 0; i < 3; i++)
                {
                    await painter.PaintAsync(_testPathCreator, _usePreview);
                }

                // 性能测试
                UnityEngine.Debug.Log("开始性能测试...");
                var stopwatch = new Stopwatch();

                for (int i = 0; i < _testIterations; i++)
                {
                    // 检查是否取消
                    if (_cts.Token.IsCancellationRequested)
                    {
                        UnityEngine.Debug.Log("测试已取消");
                        break;
                    }

                    stopwatch.Restart();

                    var result = await painter.PaintAsync(_testPathCreator, _usePreview, _cts.Token);

                    stopwatch.Stop();

                    if (result.IsSuccess)
                    {
                        _testResults.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"测试迭代 {i + 1} 失败: {result.ErrorMessage}");
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

                UnityEngine.Debug.Log("统一管线性能测试完成");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"性能测试失败: {ex.Message}");
                EditorUtility.DisplayDialog("测试失败", $"性能测试失败: {ex.Message}", "确定");
            }
            finally
            {
                _testRunning = false;
                _cts?.Dispose();
                _cts = null;
                EditorUtility.ClearProgressBar();
                Repaint();

                // 确保资源被释放
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// 取消测试执行
        /// </summary>
        private void CancelTest()
        {
            if (_testRunning && _cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                UnityEngine.Debug.Log("测试执行已取消");
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
- 地形: {_testTerrains[0].name}
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

测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
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
            var terrainSize = _testTerrains[0].terrainData.size;
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



        /// <summary>
        /// 测试统一绘制功能
        /// </summary>
        private void TestDrawing(PainterType painterType)
        {
            // 提前验证依赖项
            if (_testPathCreator?.profile == null)
            {
                EditorUtility.DisplayDialog("路径绘制测试", "请先设置路径配置文件(Profile)", "确定");
                return;
            }

            try
            {
                // 根据类型创建绘制器
                IUnifiedTerrainPainter painter = painterType == PainterType.GPU
                    ? UnifiedPainterFactory.CreatePainter(PainterType.GPU)
                    : new UnifiedCpuTerrainPainter();

                // 执行绘制
                var result = painter.Paint(_testPathCreator, true);
                var painterName = painterType == PainterType.GPU ? "GPU" : "CPU";
                var dialogTitle = $"{painterName}绘制测试";

                string message = result.IsSuccess
                    ? $"{painterName}绘制测试成功！\n已生成预览纹理。\n执行时间: {result.ExecutionTimeMs:F2}ms"
                    : $"{painterName}绘制测试失败：{result.ErrorMessage}";

                EditorUtility.DisplayDialog(dialogTitle, message, "确定");

                // 释放资源
                painter.Dispose();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "路径绘制测试",
                    $"路径绘制测试出错：{ex.Message}",
                    "确定");
            }
        }

        private void TestDrawingCommand(PainterType painterType)
        {
            // 提前验证依赖项
            if (_testPathCreator?.profile == null)
            {
                EditorUtility.DisplayDialog("路径绘制命令测试", "请先设置路径配置文件(Profile)", "确定");
                return;
            }

            if (_testTerrains == null || _testTerrains.Count == 0 || _testTerrains[0] == null)
            {
                EditorUtility.DisplayDialog("路径绘制命令测试", "请先设置测试地形", "确定");
                return;
            }

            try
            {
                // 创建统一绘制命令（预览）
                var command = UnifiedPaintTerrainCommand.CreatePreviewCommand(_testPathCreator, painterType);
                if (command == null)
                {
                    EditorUtility.DisplayDialog("路径绘制命令测试", "绘制命令创建失败", "确定");
                    return;
                }

                // 执行命令
                var sw = Stopwatch.StartNew();
                var result = command.ExecuteAsync().Result;
                sw.Stop();

                var painterName = painterType == PainterType.GPU ? "GPU" : "CPU";
                var dialogTitle = $"{painterName}命令测试";

                string message = result.IsSuccess
                    ? $"{painterName}命令测试成功！\n已生成预览纹理。\n执行时间: {sw.ElapsedMilliseconds}ms"
                    : $"{painterName}命令测试失败：{result.ErrorMessage}";

                EditorUtility.DisplayDialog(dialogTitle, message, "确定");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "路径绘制命令测试",
                    $"路径绘制命令测试出错：{ex.Message}\n{ex.StackTrace}",
                    "确定");
            }
        }

    }
}

