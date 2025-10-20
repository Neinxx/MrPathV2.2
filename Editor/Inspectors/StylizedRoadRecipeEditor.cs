using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using __temp.MrPathV2._2.Editor.Performance;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Inspectors
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : OdinEditor
    {
        public static event Action<StylizedRoadRecipe> OnRecipeModified;

        /// <summary>
        /// 手动触发Recipe修改事件，用于嵌入式编辑器
        /// </summary>
        public static void TriggerRecipeModified(StylizedRoadRecipe recipe)
        {
            OnRecipeModified?.Invoke(recipe);
        }


        private StylizedRoadRecipe _recipe;
        private int _lastRecipeHash;

        // --- GPU Resources ---
        private RenderTexture _previewRT;

        // 使用静态共享材质，避免每次打开 Inspector 都重新分配 GPU 资源
        private static Shader _previewShader;
        private bool _showCenterLine;
        private static Material _sharedPreviewMaterial;
        private Material _previewMaterial;
        private static bool _missingShaderLogged;
        // _maskLutCache 已弃用

        // CPU 预览逻辑已移除

        private Type _selectedMaskType;
        private static readonly Type[] MaskTypes = FindAvailableMaskTypes();
        private static readonly int LayerTiling = Shader.PropertyToID("_LayerTiling");
        private static readonly int LayerTint = Shader.PropertyToID("_LayerTint");
        private static readonly int LayerOpacity = Shader.PropertyToID("_LayerOpacity");
        private static readonly int Mode = Shader.PropertyToID("_BlendMode");
        private static readonly int LayerTex = Shader.PropertyToID("_LayerTex");
        private static readonly int MaskAtlas = Shader.PropertyToID("_MaskAtlas");
        private static readonly int AtlasInvHeight = Shader.PropertyToID("_AtlasInvHeight");
        private static readonly int PreviousResultTex = Shader.PropertyToID("_PreviousResultTex");

        protected override void OnEnable()
        {
            base.OnEnable();
            _recipe = target as StylizedRoadRecipe;
            if (MaskTypes.Length > 0)
                _selectedMaskType = MaskTypes[0];


            // --- 预览材质初始化（静态共享）---
            if (_previewShader == null)
            {
                _previewShader = Shader.Find("MrPathV2/StylizedRoadBlend");
                if (_previewShader == null && !_missingShaderLogged)
                {
                    Debug.LogError("MrPath: 预览 Shader 'MrPathV2/StylizedRoadBlend' 未找到！请确认文件存在。");
                    _missingShaderLogged = true;
                }
            }

            if (_sharedPreviewMaterial == null && _previewShader != null)
            {
                _sharedPreviewMaterial = new Material(_previewShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _previewMaterial = _sharedPreviewMaterial; // 使用共享实例，避免重复创建/销毁

            this.Tree.OnPropertyValueChanged += (_, _) => OnRecipeModified?.Invoke(_recipe);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_previewRT != null) _previewRT.Release();


            // 不再销毁 _previewMaterial，因为它是静态共享实例


        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            GUILayout.FlexibleSpace();

            SirenixEditorGUI.BeginBox();
            
            // 移除Inspector预览面板，用户现在可以在Scene视图中实时看到效果
            EditorGUILayout.HelpBox("道路预览已移至Scene视图，调节参数可实时查看效果。", MessageType.Info);
            
            EditorGUILayout.Space();
            DrawMaskCreator();
            
            // 添加性能分析面板
            MultiLayerPerformanceManager.DrawPerformanceInfo(_recipe);
            
            SirenixEditorGUI.EndBox();

            CheckForChanges();
        }

        private void CheckForChanges()
        {
            int currentHash = ComputeRecipeHash(_recipe);
            if (currentHash != _lastRecipeHash)
            {
                _lastRecipeHash = currentHash;

                // 【FIX】Clear the LUT cache to force regeneration of mask textures

                // LUT 缓存已删除，无需额外处理

                UpdatePreviewTexture();
                OnRecipeModified?.Invoke(_recipe);
            }
        }

        #region Preview Generation

        private void UpdatePreviewTexture()
        {
            // 统一使用 GPU 预览
            if (_previewMaterial != null) GenerateCombinedPreviewGPU(_recipe);
            Repaint();
        }

        private void GenerateCombinedPreviewGPU(StylizedRoadRecipe recipe)
        {
            // 修改预览尺寸：使用更宽的比例以显示完整道路横截面（水平方向）
            // 640x160 提供4:1的宽高比，更适合显示道路横截面
            if (_previewRT == null || _previewRT.width != 640 || _previewRT.height != 160)
            {
                if (_previewRT != null) _previewRT.Release();
                _previewRT = new RenderTexture(640, 160, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
                _previewRT.Create();
            }

            float previewWorldWidth = GetPreviewWorldWidth(recipe);
            var activeLayers = recipe.blendLayers.Where(l => l.enabled && l.mask != null && l.terrainLayer != null).ToList();
            if (activeLayers.Count == 0)
            {
                // ... 清空 RT 的代码不变 ...
                var oldActive = RenderTexture.active;
                RenderTexture.active = _previewRT;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = oldActive;
                return;
            }

            // --- 乒乓渲染 ---
            RenderTexture rt1 = RenderTexture.GetTemporary(_previewRT.descriptor);
            RenderTexture rt2 = RenderTexture.GetTemporary(_previewRT.descriptor);

            Graphics.SetRenderTarget(rt1);
            GL.Clear(true, true, Color.clear);

            for (int i = 0; i < activeLayers.Count; i++)
            {
                var layer = activeLayers[i];
                // 使用 PreviewPipelineUtility 统一计算 tiling 并生成 LUT
                Vector2 tiling = PreviewPipelineUtility.CalcLayerTiling(previewWorldWidth, layer.terrainLayer);
                
                // 使用新的mask系统获取活动遮罩
                var activeMask = layer.GetActiveMask();
                
                var pli = new PreviewPipelineUtility.PreviewLayerInfo(
                    layer.terrainLayer.diffuseTexture ?? Texture2D.whiteTexture,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(layer.opacity * recipe.masterOpacity),
                    layer.blendMode,
                    activeMask);
                // Build single-layer Mask Atlas and configure material
                Texture2D maskAtlas = PreviewPipelineUtility.BuildMaskAtlas(null, new List<PreviewPipelineUtility.PreviewLayerInfo> { pli }, previewWorldWidth);

                _previewMaterial.SetVector(LayerTiling, new Vector4(tiling.x, tiling.y, 0, 0));
                _previewMaterial.SetColor(LayerTint, Color.white);
                _previewMaterial.SetFloat(LayerOpacity, pli.opacity);
                _previewMaterial.SetFloat(Mode, (float)pli.blendMode);
                _previewMaterial.SetTexture(LayerTex, pli.texture);

                if (_previewMaterial.HasProperty(MaskAtlas))
                {
                    _previewMaterial.SetTexture(MaskAtlas, maskAtlas);
                    _previewMaterial.SetFloat(AtlasInvHeight, 1f); // single row atlas
                }

                if (i % 2 == 0)
                {
                    _previewMaterial.SetTexture(PreviousResultTex, rt1);
                    Graphics.Blit(rt1, rt2, _previewMaterial);
                }
                else
                {
                    _previewMaterial.SetTexture(PreviousResultTex, rt2);
                    Graphics.Blit(rt2, rt1, _previewMaterial);
                }
            }

            // 将最终结果复制到预览 RT，并释放临时资源
            Graphics.Blit(activeLayers.Count % 2 != 0 ? rt2 : rt1, _previewRT);
            RenderTexture.ReleaseTemporary(rt1);
            RenderTexture.ReleaseTemporary(rt2);
        }


        // 根据配方（Recipe）的实际道路宽度，动态计算预览所使用的世界宽度。
        private static float GetPreviewWorldWidth(StylizedRoadRecipe recipe)
        {
            if (!recipe) return 10f;          // 合理地默认值，避免 Null 引发异常
            return Mathf.Max(0.1f, recipe.width);   // 避免出现 0 带来的除零错误
        }

        /// <summary>
        /// 【FIX】Filled in the missing CPU preview logic
        /// </summary>
        // (removed GenerateChannelsPreviewCPU method as GPU replaces it)

        
        private int ComputeRecipeHash(StylizedRoadRecipe r)
        {
            unchecked
            {
                int hash = 17;

                if (r == null) return hash;

                foreach (var layer in r.blendLayers)
                {
                    if (layer == null) continue;
                    hash = hash * 23 + layer.enabled.GetHashCode();
                    hash = hash * 23 + layer.opacity.GetHashCode();
                    hash = hash * 23 + layer.blendMode.GetHashCode();
                    hash = hash * 23 + (layer.terrainLayer != null ? layer.terrainLayer.GetInstanceID() : 0);

                    // 使用新的mask系统计算hash
                    hash = hash * 23 + layer.maskType.GetHashCode();
                    
                    var activeMask = layer.GetActiveMask();
                    if (activeMask != null)
                    {
                        hash = hash * 23 + JsonUtility.ToJson(activeMask).GetHashCode();
                    }
                }
                return hash;
            }
        }

        private void DrawMaskCreator()
        {
            EditorGUILayout.LabelField("快速创建遮罩", EditorStyles.boldLabel);
            
            using (new EditorGUILayout.HorizontalScope())
            {
                // 创建路肩遮罩按钮
                if (GUILayout.Button("创建路肩遮罩", GUILayout.Width(120)))
                {
                    CreateSpecificMaskAsset(typeof(ShoulderMask), "路肩遮罩");
                }
                
                // 创建路面遮罩按钮
                if (GUILayout.Button("创建路面遮罩", GUILayout.Width(120)))
                {
                    CreateSpecificMaskAsset(typeof(RoadSurfaceMask), "路面遮罩");
                }
            }
            
            EditorGUILayout.Space(5);
            
            // 通用遮罩创建器（保留原有功能）
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("通用遮罩:", GUILayout.Width(80));
                
                if (MaskTypes.Length > 0)
                {
                    int currentIndex = Array.IndexOf(MaskTypes, _selectedMaskType);
                    var typeNames = MaskTypes.Select(GetMaskTypeDisplayName).ToArray();
                    int newIndex = EditorGUILayout.Popup(currentIndex, typeNames);
                    if (newIndex != currentIndex) _selectedMaskType = MaskTypes[newIndex];
                }
                
                if (GUILayout.Button("创建", GUILayout.Width(60)))
                {
                    CreateMaskAsset();
                }
            }
        }
        private void CreateSpecificMaskAsset(Type maskType, string displayName)
        {
            if (maskType == null || !maskType.IsSubclassOf(typeof(BlendMaskBase)))
            {
                Debug.LogError($"无效的遮罩类型: {maskType?.Name}");
                return;
            }

            var newMask = CreateInstance(maskType);
            string recipePath = AssetDatabase.GetAssetPath(_recipe);
            string folder = Path.GetDirectoryName(recipePath) ?? "Assets";
            string masksFolder = Path.Combine(folder, "Masks");
            
            if (!AssetDatabase.IsValidFolder(masksFolder))
            {
                AssetDatabase.CreateFolder(folder, "Masks");
            }
            
            string assetPath = Path.Combine(masksFolder, $"{_recipe.name}_{maskType.Name}.asset").Replace("\\", "/");
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            
            AssetDatabase.CreateAsset(newMask, uniquePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMask);
            
            Debug.Log($"MrPath: 已创建新的{displayName}资产: {uniquePath}");
        }

        private string GetMaskTypeDisplayName(Type maskType)
        {
            if (maskType == typeof(ShoulderMask)) return "路肩遮罩";
            if (maskType == typeof(RoadSurfaceMask)) return "路面遮罩";
            if (maskType == typeof(GradientMask)) return "渐变遮罩";
            if (maskType == typeof(NoiseMask)) return "噪声遮罩";
            return maskType.Name;
        }

        private void CreateMaskAsset()
        {
            if (_selectedMaskType == null) { Debug.LogError("没有可用的遮罩类型！"); return; }
            var newMask = CreateInstance(_selectedMaskType);
            string recipePath = AssetDatabase.GetAssetPath(_recipe);
            string folder = Path.GetDirectoryName(recipePath) ?? "Assets";
            string masksFolder = Path.Combine(folder, "Masks");
            if (!AssetDatabase.IsValidFolder(masksFolder)) { AssetDatabase.CreateFolder(folder, "Masks"); }
            string assetPath = Path.Combine(masksFolder, $"{_recipe.name}_{_selectedMaskType.Name}.asset").Replace("\\", "/");
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(newMask, uniquePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMask);
            Debug.Log($"MrPath: 已在 {uniquePath} 创建新的 {GetMaskTypeDisplayName(_selectedMaskType)} 资产。");
        }
        private static Type[] FindAvailableMaskTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
        }

        #endregion
    }
}