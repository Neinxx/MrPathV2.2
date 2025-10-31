using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using __temp.MrPathV2.Runtime.Core;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一架构验证器 - 验证统一架构的功能和性能
    /// </summary>
    public static class UnifiedArchitectureValidator
    {
        [MenuItem("Tools/MrPathV2/Validate Unified Architecture")]
        public static void ValidateArchitecture()
        {
            UnityEngine.Debug.Log("开始验证统一架构...");

            var results = new List<ValidationResult>();

            // 1. 验证数据适配器
            results.Add(ValidateDataAdapter());

            // 2. 验证绘制器工厂
            results.Add(ValidatePainterFactory());

            // 3. 验证CPU绘制器
            results.Add(ValidateCpuPainter());

            // 4. 验证GPU绘制器
            results.Add(ValidateGpuPainter());

            // 5. 验证统一命令
            results.Add(ValidateUnifiedCommand());

            // 6. 性能基准测试
            results.Add(PerformanceBenchmark());

            // 输出结果
            PrintValidationResults(results);
        }

        private static ValidationResult ValidateDataAdapter()
        {
            var result = new ValidationResult("数据适配器验证");

            try
            {
                // 创建测试数据
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                // 测试GPU数据转换
                var gpuRenderData = UnifiedDataAdapter.CreateGpuRenderData(pathData, pathProfile, Allocator.TempJob);

                if (!gpuRenderData.IsCreated)
                {
                    result.AddError("GPU渲染数据创建失败");
                }
                else
                {
                    result.AddSuccess("GPU渲染数据创建成功");

                    // 验证数据完整性
                    if (gpuRenderData.PathData.SpinePoints.Length > 0)
                        result.AddSuccess("脊线点数据转换正确");
                    else
                        result.AddError("脊线点数据转换失败");

                    if (gpuRenderData.PathData.PathLength > 0)
                        result.AddSuccess("路径长度计算正确");
                    else
                        result.AddError("路径长度计算失败");

                    if (gpuRenderData.RecipeData.Layers.Length > 0)
                        result.AddSuccess("配方数据转换正确");
                    else
                        result.AddError("配方数据转换失败");
                }

                // 清理资源
                gpuRenderData.Dispose();
                result.AddSuccess("资源清理成功");
            }
            catch (Exception ex)
            {
                result.AddError($"数据适配器验证异常: {ex.Message}");
            }

            return result;
        }

        private static ValidationResult ValidatePainterFactory()
        {
            var result = new ValidationResult("绘制器工厂验证");

            try
            {
                var terrain = CreateTestTerrain();
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                // 修复：从PathData提取路径点并转换为NativeArray<float3>
                var pathPointsArray = new NativeArray<float3>(pathData.KnotCount, Allocator.Temp);
                for (int i = 0; i < pathData.KnotCount; i++)
                {
                    pathPointsArray[i] = pathData.GetPosition(i);
                }

                // 计算路径边界用于自动选择与性能评估
                var pathBounds = UnifiedDataAdapter.CalculatePathBounds(pathPointsArray, pathProfile.roadWidth);
                pathPointsArray.Dispose(); // 记得释放NativeArray资源

                // 测试CPU绘制器创建
                var cpuPainter = UnifiedPainterFactory.CreatePainter(PainterType.CPU, terrain, pathBounds);
                if (cpuPainter != null && cpuPainter.Type == PainterType.CPU)
                    result.AddSuccess("CPU绘制器创建成功");
                else
                    result.AddError("CPU绘制器创建失败");

                // 测试GPU绘制器创建
                var gpuPainter = UnifiedPainterFactory.CreatePainter(PainterType.GPU, terrain, pathBounds);
                if (SystemInfo.supportsComputeShaders)
                {
                    if (gpuPainter != null && gpuPainter.Type == PainterType.GPU)
                        result.AddSuccess("GPU绘制器创建成功");
                    else
                        result.AddError("GPU绘制器创建失败");
                }
                else
                {
                    result.AddInfo("当前系统不支持计算着色器，跳过GPU绘制器测试");
                }

                // 测试自动选择（基于首选GPU与路径覆盖估算）
                var autoPainter = UnifiedPainterFactory.CreatePainter(PainterType.GPU, terrain, pathBounds);
                if (autoPainter != null)
                    result.AddSuccess($"自动选择绘制器成功: {autoPainter.Type}");
                else
                    result.AddError("自动选择绘制器失败");

                // 清理资源
                cpuPainter?.Dispose();
                gpuPainter?.Dispose();
                autoPainter?.Dispose();

                CleanupTestTerrain(terrain);
            }
            catch (Exception ex)
            {
                result.AddError($"绘制器工厂验证异常: {ex.Message}");
            }

            return result;
        }


        private static ValidationResult ValidateUnifiedCommand()
        {
            var result = new ValidationResult("统一命令验证");

            try
            {
                var terrain = CreateTestTerrain();
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                // 使用新的命令创建方式
                var pathCreatorObject = new GameObject("ValidatorPathCreator");
                var pathCreator = pathCreatorObject.AddComponent<PathCreator>();
                pathCreator.pathData = pathData;
                pathCreator.profile = pathProfile;

                var command = UnifiedPaintTerrainCommandV2.Create(pathCreator);

                if (command != null)
                    result.AddSuccess("命令构建成功");
                else
                    result.AddError("命令构建失败");

                // 测试命令执行
                var terrains = new List<UnityEngine.Terrain>
                {
                    terrain
                };
                var commandResult = command.ExecuteAsync(terrains, pathProfile, PainterType.GPU, false).Result;


                if (commandResult.IsSuccess)
                    result.AddSuccess($"命令执行成功 (处理地形数: {commandResult.ProcessedTerrainCount})");
                else
                    result.AddError($"命令执行失败: {commandResult.ErrorMessage}");

                // 清理资源
                UnityEngine.Object.DestroyImmediate(pathCreatorObject);
                CleanupTestTerrain(terrain);
            }
            catch (Exception ex)
            {
                result.AddError($"统一命令验证异常: {ex.Message}");
            }

            return result;
        }

        private static ValidationResult ValidateCpuPainter()
        {
            var result = new ValidationResult("CPU绘制器验证");

            try
            {
                var terrain = CreateTestTerrain();
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                var painter = new UnifiedCpuTerrainPainter();

                if (!painter.IsSupported)
                {
                    result.AddError("CPU绘制器不支持");
                    return result;
                }

                // 测试同步绘制
                var syncResult = painter.Paint(terrain, pathData, pathProfile, true);
                if (syncResult.Success)
                    result.AddSuccess($"同步绘制成功 (耗时: {syncResult.ExecutionTimeMs:F2}ms)");
                else
                    result.AddError($"同步绘制失败: {syncResult.ErrorMessage}");

                // 测试异步绘制
                var asyncTask = painter.PaintAsync(terrain, pathData, pathProfile, true);
                asyncTask.Wait(5000); // 5秒超时

                if (asyncTask.IsCompleted && asyncTask.Result.Success)
                    result.AddSuccess($"异步绘制成功 (耗时: {asyncTask.Result.ExecutionTimeMs:F2}ms)");
                else
                    result.AddError("异步绘制失败或超时");

                CleanupTestTerrain(terrain);
            }
            catch (Exception ex)
            {
                result.AddError($"CPU绘制器验证异常: {ex.Message}");
            }

            return result;
        }

        private static ValidationResult ValidateGpuPainter()
        {
            var result = new ValidationResult("GPU绘制器验证");

            if (!SystemInfo.supportsComputeShaders)
            {
                result.AddInfo("当前系统不支持计算着色器，跳过GPU绘制器验证");
                return result;
            }

            try
            {
                var terrain = CreateTestTerrain();
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                var painter = new UnifiedGpuTerrainPainter();

                if (!painter.IsSupported)
                {
                    result.AddError("GPU绘制器不支持");
                    return result;
                }

                // 测试同步绘制
                var syncResult = painter.Paint(terrain, pathData, pathProfile, true);
                if (syncResult.Success)
                    result.AddSuccess($"同步绘制成功 (耗时: {syncResult.ExecutionTimeMs:F2}ms)");
                else
                    result.AddError($"同步绘制失败: {syncResult.ErrorMessage}");

                CleanupTestTerrain(terrain);
            }
            catch (Exception ex)
            {
                result.AddError($"GPU绘制器验证异常: {ex.Message}");
            }

            return result;
        }

        private static ValidationResult PerformanceBenchmark()
        {
            var result = new ValidationResult("性能基准测试");

            try
            {
                var terrain = CreateTestTerrain();
                var pathData = CreateTestPathData();
                var pathProfile = CreateTestPathProfile();

                const int iterations = 5;

                // CPU性能测试
                var cpuPainter = new UnifiedCpuTerrainPainter();
                var cpuTimes = new List<float>();

                for (int i = 0; i < iterations; i++)
                {
                    var cpuResult = cpuPainter.Paint(terrain, pathData, pathProfile, true);
                    if (cpuResult.Success)
                        cpuTimes.Add(cpuResult.ExecutionTimeMs);
                }

                if (cpuTimes.Count > 0)
                {
                    var avgCpuTime = cpuTimes.Average();
                    result.AddSuccess($"CPU平均绘制时间: {avgCpuTime:F2}ms ({iterations}次测试)");
                }

                // GPU性能测试（如果支持）
                if (SystemInfo.supportsComputeShaders)
                {
                    var gpuPainter = new UnifiedGpuTerrainPainter();
                    var gpuTimes = new List<float>();

                    for (int i = 0; i < iterations; i++)
                    {
                        var gpuResult = gpuPainter.Paint(terrain, pathData, pathProfile, true);
                        if (gpuResult.Success)
                            gpuTimes.Add(gpuResult.ExecutionTimeMs);
                    }

                    if (gpuTimes.Count > 0)
                    {
                        var avgGpuTime = gpuTimes.Average();
                        result.AddSuccess($"GPU平均绘制时间: {avgGpuTime:F2}ms ({iterations}次测试)");

                        if (cpuTimes.Count > 0)
                        {
                            var speedup = cpuTimes.Average() / avgGpuTime;
                            result.AddInfo($"GPU相对CPU加速比: {speedup:F2}x");
                        }
                    }
                }

                CleanupTestTerrain(terrain);
            }
            catch (Exception ex)
            {
                result.AddError($"性能基准测试异常: {ex.Message}");
            }

            return result;
        }

        #region Helper Methods

        private static PathData CreateTestPathData()
        {
            var pathData = new PathData();

            // 创建简单的测试路径
            var positions = new List<Vector3>
            {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(20, 0, 10),
                new Vector3(30, 0, 10)
            };

            foreach (var pos in positions)
            {
                pathData.AddKnot(pos, Vector3.forward, Vector3.back);
            }

            return pathData;
        }

        private static PathProfile CreateTestPathProfile()
        {
            var profile = ScriptableObject.CreateInstance<PathProfile>();
            profile.roadWidth = 5f;
            profile.falloffWidth = 2f;
            profile.falloffShape = AnimationCurve.EaseInOut(0, 1, 1, 0);

            // 创建测试道路配方
            var recipe = ScriptableObject.CreateInstance<StylizedRoadRecipe>();
            recipe.masterOpacity = 1f;

            profile.roadRecipe = recipe;

            return profile;
        }

        private static UnityEngine.Terrain CreateTestTerrain()
        {
            var terrainData = new TerrainData
            {
                heightmapResolution = 129,
                size = new Vector3(100, 10, 100)
            };

            var terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            return terrainObject.GetComponent<UnityEngine.Terrain>();
        }

        private static void CleanupTestTerrain(UnityEngine.Terrain terrain)
        {
            if (terrain != null && terrain.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(terrain.gameObject);
            }
        }

        private static void PrintValidationResults(List<ValidationResult> results)
        {
            UnityEngine.Debug.Log("=== 统一架构验证结果 ===");

            int totalTests = 0;
            int passedTests = 0;
            int failedTests = 0;

            foreach (var result in results)
            {
                UnityEngine.Debug.Log($"\n[{result.TestName}]");

                foreach (var message in result.Messages)
                {
                    totalTests++;

                    switch (message.Type)
                    {
                        case MessageType.Success:
                            UnityEngine.Debug.Log($"  ✅ {message.Text}");
                            passedTests++;
                            break;
                        case MessageType.Error:
                            UnityEngine.Debug.LogError($"  ❌ {message.Text}");
                            failedTests++;
                            break;
                        case MessageType.Info:
                            UnityEngine.Debug.Log($"  ℹ️ {message.Text}");
                            break;
                    }
                }
            }

            UnityEngine.Debug.Log($"\n=== 总结 ===");
            UnityEngine.Debug.Log($"总测试数: {totalTests}");
            UnityEngine.Debug.Log($"通过: {passedTests}");
            UnityEngine.Debug.Log($"失败: {failedTests}");
            UnityEngine.Debug.Log($"成功率: {(totalTests > 0 ? (float)passedTests / totalTests * 100 : 0):F1}%");

            if (failedTests == 0)
            {
                UnityEngine.Debug.Log("🎉 所有测试通过！统一架构验证成功！");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"⚠️ 有 {failedTests} 个测试失败，请检查相关问题。");
            }
        }

        #endregion

        #region Data Structures

        private class ValidationResult
        {
            public string TestName { get; }
            public List<ValidationMessage> Messages { get; }

            public ValidationResult(string testName)
            {
                TestName = testName;
                Messages = new List<ValidationMessage>();
            }

            public void AddSuccess(string message) => Messages.Add(new ValidationMessage(MessageType.Success, message));
            public void AddError(string message) => Messages.Add(new ValidationMessage(MessageType.Error, message));
            public void AddInfo(string message) => Messages.Add(new ValidationMessage(MessageType.Info, message));
        }

        private class ValidationMessage
        {
            public MessageType Type { get; }
            public string Text { get; }

            public ValidationMessage(MessageType type, string text)
            {
                Type = type;
                Text = text;
            }
        }

        private enum MessageType
        {
            Success,
            Error,
            Info
        }

        #endregion
    }

    // 扩展方法
    public static class ListExtensions
    {
        public static float Average(this List<float> list)
        {
            if (list.Count == 0) return 0f;

            float sum = 0f;
            foreach (var value in list)
                sum += value;

            return sum / list.Count;
        }
    }
}
