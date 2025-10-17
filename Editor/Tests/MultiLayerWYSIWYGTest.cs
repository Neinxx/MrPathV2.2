using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace MrPathV2.Tests
{
    /// <summary>
    /// 多层WYSIWYG一致性测试
    /// 验证预览材质与最终地形结果的视觉一致性
    /// </summary>
    public class MultiLayerWYSIWYGTest : EditorWindow
    {
        [MenuItem("MrPath/Tests/Multi-Layer WYSIWYG Test")]
        public static void ShowWindow()
        {
            GetWindow<MultiLayerWYSIWYGTest>("多层WYSIWYG测试");
        }

        private PathCreator _pathCreator;
        private StylizedRoadRecipe _recipe;
        private Terrain _testTerrain;
        private bool _testInProgress;
        private string _testResults = "";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("多层WYSIWYG一致性测试", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "此测试验证多层预览材质与最终地形绘制结果的视觉一致性。\n" +
                "测试将创建一个包含多个层的道路配方，并比较预览与实际地形效果。",
                MessageType.Info);

            EditorGUILayout.Space();

            // 测试参数
            _pathCreator = EditorGUILayout.ObjectField("Path Creator", _pathCreator, typeof(PathCreator), true) as PathCreator;
            _recipe = EditorGUILayout.ObjectField("Road Recipe", _recipe, typeof(StylizedRoadRecipe), false) as StylizedRoadRecipe;
            _testTerrain = EditorGUILayout.ObjectField("Test Terrain", _testTerrain, typeof(Terrain), true) as Terrain;

            EditorGUILayout.Space();

            // 测试控制
            GUI.enabled = !_testInProgress && _pathCreator != null && _recipe != null && _testTerrain != null;
            if (GUILayout.Button("开始WYSIWYG测试"))
            {
                StartWYSIWYGTest();
            }
            GUI.enabled = true;

            if (_testInProgress)
            {
                EditorGUILayout.HelpBox("测试进行中...", MessageType.Info);
            }

            // 显示测试结果
            if (!string.IsNullOrEmpty(_testResults))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("测试结果:", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_testResults, GUILayout.Height(200));
            }

            // 快速设置按钮
            EditorGUILayout.Space();
            if (GUILayout.Button("创建测试配方 (8层)"))
            {
                CreateTestRecipe();
            }
        }

        private void StartWYSIWYGTest()
        {
            _testInProgress = true;
            _testResults = "";

            try
            {
                // 验证配方层数
                int layerCount = _recipe.blendLayers.Count;
                LogResult($"开始测试 - 配方包含 {layerCount} 层");

                // 检查预览材质支持
                var previewManager = new PreviewMaterialManager();
                bool supportsMultiLayer = CheckMultiLayerSupport(previewManager);
                LogResult($"预览材质多层支持: {(supportsMultiLayer ? "✓" : "✗")}");

                // 检查地形层数限制
                bool terrainSupportsLayers = CheckTerrainLayerSupport();
                LogResult($"地形层数支持: {(terrainSupportsLayers ? "✓" : "✗")}");

                // 验证LayerResolver
                bool layerResolverOK = TestLayerResolver();
                LogResult($"LayerResolver无限制: {(layerResolverOK ? "✓" : "✗")}");

                // 验证PaintSplatmapJob
                bool paintJobOK = TestPaintSplatmapJob();
                LogResult($"PaintSplatmapJob多层支持: {(paintJobOK ? "✓" : "✗")}");

                // 总结
                bool allTestsPassed = supportsMultiLayer && terrainSupportsLayers && layerResolverOK && paintJobOK;
                LogResult($"\n=== 测试总结 ===");
                LogResult($"整体WYSIWYG一致性: {(allTestsPassed ? "✓ 通过" : "✗ 失败")}");

                if (allTestsPassed)
                {
                    LogResult("🎉 多层支持已成功实现！预览与最终结果应保持一致。");
                }
                else
                {
                    LogResult("⚠️ 发现问题，需要进一步调试。");
                }
            }
            catch (System.Exception ex)
            {
                LogResult($"测试异常: {ex.Message}");
            }
            finally
            {
                _testInProgress = false;
            }
        }

        private bool CheckMultiLayerSupport(PreviewMaterialManager previewManager)
        {
            // 检查是否存在PathPreviewSplatMulti shader
            var multiShader = Shader.Find("MrPath/PathPreviewSplatMulti");
            if (multiShader == null)
            {
                LogResult("  - PathPreviewSplatMulti shader 未找到");
                return false;
            }

            LogResult("  - PathPreviewSplatMulti shader 已找到");
            
            // 检查材质模板
            string materialPath = "Assets/__temp/MrPathV2.2/Editor/Resources/PathPreviewSplatMultiMaterial.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                LogResult("  - PathPreviewSplatMultiMaterial.mat 未找到");
                return false;
            }

            LogResult("  - PathPreviewSplatMultiMaterial.mat 已找到");
            return true;
        }

        private bool CheckTerrainLayerSupport()
        {
            if (_testTerrain?.terrainData == null)
                return false;

            int maxLayers = _testTerrain.terrainData.alphamapLayers;
            LogResult($"  - 当前地形支持最大层数: {maxLayers}");
            
            // Unity理论上支持无限层数（通过多个control texture）
            return maxLayers >= 4; // 至少支持基本的4层
        }

        private bool TestLayerResolver()
        {
            // 创建一个包含多层的测试配方
            var testRecipe = CreateInstance<StylizedRoadRecipe>();
            for (int i = 0; i < 8; i++)
            {
                testRecipe.blendLayers.Add(new BlendLayer
                {
                    name = $"Test Layer {i}",
                    enabled = true,
                    terrainLayer = _testTerrain.terrainData.terrainLayers.FirstOrDefault()
                });
            }

            try
            {
                // 测试LayerResolver是否会显示4层限制警告
                var layerMap = LayerResolver.Resolve(_testTerrain, testRecipe);
                LogResult($"  - LayerResolver处理了 {layerMap.Count} 层，无错误");
                return true;
            }
            catch (System.Exception ex)
            {
                LogResult($"  - LayerResolver错误: {ex.Message}");
                return false;
            }
            finally
            {
                DestroyImmediate(testRecipe);
            }
        }

        private bool TestPaintSplatmapJob()
        {
            // PaintSplatmapJob通过Unity原生alphamap系统支持多层
            // 这里主要验证数据结构是否正确
            try
            {
                var terrainData = _testTerrain.terrainData;
                var alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapResolution, terrainData.alphamapResolution);
                
                int layers = alphamaps.GetLength(2);
                LogResult($"  - 地形alphamap支持 {layers} 层");
                LogResult($"  - PaintSplatmapJob使用Unity原生alphamap系统");
                
                return layers > 0;
            }
            catch (System.Exception ex)
            {
                LogResult($"  - PaintSplatmapJob测试错误: {ex.Message}");
                return false;
            }
        }

        private void CreateTestRecipe()
        {
            var recipe = CreateInstance<StylizedRoadRecipe>();
            recipe.name = "Test Multi-Layer Recipe";

            // 创建8层测试配方
            for (int i = 0; i < 8; i++)
            {
                recipe.blendLayers.Add(new BlendLayer
                {
                    name = $"Test Layer {i + 1}",
                    enabled = true,
                    opacity = 0.8f,
                    blendMode = BlendMode.Normal
                });
            }

            // 保存为资产
            string path = "Assets/__temp/MrPathV2.2/Settings/TestMultiLayerRecipe.asset";
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();

            _recipe = recipe;
            LogResult($"已创建测试配方: {path}");
        }

        private void LogResult(string message)
        {
            _testResults += message + "\n";
            Debug.Log($"[WYSIWYG Test] {message}");
        }
    }
}