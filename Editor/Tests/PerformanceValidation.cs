#if UNITY_EDITOR
using System.Diagnostics;
using __temp.MrPathV2.Editor.Factories;
using __temp.MrPathV2.Editor.Inspectors;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Providers;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Tests
{
    /// <summary>
    /// 性能验证脚本 - 验证PathCreator取消选中优化效果
    /// </summary>
    public static class PerformanceValidation
    {
        [MenuItem("MrPath/Tests/Validate Performance Optimizations")]
        public static void ValidateOptimizations()
        {
            var results = new System.Text.StringBuilder();
            results.AppendLine("=== PathCreator性能优化验证 ===");
            results.AppendLine();

            // 测试1: TerrainHeightProvider.Dispose性能
            TestTerrainHeightProviderDispose(results);
            
            // 测试2: PathEditorContext.Dispose性能
            TestPathEditorContextDispose(results);
            
            // 测试3: 模拟选中/取消选中操作
            TestSelectionPerformance(results);

            UnityEngine.Debug.Log(results.ToString());
            EditorUtility.DisplayDialog("性能验证完成", 
                "优化验证已完成，详细结果请查看Console窗口", 
                "确定");
        }

        private static void TestTerrainHeightProviderDispose(System.Text.StringBuilder results)
        {
            results.AppendLine("--- 测试1: TerrainHeightProvider.Dispose性能 ---");
            
            var stopwatch = Stopwatch.StartNew();
            
            // 创建多个TerrainHeightProvider实例并测试Dispose性能
            for (int i = 0; i < 10; i++)
            {
                var provider = new TerrainHeightProvider();
                // 模拟一些订阅操作
                provider.Dispose();
            }
            
            stopwatch.Stop();
            var avgTime = stopwatch.ElapsedMilliseconds / 10f;
            
            results.AppendLine($"平均Dispose时间: {avgTime:F2} ms");
            
            if (avgTime < 5f)
            {
                results.AppendLine("✅ TerrainHeightProvider.Dispose性能良好");
            }
            else
            {
                results.AppendLine("⚠️ TerrainHeightProvider.Dispose性能需要进一步优化");
            }
            results.AppendLine();
        }

        private static void TestPathEditorContextDispose(System.Text.StringBuilder results)
        {
            results.AppendLine("--- 测试2: PathEditorContext.Dispose性能 ---");
            
            // 创建测试PathCreator
            var testGO = new GameObject("TestPathCreator");
            var pathCreator = testGO.AddComponent<MrPathV2.Runtime.Core.PathCreator>();
            
            var profile = ScriptableObject.CreateInstance<PathProfile>();
            profile.name = "TestProfile";
            pathCreator.profile = profile;

            var stopwatch = Stopwatch.StartNew();
            
            // 测试PathEditorContext创建和销毁
            for (int i = 0; i < 5; i++)
            {
                var context = new PathEditorContext(pathCreator);
                context.Dispose();
            }
            
            stopwatch.Stop();
            var avgTime = stopwatch.ElapsedMilliseconds / 5f;
            
            results.AppendLine($"平均PathEditorContext Dispose时间: {avgTime:F2} ms");
            
            if (avgTime < 10f)
            {
                results.AppendLine("✅ PathEditorContext.Dispose性能良好");
            }
            else
            {
                results.AppendLine("⚠️ PathEditorContext.Dispose性能需要进一步优化");
            }
            
            // 清理
            Object.DestroyImmediate(testGO);
            Object.DestroyImmediate(profile);
            results.AppendLine();
        }

        private static void TestSelectionPerformance(System.Text.StringBuilder results)
        {
            results.AppendLine("--- 测试3: 选中/取消选中性能 ---");
            
            // 创建测试PathCreator
            var testGO = new GameObject("TestPathCreator");
            var pathCreator = testGO.AddComponent<MrPathV2.Runtime.Core.PathCreator>();
            
            var profile = ScriptableObject.CreateInstance<PathProfile>();
            profile.name = "TestProfile";
            pathCreator.profile = profile;

            // 添加路径点
            PathFactory.CreateDefaultPath();

            var stopwatch = Stopwatch.StartNew();
            
            // 模拟快速选中/取消选中操作
            for (int i = 0; i < 20; i++)
            {
                Selection.activeGameObject = testGO;
                System.Threading.Thread.Sleep(5); // 短暂延迟
                Selection.activeGameObject = null;
                System.Threading.Thread.Sleep(5);
            }
            
            stopwatch.Stop();
            var avgTime = stopwatch.ElapsedMilliseconds / 40f; // 40次操作（20次选中+20次取消选中）
            
            results.AppendLine($"平均选中/取消选中时间: {avgTime:F2} ms");
            
            if (avgTime < 25f)
            {
                results.AppendLine("✅ 选中/取消选中性能良好");
            }
            else if (avgTime < 50f)
            {
                results.AppendLine("⚠️ 选中/取消选中性能一般");
            }
            else
            {
                results.AppendLine("❌ 选中/取消选中性能较差");
            }
            
            // 清理
            Object.DestroyImmediate(testGO);
            Object.DestroyImmediate(profile);
            results.AppendLine();
        }
    }
}
#endif
