using UnityEngine;
using UnityEditor;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.Preview;

namespace MrPathV2.Editor.Tests
{
    /// <summary>
    /// 简单的GPU预览测试脚本
    /// </summary>
    public static class SimpleGpuTest
    {
        [MenuItem("MrPath/Debug/Test GPU Preview Now")]
        public static void TestGpuPreviewNow()
        {
            Debug.Log("=== GPU Preview Test Started ===");
            
            // 查找场景中的组件
            var pathCreator = Object.FindObjectOfType<PathCreator>();
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            
            if (pathCreator == null)
            {
                Debug.LogError("❌ No PathCreator found in scene!");
                return;
            }
            
            if (terrain == null)
            {
                Debug.LogError("❌ No Terrain found in scene!");
                return;
            }
            
            Debug.Log($"✅ Found PathCreator: {pathCreator.name}");
            Debug.Log($"✅ Found Terrain: {terrain.name}");
            
            // 检查profile
            if (pathCreator.profile == null)
            {
                Debug.LogError("❌ PathCreator has no profile!");
                return;
            }
            
            Debug.Log($"✅ Profile: {pathCreator.profile.name}");
            
            // 获取spine数据 - 使用PathSampler来生成spine
            var heightProvider = new MrPathV2.Runtime.Providers.TerrainHeightProvider();
            var spine = MrPathV2.Runtime.Core.PathSampler.SamplePath(pathCreator, heightProvider);
            if (spine.VertexCount == 0)
            {
                Debug.LogError("❌ Failed to get spine data!");
                return;
            }
            
            Debug.Log($"✅ Spine has {spine.VertexCount} vertices");
            
            // 启用GPU预览
            PreviewMaterialManager.EnableGpuPreview = true;
            Debug.Log("✅ GPU preview enabled");
            
            // 执行GPU预览
            Debug.Log("🚀 Executing GPU preview...");
            bool success = GpuPreviewRunner.TryRun(terrain, spine, pathCreator.profile);
            
            if (success)
            {
                Debug.Log("✅ GPU preview executed successfully!");
                Debug.Log("👀 Check Scene View for visual output:");
                Debug.Log("   - Magenta: Normal compute shader output");
                Debug.Log("   - Red: Empty spine data (spine binding fix test)");
                Debug.Log("   - Blue: Zero coordinates (spine binding fix test)");
            }
            else
            {
                Debug.LogError("❌ GPU preview failed!");
            }
            
            // 刷新场景视图
            SceneView.RepaintAll();
            
            Debug.Log("=== GPU Preview Test Completed ===");
        }
        
        [MenuItem("MrPath/Debug/Check Spine Data")]
        public static void CheckSpineData()
        {
            var pathCreator = Object.FindObjectOfType<PathCreator>();
            if (pathCreator == null)
            {
                Debug.LogError("No PathCreator found!");
                return;
            }
            
            var heightProvider = new MrPathV2.Runtime.Providers.TerrainHeightProvider();
            var spine = MrPathV2.Runtime.Core.PathSampler.SamplePath(pathCreator, heightProvider);
            if (spine.VertexCount == 0)
            {
                Debug.LogError("No spine data!");
                return;
            }
            
            Debug.Log($"Spine vertex count: {spine.VertexCount}");
            
            if (spine.VertexCount > 0)
            {
                var firstVertex = spine.Points[0];
                var firstTangent = spine.Tangents[0];
                var firstNormal = spine.SurfaceNormals[0];
                Debug.Log($"First vertex: Position={firstVertex}, Tangent={firstTangent}, Normal={firstNormal}");
                
                if (spine.VertexCount > 1)
                {
                    var secondVertex = spine.Points[1];
                    var secondTangent = spine.Tangents[1];
                    var secondNormal = spine.SurfaceNormals[1];
                    Debug.Log($"Second vertex: Position={secondVertex}, Tangent={secondTangent}, Normal={secondNormal}");
                }
            }
        }
    }
}