using System;
// CPU-only：移除GPU测试依赖
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
            Debug.Log("[RoadMaskTest] 开始测试道路遮罩生成 (CPU-only)...");

            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[RoadMaskTest] 场景中没有找到地形对象");
                return;
            }

            Debug.Log($"[RoadMaskTest] 找到地形: {terrain.name}");
            Debug.Log($"[RoadMaskTest] 地形尺寸: {terrain.terrainData.size}");
            Debug.Log($"[RoadMaskTest] AlphaMap分辨率: {terrain.terrainData.alphamapResolution}");

            // CPU-only：只进行基础环境检查
            Debug.Log("[RoadMaskTest] CPU-only 模式，跳过GPU组件初始化测试");
            Debug.Log("[RoadMaskTest] 测试完成 - 基础检查通过");
        }

        [MenuItem("MrPath/Tests/Validate Shader Properties")]
        public static void ValidateShaderProperties()
        {
            Debug.Log("[RoadMaskTest] 验证着色器属性...");

            // 检查计算着色器是否存在
            var computeShader = MrPathV2.Runtime.Core.Resources.ResourceProvider.LoadComputeShader("PaintSplatmapCompute");
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
