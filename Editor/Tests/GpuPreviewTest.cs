using UnityEngine;
using UnityEditor;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.Preview;
using Unity.Mathematics;
using MrPathV2.Editor.Factories;

namespace MrPathV2.Editor.Tests
{
    /// <summary>
    /// 测试脚本：手动触发GPU预览系统以验证spine数据绑定修复
    /// </summary>
    public static class GpuPreviewTest
    {
        [MenuItem("MrPath/Tests/Test GPU Preview System")]
        public static void TestGpuPreviewSystem()
        {
            Debug.Log("[GpuPreviewTest] Starting GPU preview system test...");

            // 查找场景中的PathCreator和Terrain
            var pathCreator = Object.FindObjectOfType<PathCreator>();
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();

            if (pathCreator == null)
            {
                Debug.LogError("[GpuPreviewTest] No PathCreator found in scene. Please create one first.");
                return;
            }

            if (terrain == null)
            {
                Debug.LogError("[GpuPreviewTest] No Terrain found in scene. Please create one first.");
                return;
            }

            if (pathCreator.profile == null)
            {
                Debug.LogError("[GpuPreviewTest] PathCreator has no profile assigned.");
                return;
            }

            Debug.Log($"[GpuPreviewTest] Found PathCreator: {pathCreator.name}, Profile: {pathCreator.profile.name}");
            Debug.Log($"[GpuPreviewTest] Found Terrain: {terrain.name}");

            // 获取spine数据 - 使用PathSampler来生成spine
            var heightProvider = new MrPathV2.Runtime.Providers.TerrainHeightProvider();
            var spine = MrPathV2.Runtime.Core.PathSampler.SamplePath(pathCreator, heightProvider);
            if (spine.VertexCount < 2)
            {
                Debug.LogError($"[GpuPreviewTest] Invalid spine data. Vertex count: {spine.VertexCount}");
                return;
            }

            Debug.Log($"[GpuPreviewTest] Spine has {spine.VertexCount} vertices");

            // 启用GPU预览
            PreviewMaterialManager.EnableGpuPreview = true;
            Debug.Log("[GpuPreviewTest] GPU preview enabled");

            // 手动触发GPU预览运行
            bool success = GpuPreviewRunner.TryRun(terrain, spine, pathCreator.profile);

            if (success)
            {
                Debug.Log("[GpuPreviewTest] ✅ GPU preview system executed successfully!");
                Debug.Log("[GpuPreviewTest] Check the scene view for magenta debug output from the compute shader.");
                Debug.Log("[GpuPreviewTest] If spine data is empty, you should see red color.");
                Debug.Log("[GpuPreviewTest] If first spine point coordinates are zero, you should see blue color.");
            }
            else
            {
                Debug.LogError("[GpuPreviewTest] ❌ GPU preview system failed to execute.");
            }

            // 强制刷新场景视图
            SceneView.RepaintAll();
        }

        [MenuItem("MrPath/Tests/Create Test PathCreator")]
        public static void CreateTestPathCreator()
        {
            Debug.Log("[GpuPreviewTest] Creating test PathCreator...");
            PathFactory.CreateDefaultPath();
            // // 创建PathCreator GameObject
            // var go = new GameObject("Test PathCreator");
            // var pathCreator = go.AddComponent<PathCreator>();

            // // 加载默认配置
            // var appearanceDefaults = AssetDatabase.LoadAssetAtPath<MrPathV2.Editor.Settings.MrPathAppearanceDefaults>(
            //     "Assets/__temp/MrPathV2/Settings/AppearanceDefaults/MrPath_AppearanceDefaults.asset");

            // if (appearanceDefaults?.defaultPathProfile != null)
            // {
            //     pathCreator.profile = appearanceDefaults.defaultPathProfile;
            //     Debug.Log($"[GpuPreviewTest] Assigned default profile: {pathCreator.profile.name}");
            // }
            // else
            // {
            //     Debug.LogWarning("[GpuPreviewTest] No default profile found. Please assign one manually.");
            // }

            // // 设置简单的路径数据
            // pathCreator.pathData.AddKnot(new float3(0, 0, 0), new float3(1, 0, 0), new float3(-1, 0, 0));
            // pathCreator.pathData.AddKnot(new float3(0, 0, 5), new float3(1, 0, 5), new float3(-1, 0, 5));
            // pathCreator.pathData.AddKnot(new float3(0, 0, 10), new float3(1, 0, 10), new float3(-1, 0, 10));

            // // 选中新创建的对象
            // Selection.activeGameObject = go;

            Debug.Log("[GpuPreviewTest] ✅ Test PathCreator created successfully!");
        }

        [MenuItem("MrPath/Tests/Create Test Terrain")]
        public static void CreateTestTerrain()
        {
            Debug.Log("[GpuPreviewTest] Creating test Terrain...");

            // 创建TerrainData
            var terrainData = new TerrainData();
            terrainData.heightmapResolution = 513;
            terrainData.size = new Vector3(100, 30, 100);

            // 加载测试用的TerrainLayer
            var layer1 = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/__temp/MrPathV2/Data/testLayer.terrainlayer");
            var layer2 = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/__temp/MrPathV2/Data/testLayer 1.terrainlayer");

            if (layer1 != null && layer2 != null)
            {
                terrainData.terrainLayers = new TerrainLayer[] { layer1, layer2 };
                Debug.Log("[GpuPreviewTest] Assigned terrain layers");
            }
            else
            {
                Debug.LogWarning("[GpuPreviewTest] Test terrain layers not found. Creating basic terrain without layers.");
            }

            // 创建Terrain GameObject
            var terrainGO = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            terrainGO.name = "Test Terrain";

            // 选中新创建的地形
            Selection.activeGameObject = terrainGO;

            Debug.Log("[GpuPreviewTest] ✅ Test Terrain created successfully!");
        }
    }
}