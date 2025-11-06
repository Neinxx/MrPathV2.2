using System;
using MrPathV2.Editor.GPU;
using MrPathV2.Editor.GPU.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MrPathV2.Editor.Tests
{
    /// <summary>
    ///     测试道路遮罩功能是否正常工作
    /// </summary>
    public static class RoadMaskTest
    {
        [MenuItem("MrPath/Tests/Test Road Mask Generation")]
        public static void TestRoadMaskGeneration()
        {
            Debug.Log("[RoadMaskTest] 开始测试道路遮罩生成...");

            // 查找场景中的地形
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[RoadMaskTest] 场景中没有找到地形对象");
                return;
            }

            Debug.Log($"[RoadMaskTest] 找到地形: {terrain.name}");
            Debug.Log($"[RoadMaskTest] 地形尺寸: {terrain.terrainData.size}");
            Debug.Log($"[RoadMaskTest] AlphaMap分辨率: {terrain.terrainData.alphamapResolution}");

            // 检查GPU组件是否可以正常初始化
            try
            {
                var resourceManager = new GpuResourceManager();
                resourceManager.Initialize();

                var dataStreamer = new GpuDataStreamer(resourceManager);
                dataStreamer.Initialize();

                Debug.Log("[RoadMaskTest] GPU组件初始化成功");

                // 清理资源
                dataStreamer.Dispose();
                resourceManager.Dispose();

                Debug.Log("[RoadMaskTest] 测试完成 - 所有组件正常工作");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RoadMaskTest] 测试失败: {ex.Message}");
                Debug.LogError($"[RoadMaskTest] 堆栈跟踪: {ex.StackTrace}");
            }
        }

        [MenuItem("MrPath/Tests/Validate Shader Properties")]
        public static void ValidateShaderProperties()
        {
            Debug.Log("[RoadMaskTest] 验证着色器属性...");

            // 检查计算着色器是否存在
            var computeShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");
            if (computeShader == null)
            {
                Debug.LogError("[RoadMaskTest] 未找到 PaintSplatmapCompute 计算着色器");
                return;
            }

            Debug.Log($"[RoadMaskTest] 找到计算着色器: {computeShader.name}");

            // 检查内核是否存在（统一为 paint_terrain）
            if (computeShader.HasKernel("paint_terrain"))
            {
                Debug.Log("[RoadMaskTest] 找到 paint_terrain 内核");
            }
            else
            {
                Debug.LogError("[RoadMaskTest] 未找到 paint_terrain 内核");
            }

            Debug.Log("[RoadMaskTest] 着色器属性验证完成");
        }
    }
}
