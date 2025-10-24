using System;
using System.Collections.Generic;
using __temp.MrPathV2._2.Runtime.Core;
using UnityEngine;
#if UNITY_EDITOR
using EditorGpuPreviewCache = __temp.MrPathV2._2.Editor.Terrain.GpuPreviewCache;
#endif

namespace __temp.MrPathV2._2.Runtime.Preview
{

    /// <summary>
    /// Wraps a single instanced material used by preview mesh rendering and keeps it up-to-date with the current profile/template.
    /// Supports PathPreviewSplatMulti shader (multi-layer preview).
    /// </summary>
    public sealed class PreviewMaterialManager : IDisposable
    {
        private static readonly int LayerCount = Shader.PropertyToID("_LayerCount");
        private static readonly int PreviewAlpha = Shader.PropertyToID("_PreviewAlpha");
        private static readonly int OpaquePreview = Shader.PropertyToID("_OpaquePreview");
        private static readonly int MasterOpacity = Shader.PropertyToID("_MasterOpacity");
        private static readonly int EdgeFadeStart = Shader.PropertyToID("_EdgeFadeStart");
        private static readonly int EdgeFadeEnd = Shader.PropertyToID("_EdgeFadeEnd");
        private static readonly int MaskAtlas = Shader.PropertyToID("_MaskAtlas");
        private static readonly int AtlasInvHeight = Shader.PropertyToID("_AtlasInvHeight");
        private static readonly int LayerTex = Shader.PropertyToID("_LayerTex");
        private static readonly int LayerTiling = Shader.PropertyToID("_LayerTiling");
        private static readonly int LayerTint = Shader.PropertyToID("_LayerTint");
        private static readonly int LayerOpacity = Shader.PropertyToID("_LayerOpacity");
        private static readonly int Mode = Shader.PropertyToID("_BlendMode");
        private static readonly int LayerTilingsArr = Shader.PropertyToID("_LayerTilings");
        private static readonly int LayerOpacitiesArr = Shader.PropertyToID("_LayerOpacities");
        private static readonly int LayerBlendModesArr = Shader.PropertyToID("_LayerBlendModes");
        private static readonly int PathSamplesId = Shader.PropertyToID("_PathSamples");
        private static readonly int LayerIndexId = Shader.PropertyToID("_LayerIndex");
        private static readonly int MaskStrengthId = Shader.PropertyToID("_MaskStrength");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        // 新增：Mesh UV 重复参数（用于遮罩与采样自适应）
        private static readonly int MeshRepeatAcrossId = Shader.PropertyToID("_MeshRepeatAcross");
        private static readonly int MeshRepeatAlongId = Shader.PropertyToID("_MeshRepeatAlong");
        // 新增：GPU 预览相关属性
#if UNITY_EDITOR
        private static readonly int SplatWeightsID = Shader.PropertyToID("_SplatWeights");
        private static readonly int UseSplatWeightsID = Shader.PropertyToID("_UseSplatWeights");
        private static readonly int TerrainPositionID = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeID = Shader.PropertyToID("_TerrainSize");
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int LayerSplatIndicesArr = Shader.PropertyToID("_LayerSplatIndices");
#endif

        private enum ShaderFlavor { Splat, Stylized, Unknown }

        private Material _instance;
        private ShaderFlavor _flavor = ShaderFlavor.Unknown;
        private int _lastHash = -1;
        private bool _dirty = true;

        // Cached combined mask LUT (RGBA channels for up to 4 layers)
        // private Texture2D _maskLUT;
        // Future: cached 2D mask atlas
        private Texture2D _maskAtlas;

        // 新增：记录路径长度供构建 MaskAtlas 使用
        private float _pathLength = -1f;

        private readonly List<Material> _cachedList = new(1);

        public Material Current => _instance;

        public List<Material> GetRenderMaterials()
        {
            if (_dirty)
            {
                _cachedList.Clear();
                if (_instance != null) _cachedList.Add(_instance);
                _dirty = false;
            }
            return _cachedList;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 设置当前预览所关联的 Terrain（用于从 <see cref="__temp.MrPathV2._2.Editor.Terrain.GpuPreviewCache"/> 获取缓存的 alphamap RenderTextureArray）
        /// </summary>
        /// <param name="terrain">目标 Terrain</param>
        public void SetTargetTerrain(UnityEngine.Terrain terrain)
        {
            _targetTerrain = terrain;
        }
#endif

        // GPU 预览目标 Terrain（仅在 Editor 环境下使用）
#if UNITY_EDITOR
        private UnityEngine.Terrain _targetTerrain;
        /// <summary>
        /// 全局开关：是否启用 GPU 实时预览。
        /// 后续可替换为 ProjectSettings / ScriptableObject 配置。
        /// </summary>
        public static bool EnableGpuPreview = true;
#endif

        // 新增：供外部推送路径长度与Mesh重复参数
        public void SetPathLength(float length)
        {
            _pathLength = length;
        }

        public void SetMeshRepeats(float across, float along)
        {
            if (_instance == null) return;
            if (_instance.HasProperty(MeshRepeatAcrossId)) _instance.SetFloat(MeshRepeatAcrossId, Mathf.Max(1e-4f, across));
            if (_instance.HasProperty(MeshRepeatAlongId)) _instance.SetFloat(MeshRepeatAlongId, Mathf.Max(1e-4f, along));
        }

        // 恢复 Update 方法（被前一次编辑移除），保持材质刷新与GPU绑定逻辑
        public void Update(PathProfile profile, Material template, float previewAlpha)
        {
            if (profile == null || template == null)
            {
                Clear();
                return;
            }

            var newHash = CalculateHash(profile, template, previewAlpha);
            if (newHash == _lastHash && _instance != null) return;
            _lastHash = newHash;

            if (_instance == null || _instance.shader != template.shader)
            {
                Clear();
                _instance = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
                _flavor = DetectFlavor(_instance.shader);
            }

            switch (_flavor)
            {
                case ShaderFlavor.Splat:
                    ApplySplat(profile, previewAlpha);
                    break;
                case ShaderFlavor.Stylized:
                    ApplyStylized(profile);
                    break;
            }

#if UNITY_EDITOR
            // Editor-only GPU preview binding with graceful fallback
            if (_instance && _instance.HasProperty(SplatWeightsID))
            {
                if (EnableGpuPreview && _targetTerrain &&
                    EditorGpuPreviewCache.TryGet(_targetTerrain, out var rt) && rt != null)
                {
                    // Bind cached alphamap array for GPU blending
                    _instance.SetTexture(SplatWeightsID, rt);
                    _instance.SetInt(UseSplatWeightsID, 1);

                    var td = _targetTerrain.terrainData;
                    if (td)
                    {
                        var tpos = _targetTerrain.GetPosition();
                        _instance.SetVector(TerrainPositionID, new Vector4(tpos.x, tpos.z, 0f, 0f));
                        _instance.SetVector(TerrainSizeID, new Vector4(td.size.x, td.size.z, 0f, 0f));
                        _instance.SetVector(AlphamapResolutionID, new Vector4(td.alphamapResolution, td.alphamapResolution, 0f, 0f));
                    }

                    Debug.Log($"[PreviewMaterialManager] GPU Preview enabled - binding cached RT for terrain {_targetTerrain.name}");
                }
                else
                {
                    // Disable GPU weights usage; shader will fallback to mask atlas path
                    _instance.SetInt(UseSplatWeightsID, 0);
                    _instance.SetTexture(SplatWeightsID, null);
                    Debug.Log($"[PreviewMaterialManager] GPU Preview disabled or no cached RT - falling back to mask atlas. EnableGpuPreview: {EnableGpuPreview}, HasTerrain: {_targetTerrain != null}");
                }
            }
#endif

            _dirty = true;
        }

        private void ApplySplat(PathProfile profile, float alpha)
        {
            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            var layerCount = layers?.Count ?? 0;

            // 检测是否使用多层着色器
            var isMultiLayerShader = _instance.shader.name.Contains("PathPreviewSplatMulti");
            var maxLayers = isMultiLayerShader ? 16 : 4;

            // 当没有任何启用的图层时，仍然为着色器提供一个占位图层，
            // 以避免 _LayerCount 为 0 导致渲染结果完全透明。
            if (layerCount == 0)
            {
                layerCount = 1; // 至少一个图层
            }

            // ================= 新增：准备数组以批量推送到着色器 =================
            Vector4[] tilingsArr = new Vector4[maxLayers];
            float[] opacitiesArr = new float[maxLayers];
            float[] blendModesArr = new float[maxLayers];

            // 设置所有层（最多16层），确保与StylizedRoadRecipe配方一致
            for (var i = 0; i < maxLayers; i++)
            {
                TerrainLayer layer = null;
                var layerOpacity = 0f;
                var blendMode = BlendMode.Normal;
                var tilingVec = Vector4.one;

                if (layers != null && i < layers.Count)
                {
                    var roadLayer = layers[i];
                    if (roadLayer != null && roadLayer.enabled)
                    {
                        layer = roadLayer.contentLayer;
                        layerOpacity = roadLayer.opacity;
                        blendMode = (BlendMode)roadLayer.blendMode;
                    }
                }


                if (layer == null)
                {
                    // 当内容纹理与遮罩均为空时，使用默认白纹理占位，继续处理后续图层
                    tilingVec = Vector4.one;
                    // 保留 layer 为 null 以便 SetLayer 写入默认纹理
                }

                // 现在确定layer不为null，检查diffuseTexture
                if (layer != null && layer.diffuseTexture != null)
                {
                    // 使用CalcLayerTiling计算值
                    var t = PreviewPipelineUtility.CalcLayerTiling(profile.roadWidth, layer);
                    tilingVec = new Vector4(t.x, t.y, 0, 0);
                }
                else
                {
                    // layer 为 null 或无纹理时使用默认值
                    tilingVec = Vector4.one;
                }

                // 保存到数组
                tilingsArr[i] = tilingVec;
                // 如果不存在任何有效图层，则保持不透明以避免全透明
                opacitiesArr[i] = layers == null || layers.Count == 0 ? 1f : Mathf.Clamp01(layerOpacity);
                blendModesArr[i] = (float)blendMode;

                // 旧版兼容：仍保留纹理与颜色写入；已删除单独 Opacity/BlendMode 写入，完全依赖数组
                SetLayer(i, layer, profile.roadWidth);
            }

            // == 推送新数组属性 ==
            _instance.SetVectorArray(LayerTilingsArr, tilingsArr);
            _instance.SetFloatArray(LayerOpacitiesArr, opacitiesArr);
            _instance.SetFloatArray(LayerBlendModesArr, blendModesArr);

#if UNITY_EDITOR
            // 推送每层在 Terrain 中的 splat 索引，供着色器从 _SplatWeights 采样正确的 slice/channel
            var layerCountForIndices = isMultiLayerShader ? Mathf.Min(maxLayers, layerCount) : Mathf.Min(4, layerCount);
            var splatIndicesArr = new float[maxLayers];
            for (int i = 0; i < maxLayers; i++) splatIndicesArr[i] = -1f;
            if (EnableGpuPreview && _targetTerrain && profile.roadRecipe)
            {
                var map = __temp.MrPathV2._2.Editor.Terrain.LayerResolver.Resolve(_targetTerrain, profile.roadRecipe, interactive: false);
                if (map != null && layers != null)
                {
                    // 保护：当 layers 为空时不进行索引访问
                    var safeCount = Mathf.Min(layerCountForIndices, layers.Count);
                    for (int i = 0; i < safeCount; i++)
                    {
                        var tl = layers[i]?.contentLayer;
                        if (tl && map.TryGetValue(tl, out var idx)) splatIndicesArr[i] = idx;
                    }
                }
            }
            if (_instance.HasProperty(LayerSplatIndicesArr)) _instance.SetFloatArray(LayerSplatIndicesArr, splatIndicesArr);
#endif

            // 设置层数
            _instance.SetInt(LayerCount, layerCount);

            var master = profile.roadRecipe?.masterOpacity ?? 1f;
            _instance.SetFloat(PreviewAlpha, Mathf.Clamp01(alpha * master));
            _instance.SetFloat(OpaquePreview, profile.opaquePreview ? 1f : 0f);
            _instance.SetFloat(MasterOpacity, master);
            _instance.SetFloat(EdgeFadeStart, 0.7f);
            _instance.SetFloat(EdgeFadeEnd, 1f);
            _instance.SetFloat(PathSamplesId, 64f);
            // 将整体不透明度同时推送到遮罩强度，用户可在Inspector调整PreviewAlpha或MasterOpacity
            _instance.SetFloat(MaskStrengthId, master);
            _instance.SetFloat(LayerIndexId, 0f);
            if (_instance.HasProperty(ZTestId)) _instance.SetInt(ZTestId, profile.enableDepthTest ? 4 : 8);
            // Prepare mask atlas texture even for stylized single-layer preview
            SetupMaskTextures(profile);

            // Generate and bind LUT so shader can sample accurate weights
            // 移除重复调用，统一由一次调用完成生成与绑定
            // SetupMaskTextures(profile); // 使用默认路径长度，实际应该从PathSpine获取
        }

        /// <summary>
        /// Generates or updates the mask atlas texture that stores per-layer mask weights.
        /// This replaces the legacy 1D RGBA LUT system and supports an arbitrary number of layers.
        /// </summary>
        private void SetupMaskTextures(PathProfile profile)
        {
            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            if (layers == null || layers.Count == 0)
            {
                // 无有效图层则绑定白纹理，确保着色器能正常工作
                if (_instance.HasProperty(MaskAtlas))
                {
                    _instance.SetTexture(MaskAtlas, Texture2D.whiteTexture);
                    _instance.SetFloat(AtlasInvHeight, 1f);
                }
                return;
            }

            // 收集层信息（不限制层数）
            List<PreviewPipelineUtility.PreviewLayerInfo> layerInfos = new(layers.Count);
            var worldWidth = Mathf.Max(0.1f, profile.roadWidth);

            foreach (var roadLayer in layers)
            {
                if (roadLayer is not { enabled: true }) continue;
                var tLayer = roadLayer.contentLayer;
                if (tLayer?.diffuseTexture is not { } tex) continue;

                var tiling = PreviewPipelineUtility.CalcLayerTiling(worldWidth, tLayer);
                var info = new PreviewPipelineUtility.PreviewLayerInfo(
                    tex,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(roadLayer.opacity * recipe.masterOpacity),
                    roadLayer.blendMode,
                    roadLayer.layerMask);
                layerInfos.Add(info);
            }

            if (layerInfos.Count == 0)
            {
                // Fallback to white texture when nothing to draw
                _instance.SetTexture(MaskAtlas, Texture2D.whiteTexture);
                _instance.SetFloat(AtlasInvHeight, 1f);
                return;
            }

            // 使用外部推送的真实路径长度；若未知则采用保守默认
            var effectivePathLength = _pathLength > 0f ? _pathLength : 100f;
            _maskAtlas = PreviewPipelineUtility.BuildMaskAtlas(_maskAtlas, layerInfos, worldWidth, effectivePathLength);
            if (!_instance.HasProperty(MaskAtlas)) return;
            _instance.SetTexture(MaskAtlas, _maskAtlas ?? Texture2D.whiteTexture);
            _instance.SetFloat(AtlasInvHeight, _maskAtlas && _maskAtlas.height > 0 ? 1f / _maskAtlas.height : 1f);
            _instance.SetFloat(PathSamplesId, 64f);
            // Stylized shader expects layer index uniform (always 0 for single-layer preview)
            _instance.SetFloat(LayerIndexId, 0f);
        }

        private void ApplyStylized(PathProfile profile)
        {
            var layersList = profile.roadRecipe?.GetLayers();
            TerrainLayer layer = (layersList != null && layersList.Count > 0) ? layersList[0]?.contentLayer : null;
            if (layer?.diffuseTexture)
            {
                _instance.SetTexture(LayerTex, layer.diffuseTexture);
                var sz = layer.tileSize;
                if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
                if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
                var tiling = LayerTilingUtility.CalcLayerTiling(profile.roadWidth, layer);
                _instance.SetVector(LayerTiling, new Vector4(tiling.x, tiling.y, 0, 0));
                _instance.SetColor(LayerTint, layer.specular); // assuming specular used as tint currently
            }
            else
            {
                _instance.SetTexture(LayerTex, Texture2D.whiteTexture);
                _instance.SetVector(LayerTiling, Vector4.one);
            }

            var master = profile.roadRecipe?.masterOpacity ?? 1f;
            _instance.SetFloat(LayerOpacity, master);
            _instance.SetFloat(MaskStrengthId, master);
            _instance.SetFloat(Mode, 0f);
            _instance.SetFloat(PathSamplesId, 64f);
            _instance.SetFloat(LayerIndexId, 0f);
            if (_instance.HasProperty(ZTestId)) _instance.SetInt(ZTestId, profile.enableDepthTest ? 4 : 8);
            // 新增：Stylized 预览也支持不透明预览
            if (_instance.HasProperty(OpaquePreview)) _instance.SetFloat(OpaquePreview, profile.opaquePreview ? 1f : 0f);

            // 确保单层预览也能获取遮罩贴图（0号层）以应用透明度渐变
            SetupMaskTextures(profile);
        }

        private void SetLayer(int index, TerrainLayer layer, float worldWidth)
        {
            if (layer?.diffuseTexture != null)
            {
                _instance.SetTexture($"_Layer{index}_Texture", layer.diffuseTexture);
                // Removed legacy _Layer{index}_Tiling; tiling is now provided via _LayerTilings array
                // var tiling = LayerTilingUtility.CalcLayerTiling(worldWidth, layer);
                // _instance.SetVector($"_Layer{index}_Tiling", new Vector4(tiling.x, tiling.y, 0, 0));
                _instance.SetColor($"_Layer{index}_Color", Color.white); // 使用白色保持与地形贴图一致
            }
            else
            {
                _instance.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                // _instance.SetVector($"_Layer{index}_Tiling", Vector4.one);
                _instance.SetColor($"_Layer{index}_Color", Color.white);
            }
        }



        #region Utilities

        private static ShaderFlavor DetectFlavor(Shader shader)
        {
            if (shader == null) return ShaderFlavor.Unknown;
            var name = shader.name;
            return name.Contains("StylizedRoadBlend") ? ShaderFlavor.Stylized :
                   name.Contains("PathPreviewSplatMulti") ? ShaderFlavor.Splat : ShaderFlavor.Unknown;
        }

        private static int CalculateHash(PathProfile profile, Material template, float alpha)
        {
#if UNITY_EDITOR
            // 在编辑器中，包含 StylizedRoadRecipe 的序列化数据哈希，
            // 以便在调整 BlendLayers 或 Mask 参数时能正确刷新材质。
#endif
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (profile?.GetHashCode() ?? 0);
                hash = hash * 31 + (template?.GetHashCode() ?? 0);
                hash = hash * 31 + alpha.GetHashCode();
                if (profile != null)
                {
                    hash = hash * 31 + profile.enableDepthTest.GetHashCode();
                }

                // Profile 中的路面配方可能在 Inspector 中发生了修改，
                // 仅依赖引用哈希不足以检测到内部字段变化，这里通过序列化为 JSON 的方式
                // 将其所有序列化字段纳入哈希计算，保证任何属性调整都会触发刷新。
#if UNITY_EDITOR
                if (profile?.roadRecipe != null)
                {
                    // Include recipe itself
                    var json = UnityEditor.EditorJsonUtility.ToJson(profile.roadRecipe);
                    hash = hash * 31 + json.GetHashCode();

                    // Additionally include embedded mask assets so tweaking their parameters triggers refresh
                    var layers = profile.roadRecipe.GetLayers();
                    if (layers != null)
                    {
                        foreach (var roadLayer in layers)
                        {
                            var mask = roadLayer?.layerMask;
                            if (mask)
                            {
                                var maskJson = UnityEditor.EditorJsonUtility.ToJson(mask);
                                hash = hash * 31 + maskJson.GetHashCode();
                            }
                        }
                    }
                }
#else
                // 在运行时只使用引用哈希，避免额外的字符串分配成本
                hash = hash * 31 + (profile?.roadRecipe?.GetHashCode() ?? 0);
#endif
                return hash;
            }
        }
        public void Dispose() => Clear();
        private void Clear()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            if (_maskAtlas != null)
            {
                UnityEngine.Object.DestroyImmediate(_maskAtlas);
                _maskAtlas = null;
            }
            _dirty = true;
        }



        #endregion
    }
}