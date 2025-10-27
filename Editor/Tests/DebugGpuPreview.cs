using UnityEngine;
using UnityEditor;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.Preview;
using MrPathV2.Editor.Terrain;

namespace MrPathV2.Editor.Tests
{
    public class DebugGpuPreview
    {
        [MenuItem("MrPath/Debug/Check GPU Preview Status")]
        public static void CheckGpuPreviewStatus()
        {
            Debug.Log("=== GPU Preview System Debug ===");
            
            // 检查全局开关
            Debug.Log($"EnableGpuPreview: {PreviewMaterialManager.EnableGpuPreview}");
            
            // 查找PathCreator和Terrain
            var pathCreator = Object.FindObjectOfType<PathCreator>();
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            
            Debug.Log($"PathCreator found: {pathCreator != null}");
            Debug.Log($"Terrain found: {terrain != null}");
            
            if (pathCreator == null || terrain == null)
            {
                Debug.LogError("Missing PathCreator or Terrain!");
                return;
            }
            
            Debug.Log($"PathCreator profile: {pathCreator.profile?.name}");
            Debug.Log($"Terrain name: {terrain.name}");
            Debug.Log($"Terrain data: {terrain.terrainData?.name}");
            
            // 检查缓存状态
            if (GpuPreviewCache.TryGet(terrain, out var cachedRT))
            {
                Debug.Log($"✅ Cached RenderTexture found: {cachedRT?.name}");
                Debug.Log($"   Size: {cachedRT?.width}x{cachedRT?.height}");
                Debug.Log($"   Depth: {cachedRT?.volumeDepth}");
                Debug.Log($"   Format: {cachedRT?.graphicsFormat}");
                Debug.Log($"   IsCreated: {cachedRT?.IsCreated()}");
            }
            else
            {
                Debug.LogWarning("❌ No cached RenderTexture found");
            }

            // 检查材质状态 - PathPreviewManager is accessed through PathEditorContext
            try
            {
                var editorContext = new MrPathV2.Editor.Inspectors.PathEditorContext(pathCreator);
                var previewManager = editorContext.PreviewManager;
                if (previewManager != null)
                {
                    Debug.Log("✅ PathPreviewManager found");
                    Debug.Log($"   IsActive: {previewManager.IsActive}");
                    Debug.Log($"   LatestSpine: {(previewManager.LatestSpine.HasValue ? "Available" : "None")}");
                    
                    // Force an update to ensure materials are refreshed
                    previewManager.Update(pathCreator, editorContext.HeightProvider);
                    
                    // Use reflection to access the private MaterialManager and get materials
                    var matMgrField = typeof(MrPathV2.Editor.Preview.PathPreviewManager).GetField("_matMgr", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (matMgrField != null)
                    {
                        var materialManager = matMgrField.GetValue(previewManager);
                        if (materialManager != null)
                        {
                            Debug.Log($"   MaterialManager type: {materialManager.GetType().Name}");
                            
                            // Check _targetTerrain field
                            var targetTerrainField = materialManager.GetType().GetField("_targetTerrain", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (targetTerrainField != null)
                            {
                                var targetTerrain = targetTerrainField.GetValue(materialManager) as UnityEngine.Terrain;
                                Debug.Log($"   _targetTerrain: {targetTerrain?.name ?? "null"}");
                            }
                            
                            // Check EnableGpuPreview static field
                            var enableGpuPreviewField = materialManager.GetType().GetField("EnableGpuPreview", 
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (enableGpuPreviewField != null)
                            {
                                var enableGpuPreview = (bool)enableGpuPreviewField.GetValue(null);
                                Debug.Log($"   EnableGpuPreview: {enableGpuPreview}");
                            }
                            
                            // Get materials using reflection
                            var getMaterialsMethod = materialManager.GetType().GetMethod("GetRenderMaterials");
                            if (getMaterialsMethod != null)
                            {
                                var materials = getMaterialsMethod.Invoke(materialManager, null) as System.Collections.Generic.List<UnityEngine.Material>;
                                Debug.Log($"   Render materials count: {materials?.Count ?? 0}");
                                
                                if (materials != null && materials.Count > 0)
                                {
                                    var mat = materials[0];
                                    Debug.Log($"   Material: {mat?.name}");
                                    Debug.Log($"   Shader: {mat?.shader?.name}");
                                    
                                    // Check if shader supports GPU preview properties
                                    var shader = mat.shader;
                                    if (shader != null)
                                    {
                                        Debug.Log($"   Shader property count: {shader.GetPropertyCount()}");
                                        for (int i = 0; i < shader.GetPropertyCount(); i++)
                                        {
                                            var propName = shader.GetPropertyName(i);
                                            if (propName.Contains("Splat") || propName.Contains("UseSplat"))
                                            {
                                                Debug.Log($"   Shader property: {propName} (type: {shader.GetPropertyType(i)})");
                                            }
                                        }
                                    }
                                    
                                    // 检查关键属性
                                    if (mat.HasProperty("_SplatWeights"))
                                    {
                                        var splatWeights = mat.GetTexture("_SplatWeights");
                                        Debug.Log($"   _SplatWeights: {splatWeights?.name ?? "null"}");
                                        if (splatWeights != null)
                                        {
                                            Debug.Log($"   _SplatWeights size: {splatWeights.width}x{splatWeights.height}");
                                        }
                                    }
                                    else
                                    {
                                        Debug.LogWarning("   Material does not have _SplatWeights property");
                                    }
                                    
                                    if (mat.HasProperty("_UseSplatWeights"))
                                    {
                                        var useSplatWeights = mat.GetInt("_UseSplatWeights");
                                        Debug.Log($"   _UseSplatWeights: {useSplatWeights}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning("   Material does not have _UseSplatWeights property");
                                    }
                                }
                            }
                            else
                            {
                                Debug.LogWarning("   GetRenderMaterials method not found on MaterialManager");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("   MaterialManager is null");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("   Could not access _matMgr field via reflection");
                    }
                }
                else
                {
                    Debug.LogWarning("❌ PathPreviewManager not found");
                }

                // Dispose the context to prevent memory leaks
                editorContext.Dispose();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"❌ Error accessing PathPreviewManager: {ex.Message}");
            }
            
            Debug.Log("=== Debug Complete ===");
        }
        
        [MenuItem("MrPath/Debug/Force GPU Preview Update")]
        public static void ForceGpuPreviewUpdate()
        {
            var pathCreator = Object.FindObjectOfType<PathCreator>();
            var terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            
            if (pathCreator == null || terrain == null)
            {
                Debug.LogError("Missing PathCreator or Terrain!");
                return;
            }
            
            Debug.Log("Forcing GPU preview update...");
            
            // 生成spine数据
            var heightProvider = new MrPathV2.Runtime.Providers.TerrainHeightProvider();
            var spine = MrPathV2.Runtime.Core.PathSampler.SamplePath(pathCreator, heightProvider);
            
            Debug.Log($"Generated spine with {spine.VertexCount} vertices");
            
            // 强制运行GPU预览
            var success = GpuPreviewRunner.TryRun(terrain, spine, pathCreator.profile);
            Debug.Log($"GPU preview run result: {success}");
            
            // 强制更新预览管理器
            try
            {
                var editorContext = new MrPathV2.Editor.Inspectors.PathEditorContext(pathCreator);
                var previewManager = editorContext.PreviewManager;
                if (previewManager != null)
                {
                    // 触发材质更新
                    previewManager.Update(pathCreator, heightProvider);
                    Debug.Log("PathPreviewManager updated");
                }
                else
                {
                    Debug.LogWarning("PathPreviewManager not found");
                }
                
                // Dispose the context to prevent memory leaks
                editorContext.Dispose();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error updating PathPreviewManager: {ex.Message}");
            }
            
            // 重新检查状态
            CheckGpuPreviewStatus();
        }
    }
}