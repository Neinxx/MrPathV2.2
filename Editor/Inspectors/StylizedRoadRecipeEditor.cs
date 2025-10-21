using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using __temp.MrPathV2._2.Editor.Performance;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using MrPathV2;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Inspectors
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : OdinEditor
    {
        /// <summary>
        /// 手动触发Recipe修改事件，用于嵌入式编辑器
        /// </summary>
        public static void TriggerRecipeModified(StylizedRoadRecipe recipe)
        {
            recipe?.RaiseRecipeChanged();
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
            _lastRecipeHash = _recipe?.GetHashCode() ?? 0;

            // 订阅属性变化事件，直接调用recipe的事件而不是静态事件
            Tree.OnPropertyValueChanged += (_, _) => _recipe?.RaiseRecipeChanged();
            
            if (MaskTypes.Length > 0)
                _selectedMaskType = MaskTypes[0];


            // --- 预览材质初始化（静态共享）---
            if (!_previewShader)
            {
                _previewShader = Shader.Find("MrPathV2/StylizedRoadBlend");
                if (!_previewShader && !_missingShaderLogged)
                {
                    Debug.LogError("MrPath: 预览 Shader 'MrPathV2/StylizedRoadBlend' 未找到！请确认文件存在。");
                    _missingShaderLogged = true;
                }
            }

            if (!_sharedPreviewMaterial && _previewShader)
            {
                _sharedPreviewMaterial = new Material(_previewShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _previewMaterial = _sharedPreviewMaterial; // 使用共享实例，避免重复创建/销毁

            this.Tree.OnPropertyValueChanged += (_, _) => _recipe?.RaiseRecipeChanged();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_previewRT) _previewRT.Release();


            // 不再销毁 _previewMaterial，因为它是静态共享实例


        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            GUILayout.FlexibleSpace();

            SirenixEditorGUI.BeginBox();
            
            // Scene preview tip
            // HelpBox removed per UI redesign
            
            // EditorGUILayout.Space();
            // DrawMaskCreator(); // Removed as per redesign
            
            // 移除性能分析面板调用
            // MultiLayerPerformanceManager.DrawPerformanceInfo(_recipe);
            
            SirenixEditorGUI.EndBox();

            CheckForChanges();
        }

        private void CheckForChanges()
        {
            var currentHash = ComputeRecipeHash(_recipe);
            if (currentHash == _lastRecipeHash) return;
            _lastRecipeHash = currentHash;

            // 【FIX】Clear the LUT cache to force regeneration of mask textures

            // LUT 缓存已删除，无需额外处理

            UpdatePreviewTexture();
            _recipe?.RaiseRecipeChanged();
        }

        #region Preview Generation

        private void UpdatePreviewTexture()
        {
            // 统一使用 GPU 预览
            if (_previewMaterial) GenerateCombinedPreviewGPU(_recipe);
            Repaint();
        }

        private void GenerateCombinedPreviewGPU(StylizedRoadRecipe recipe)
        {
            // 修改预览尺寸：使用更宽的比例以显示完整道路横截面（水平方向）
            // 640x160 提供4:1的宽高比，更适合显示道路横截面
            if (!_previewRT || _previewRT.width != 640 || _previewRT.height != 160)
            {
                if (_previewRT) _previewRT.Release();
                _previewRT = new RenderTexture(640, 160, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
                _previewRT.Create();
            }

            var previewWorldWidth = GetPreviewWorldWidth(recipe);
            var activeLayers = recipe.GetLayers().Where(l => l is { enabled: true } && l.layerMask && l.contentLayer).ToList();
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
            var rt1 = RenderTexture.GetTemporary(_previewRT.descriptor);
            var rt2 = RenderTexture.GetTemporary(_previewRT.descriptor);

            Graphics.SetRenderTarget(rt1);
            GL.Clear(true, true, Color.clear);

            for (var i = 0; i < activeLayers.Count; i++)
            {
                var layer = activeLayers[i];
                // 使用 PreviewPipelineUtility 统一计算 tiling 并生成 LUT
                var tiling = PreviewPipelineUtility.CalcLayerTiling(previewWorldWidth, layer.contentLayer);
                
                // 使用新的mask系统获取活动遮罩
                var activeMask = layer.layerMask;
                
                var pli = new PreviewPipelineUtility.PreviewLayerInfo(
                    layer.contentLayer != null ? layer.contentLayer.diffuseTexture : Texture2D.whiteTexture,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(layer.opacity * recipe.masterOpacity),
                    layer.blendMode,
                    activeMask);
                // Build single-layer Mask Atlas and configure material
                var maskAtlas = PreviewPipelineUtility.BuildMaskAtlas(null, new List<PreviewPipelineUtility.PreviewLayerInfo> { pli }, previewWorldWidth);

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
            return !recipe ? 10f : // 合理地默认值，避免 Null 引发异常
                Mathf.Max(0.1f, recipe.width); // 避免出现 0 带来的除零错误
        }

        /// <summary>
        /// 【FIX】Filled in the missing CPU preview logic
        /// </summary>
        // (removed GenerateChannelsPreviewCPU method as GPU replaces it)

        
        private static int ComputeRecipeHash(StylizedRoadRecipe r)
        {
            unchecked
            {
                var hash = 17;

                if (!r) return hash;

                foreach (var layer in r.GetLayers())
                {
                    if (layer == null) continue;
                    hash = hash * 23 + layer.enabled.GetHashCode();
                    hash = hash * 23 + layer.opacity.GetHashCode();
                    hash = hash * 23 + layer.blendMode.GetHashCode();
                    hash = hash * 23 + (layer.contentLayer != null ? layer.contentLayer.GetInstanceID() : 0);

                    // 使用新的mask系统计算hash (maskType removed)
                    // 删除旧的 maskType 和 GetActiveMask 逻辑，直接使用 layer.layerMask
                    if (layer.layerMask)
                    {
                        hash = hash * 23 + JsonUtility.ToJson(layer.layerMask).GetHashCode();
                    }
                    if (layer.layerMask)
                    {
                        hash = hash * 23 + JsonUtility.ToJson(layer.layerMask).GetHashCode();
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
                    var currentIndex = Array.IndexOf(MaskTypes, _selectedMaskType);
                    var typeNames = MaskTypes.Select(GetMaskTypeDisplayName).ToArray();
                    var newIndex = EditorGUILayout.Popup(currentIndex, typeNames);
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
            var recipePath = AssetDatabase.GetAssetPath(_recipe);
            var folder = Path.GetDirectoryName(recipePath) ?? "Assets";
            var masksFolder = Path.Combine(folder, "Masks");
            
            if (!AssetDatabase.IsValidFolder(masksFolder))
            {
                AssetDatabase.CreateFolder(folder, "Masks");
            }
            
            var assetPath = Path.Combine(masksFolder, $"{_recipe.name}_{maskType.Name}.asset").Replace("\\", "/");
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            
            AssetDatabase.CreateAsset(newMask, uniquePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMask);
            
            Debug.Log($"MrPath: 已创建新的{displayName}资产: {uniquePath}");
        }

        private static string GetMaskTypeDisplayName(Type maskType)
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
            var recipePath = AssetDatabase.GetAssetPath(_recipe);
            var folder = Path.GetDirectoryName(recipePath) ?? "Assets";
            var masksFolder = Path.Combine(folder, "Masks");
            if (!AssetDatabase.IsValidFolder(masksFolder)) { AssetDatabase.CreateFolder(folder, "Masks"); }
            var assetPath = Path.Combine(masksFolder, $"{_recipe.name}_{_selectedMaskType.Name}.asset").Replace("\\", "/");
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
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