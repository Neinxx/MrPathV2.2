// MrPathV2/Editor/Inspectors/StylizedRoadRecipeEditor.cs - Final Corrected Version

using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace MrPathV2
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : OdinEditor
    {
        public static event Action<StylizedRoadRecipe> OnRecipeModified;


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
        private readonly Dictionary<BlendMaskBase, Texture2D> _maskLUTCache = new Dictionary<BlendMaskBase, Texture2D>();

        // CPU 预览逻辑已移除

        private Type _selectedMaskType;
        private static readonly Type[] _maskTypes = FindAvailableMaskTypes();

        protected override void OnEnable()
        {
            base.OnEnable();
            _recipe = target as StylizedRoadRecipe;
            if (_maskTypes.Length > 0)
                _selectedMaskType = _maskTypes[0];


            // --- 预览材质初始化（静态共享）---
            if (_previewShader == null)
            {
                _previewShader = Shader.Find("MrPathV2/StylizedRoadPreview");
                if (_previewShader == null && !_missingShaderLogged)
                {
                    Debug.LogError("MrPath: 预览 Shader 'MrPathV2/StylizedRoadPreview' 未找到！请确认文件存在。");
                    _missingShaderLogged = true;
                }
            }

            if (_sharedPreviewMaterial == null && _previewShader != null)
            {
                _sharedPreviewMaterial = new Material(_previewShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _previewMaterial = _sharedPreviewMaterial; // 使用共享实例，避免重复创建/销毁

            this.Tree.OnPropertyValueChanged += (prop, path) => OnRecipeModified?.Invoke(_recipe);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_previewRT != null) _previewRT.Release();


            // 不再销毁 _previewMaterial，因为它是静态共享实例
            foreach (var lut in _maskLUTCache.Values) DestroyImmediate(lut);
            _maskLUTCache.Clear();


        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            GUILayout.FlexibleSpace();

            SirenixEditorGUI.BeginBox();
            DrawPreview();
            EditorGUILayout.Space();
            DrawMaskCreator();
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
                foreach (var lut in _maskLUTCache.Values) DestroyImmediate(lut);
                _maskLUTCache.Clear();

                UpdatePreviewTexture();
                OnRecipeModified?.Invoke(_recipe);
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("最终效果预览", EditorStyles.boldLabel);

            // GPU 预览不再需要模式切换工具栏
            // 直接计算并绘制 GPU 预览结果
            Rect previewRect = GUILayoutUtility.GetRect(0, 180, GUILayout.ExpandWidth(true));

            if (_lastRecipeHash == -1) UpdatePreviewTexture();

            if (_previewRT != null)
            {
                GUI.DrawTexture(previewRect, _previewRT, ScaleMode.StretchToFill, false);
            }

            float centerY = previewRect.y + previewRect.height * 0.5f;
            if (_showCenterLine)
            {
                EditorGUI.DrawRect(new Rect(previewRect.x, centerY - 0.5f, previewRect.width, 1f), new Color(1, 1, 1, 0.5f));
            }

            const string helpText = "基于地形贴图和遮罩混合的最终道路效果预览 (GPU 加速)。";
            EditorGUILayout.HelpBox(helpText, MessageType.None);
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
            if (_previewRT == null || _previewRT.width != 512 || _previewRT.height != 256)
            {
                if (_previewRT != null) _previewRT.Release();
                _previewRT = new RenderTexture(512, 256, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
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
                var pli = new PreviewPipelineUtility.PreviewLayerInfo(
                    layer.terrainLayer.diffuseTexture as Texture2D ?? Texture2D.whiteTexture,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(layer.opacity * recipe.masterOpacity),
                    layer.blendMode,
                    layer.mask);
                // Build single-layer LUT (only R channel used)
                Texture2D maskLUT = PreviewPipelineUtility.BuildMaskLUT(null, new List<PreviewPipelineUtility.PreviewLayerInfo> { pli }, previewWorldWidth);
                 _previewMaterial.SetVector("_LayerTiling", new Vector4(tiling.x, tiling.y, 0, 0));
                 _previewMaterial.SetColor("_LayerTint", Color.white);
                 _previewMaterial.SetFloat("_LayerOpacity", pli.opacity);
                 _previewMaterial.SetFloat("_BlendMode", (float)pli.blendMode);
                 _previewMaterial.SetTexture("_LayerTex", pli.texture);
                 _previewMaterial.SetTexture("_MaskLUT", maskLUT);

                 if (i % 2 == 0)
                 {
                     _previewMaterial.SetTexture("_PreviousResultTex", rt1);
                     Graphics.Blit(rt1, rt2, _previewMaterial);
                 }
                 else
                 {
                     _previewMaterial.SetTexture("_PreviousResultTex", rt2);
                     Graphics.Blit(rt2, rt1, _previewMaterial);
                 }
            }

            // ... 结尾部分不变 ...
            Graphics.Blit(activeLayers.Count % 2 != 0 ? rt2 : rt1, _previewRT);
            RenderTexture.ReleaseTemporary(rt1);
            RenderTexture.ReleaseTemporary(rt2);
        }

        // 根据配方（Recipe）的实际道路宽度，动态计算预览所使用的世界宽度。
        private static float GetPreviewWorldWidth(StylizedRoadRecipe recipe)
        {
            if (recipe == null) return 10f;          // 合理的默认值，避免 Null 引发异常
            return Mathf.Max(0.1f, recipe.width);   // 避免出现 0 带来的除零错误
        }

        private Texture2D GetOrCreateMaskLUT(BlendMaskBase mask, float previewWorldWidth)
        {
            if (_maskLUTCache.TryGetValue(mask, out Texture2D lut))
            {
                return lut;
            }

            lut = new Texture2D(256, 1, TextureFormat.RFloat, false);
            lut.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float pos = Mathf.Lerp(-1f, 1f, i / 255f);

                // 【修改】将 PREVIEW_WORLD_WIDTH 传递给 Evaluate 方法

                float value = mask.Evaluate(pos, previewWorldWidth);

                pixels[i] = new Color(value, 0, 0, 0);
            }
            lut.SetPixels(pixels);
            lut.Apply(false);
            _maskLUTCache[mask] = lut;
            return lut;
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

                    if (layer.mask != null)
                    {
                        hash = hash * 23 + JsonUtility.ToJson(layer.mask).GetHashCode();
                    }
                }
                return hash;
            }
        }

        private void DrawMaskCreator()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_maskTypes.Length > 0)
                {
                    int currentIndex = Array.IndexOf(_maskTypes, _selectedMaskType);
                    var typeNames = _maskTypes.Select(t => t.Name).ToArray();
                    int newIndex = EditorGUILayout.Popup(new GUIContent("遮罩列表"), currentIndex, typeNames);
                    if (newIndex != currentIndex) _selectedMaskType = _maskTypes[newIndex];
                }
                if (GUILayout.Button("创建遮罩", GUILayout.Width(100))) { CreateMaskAsset(); }
            }
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
            Debug.Log($"MrPath: 已在 {uniquePath} 创建新的 {_selectedMaskType.Name} 资产。");
        }
        private static Type[] FindAvailableMaskTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
        }
        private float Blend(float baseValue, float layerValue, BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Normal: return layerValue;
                case BlendMode.Add: return Mathf.Clamp01(baseValue + layerValue);
                case BlendMode.Multiply: return baseValue * layerValue;
                default: return layerValue;
            }
        }
        #endregion
    }
}