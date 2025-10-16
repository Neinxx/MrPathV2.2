using System;
using Unity.Collections;
using UnityEngine;
using MrPathV2;

namespace MrPathV2.Tests
{
    /// <summary>
    /// 测试修复后的NativeCollectionManager disposal流程
    /// </summary>
    public class TestDisposalFix
    {
        [UnityEditor.MenuItem("MrPathV2/Test Disposal Fix")]
        public static void RunTest()
        {
            Debug.Log("开始测试修复后的NativeCollectionManager disposal流程...");
            
            try
            {
                // 测试1: 正常的disposal流程
                TestNormalDisposal();
                
                // 测试2: 异常情况下的disposal
                TestExceptionHandling();
                
                // 测试3: 空集合和null引用处理
                TestNullAndEmptyHandling();
                
                Debug.Log("✅ 所有disposal测试通过！");
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Disposal测试失败: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private static void TestNormalDisposal()
        {
            Debug.Log("测试1: 正常disposal流程");
            
            using (var manager = new NativeCollectionManager())
            {
                // 创建一些native collections
                var array1 = manager.CreateNativeArray<float>(100, Allocator.Persistent, "TestArray1");
                var array2 = manager.CreateNativeArray<int>(50, Allocator.Persistent, "TestArray2");
                var list1 = manager.CreateNativeList<Vector3>(25, Allocator.Persistent, "TestList1");
                
                // 验证创建成功
                if (!array1.IsCreated || !array2.IsCreated || !list1.IsCreated)
                {
                    throw new Exception("Native collections创建失败");
                }
                
                // 获取统计信息
                var stats = manager.GetAllocationStats();
                Debug.Log($"分配统计: {string.Join(", ", stats)}");
                
                // manager会在using块结束时自动dispose
            }
            
            Debug.Log("✅ 正常disposal测试通过");
        }
        
        private static void TestExceptionHandling()
        {
            Debug.Log("测试2: 异常处理");
            
            var manager = new NativeCollectionManager();
            
            try
            {
                // 创建一个collection
                var array = manager.CreateNativeArray<float>(100, Allocator.Persistent, "TestArray");
                
                // 手动dispose这个collection
                array.Dispose();
                
                // 现在ForceCleanup应该能够安全处理已经disposed的collection
                manager.ForceCleanup();
                
                Debug.Log("✅ 异常处理测试通过");
            }
            finally
            {
                manager?.Dispose();
            }
        }
        
        private static void TestNullAndEmptyHandling()
        {
            Debug.Log("测试3: Null和空集合处理");
            
            var manager = new NativeCollectionManager();
            
            try
            {
                // 测试空manager的cleanup
                manager.ForceCleanup();
                
                // 创建后立即清理
                var array = manager.CreateNativeArray<float>(10, Allocator.Persistent, "TestArray");
                manager.ForceCleanup();
                
                Debug.Log("✅ Null和空集合处理测试通过");
            }
            finally
            {
                manager?.Dispose();
            }
        }
    }
}