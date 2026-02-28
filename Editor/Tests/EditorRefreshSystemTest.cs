using System;
using System.Collections;
using UnityEngine;
using UnityEditor;
using System.Linq;


namespace MrPathV2
{
    /// <summary>
    /// 编辑器刷新系统测试
    /// </summary>
    public static class EditorRefreshSystemTest
    {
        [MenuItem("MrPath/Tests/Test Editor Refresh System")]
        public static void TestEditorRefreshSystem()
        {
            Debug.Log("开始测试编辑器刷新系统...");
            
            try
            {
                TestBasicRefreshFunctionality();
                TestDebouncing();
                TestMultipleRefreshTypes();
                TestDisposal();
                
                Debug.Log("✓ 编辑器刷新系统测试通过！");
            }
            catch (Exception ex)
            {
                Debug.LogError($"✗ 编辑器刷新系统测试失败: {ex.Message}");
            }
        }

        private static void TestBasicRefreshFunctionality()
        {
            Debug.Log("测试基本刷新功能...");
            
            using var refreshManager = new EditorRefreshManager();
            bool refreshExecuted = false;
            
            refreshManager.RequestRefresh("test_refresh", () =>
            {
                refreshExecuted = true;
                Debug.Log("刷新操作已执行");
            }, forceImmediate: true);
            
            if (!refreshExecuted)
            {
                throw new Exception("立即刷新未执行");
            }
            
            Debug.Log("✓ 基本刷新功能正常");
        }

        private static void TestDebouncing()
        {
            Debug.Log("测试防抖动功能...");
            
            using var refreshManager = new EditorRefreshManager();
            int executionCount = 0;
            
            // 快速连续请求多次刷新
            for (int i = 0; i < 5; i++)
            {
                refreshManager.RequestRefresh("debounce_test", () =>
                {
                    executionCount++;
                    Debug.Log($"防抖动测试执行次数: {executionCount}");
                });
            }
            
            // 等待防抖动延迟
            System.Threading.Thread.Sleep(600); // 稍微超过默认的500ms延迟
            
            // 手动处理待执行的刷新
            EditorApplication.delayCall += () =>
            {
                if (executionCount != 1)
                {
                    Debug.LogError($"防抖动失败: 期望执行1次，实际执行{executionCount}次");
                }
                else
                {
                    Debug.Log("✓ 防抖动功能正常");
                }
            };
        }

        private static void TestMultipleRefreshTypes()
        {
            Debug.Log("测试多种刷新类型...");
            
            using var refreshManager = new EditorRefreshManager();
            bool previewRefreshed = false;
            bool sceneRefreshed = false;
            bool inspectorRefreshed = false;
            
            refreshManager.RequestRefresh("preview", () => previewRefreshed = true, true);
            refreshManager.RequestRefresh("scene", () => sceneRefreshed = true, true);
            refreshManager.RequestRefresh("inspector", () => inspectorRefreshed = true, true);
            
            if (!previewRefreshed || !sceneRefreshed || !inspectorRefreshed)
            {
                throw new Exception("多种刷新类型测试失败");
            }
            
            Debug.Log("✓ 多种刷新类型正常");
        }

        private static void TestDisposal()
        {
            Debug.Log("测试资源释放...");
            
            var refreshManager = new EditorRefreshManager();
            bool shouldNotExecute = false;
            
            refreshManager.RequestRefresh("disposal_test", () =>
            {
                shouldNotExecute = true;
                Debug.LogError("这个刷新不应该被执行");
            });
            
            // 释放管理器，应该取消所有待执行的刷新
            refreshManager.Dispose();
            
            // 等待一段时间确保没有执行
            System.Threading.Thread.Sleep(600);
            
            if (shouldNotExecute)
            {
                throw new Exception("释放后仍然执行了刷新操作");
            }
            
            Debug.Log("✓ 资源释放正常");
        }

        [MenuItem("MrPath/Tests/Test Memory Management System")]
        public static void TestMemoryManagementSystem()
        {
            Debug.Log("开始测试内存管理系统...");
            
            try
            {
                TestNativeCollectionManager();
                TestPreviewLineRendererSharing();
                
                Debug.Log("✓ 内存管理系统测试通过！");
            }
            catch (Exception ex)
            {
                Debug.LogError($"✗ 内存管理系统测试失败: {ex.Message}");
            }
        }

        private static void TestNativeCollectionManager()
        {
            Debug.Log("测试NativeCollectionManager...");
            
            using var manager = new MrPathV2.NativeCollectionManager();
            
            // 测试NativeArray创建和跟踪
            var array = manager.CreateNativeArray<float>(100, Unity.Collections.Allocator.Persistent, "TestArray");
            
            if (!array.IsCreated)
            {
                throw new Exception("NativeArray创建失败");
            }
            
            var stats = manager.GetAllocationStats();
            if (!stats.ContainsKey("TestArray") || stats["TestArray"] != 1)
            {
                throw new Exception($"分配统计错误: 期望TestArray=1，实际{string.Join(",", stats.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
            
            Debug.Log($"✓ NativeCollectionManager正常 - 分配统计: {string.Join(",", stats.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        private static void TestPreviewLineRendererSharing()
        {
            Debug.Log("测试PreviewLineRenderer共享...");
            
            // 这里需要一个PathCreator实例来测试
            var go = new GameObject("TestPathCreator");
            var pathCreator = go.AddComponent<PathCreator>();
            
            try
            {
                using var context = new PathEditorContext(pathCreator);
                var sharedRenderer = context.PreviewManager?.GetSharedLineRenderer();
                
                if (sharedRenderer == null)
                {
                    throw new Exception("共享LineRenderer获取失败");
                }
                
                Debug.Log("✓ PreviewLineRenderer共享正常");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}