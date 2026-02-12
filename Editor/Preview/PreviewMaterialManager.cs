using System;
using System.Collections.Generic;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using EditorGpuPreviewCache = MrPathV2.Editor.Terrain.GpuPreviewCache;
#endif

namespace MrPathV2.Editor.Preview
{

    /// <summary>
    ///     Wraps a single instanced material used by preview mesh rendering and keeps it up-to-date with the current
    ///     profile/template.
    ///     Supports PathPreviewSplatMulti shader (multi-layer preview).
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
        // 新增：Mask 阈值属性绑定，控制遮罩锐化阈值
        private static readonly int MaskThresholdId = Shader.PropertyToID("_MaskThreshold");
        // 新增：Mesh UV 重复参数（用于遮罩与采样自适应）
        private static readonly int MeshRepeatAcrossId = Shader.PropertyToID("_MeshRepeatAcross");
        private static readonly int MeshRepeatAlongId = Shader.PropertyToID("_MeshRepeatAlong");

        private readonly List<Material> _cachedList = new List<Material>(1);
        private bool _dirty = true;
        private ShaderFlavor _flavor = ShaderFlavor.Unknown;

        private int _lastHash = -1;

        // Cached combined mask LUT (RGBA channels for up to 4 layers)
        // private Texture2D _maskLUT;
        // Future: cached 2D mask atlas
        private Texture2D _maskAtlas;

        // 新增：记录路径长度供构建 MaskAtlas 使用
        private float _pathLength = -1f;
        private bool _disposed;

        public Material Current { get; private set; }

        public List<Material> GetRenderMaterials()
        {
            if (_dirty)
            {
                _cachedList.Clear();
                if (Current) _cachedList.Add(Current);
                _dirty = false;
            }
            return _cachedList;
        }

#if UNITY_EDITOR
        /// <summary>
        ///     设置当前预览所关联的 Terrain（用于从 <see cref="Gpu Preview Cache" /> 获取缓存的 alphamap RenderTextureArray）
        /// </summary>
        /// <param name="terrain">目标 Terrain</param>
        public void SetTargetTerrain(UnityEngine.Terrain terrain)
        {
            _targetTerrain = terrain;
        }
#endif

        // 新增：供外部推送路径长度与Mesh重复参数
        public void SetPathLength(float length)
        {
            _pathLength = length;
        }

        public void SetMeshRepeats(float across, float along)
        {
            if (!Current) return;
            if (Current.HasProperty(MeshRepeatAcrossId)) Current.SetFloat(MeshRepeatAcrossId, Mathf.Max(1e-4f, across));
            if (Current.HasProperty(MeshRepeatAlongId)) Current.SetFloat(MeshRepeatAlongId, Mathf.Max(1e-4f, along));
        }

        // 恢复 Update 方法（被前一次编辑移除），保持材质刷新与GPU绑定逻辑
        public void Update(PathProfile profile, Material template, float previewAlpha)
        {
            if (_disposed) return;
            if (!profile || !template)
            {
                Clear();
                return;
            }

            var newHash = CalculateHash(profile, template, previewAlpha);
            var needRefresh = newHash != _lastHash || Current == null;
            _lastHash = newHash;

            if (needRefresh)
            {
                EnsureMaterial(template);
                RefreshMaterial(profile, previewAlpha);
            }

#if UNITY_EDITOR
            // 绑定（或解除）GPU 实时预览纹理
            TryBindGpuPreview(profile);
#endif

            _dirty = true;
        }


        private void ApplySplat(PathProfile profile, float alpha)
        {
            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            var layerCount = layers?.Count ?? 0;

            // 检测是否使用多层着色器
            var isMultiLayerShader = Current.shader.name.Contains("PathPreviewSplatMulti");
            var maxLayers = isMultiLayerShader ? 16 : 4;

            // 当没有任何启用的图层时，仍然为着色器提供一个占位图层，
            // 以避免 _LayerCount 为 0 导致渲染结果完全透明。
            if (layerCount == 0)
            {
                layerCount = 1; // 至少一个图层
            }

            // ================= 新增：准备数组以批量推送到着色器 =================
            var tilingsArr = new Vector4[maxLayers];
            var opacitiesArr = new float[maxLayers];
            var blendModesArr = new float[maxLayers];

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
                        blendMode = roadLayer.blendMode;
                    }
                }


                if (!layer)
                {
                    // 当内容纹理与遮罩均为空时，使用默认白纹理占位，继续处理后续图层
                    tilingVec = Vector4.one;
                    // 保留 layer 为 null 以便 SetLayer 写入默认纹理
                }

                // 现在确定layer不为null，检查diffuseTexture
                if (layer && layer.diffuseTexture)
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
            Current.SetVectorArray(LayerTilingsArr, tilingsArr);
            Current.SetFloatArray(LayerOpacitiesArr, opacitiesArr);
            Current.SetFloatArray(LayerBlendModesArr, blendModesArr);

#if UNITY_EDITOR
            // 推送每层在 Terrain 中的 splat 索引，供着色器从 _SplatWeights 采样正确的 slice/channel
            var layerCountForIndices = isMultiLayerShader ? Mathf.Min(maxLayers, layerCount) : Mathf.Min(4, layerCount);
            var splatIndicesArr = new float[maxLayers];
            for (var i = 0; i < maxLayers; i++) splatIndicesArr[i] = -1f;
            if (EnableGpuPreview && _targetTerrain && profile.roadRecipe)
            {
                // 统一配置：GPU 预览下确保 Terrain 中存在所有配方图层，避免映射缺失导致权重采样为0
                var map = LayerResolver.ResolveEnsurePresent(_targetTerrain, profile.roadRecipe);
                if (map != null && layers != null)
                {
                    // 保护：当 layers 为空时不进行索引访问
                    var safeCount = Mathf.Min(layerCountForIndices, layers.Count);
                    for (var i = 0; i < safeCount; i++)
                    {
                        var tl = layers[i]?.contentLayer;
                        if (tl && map.TryGetValue(tl, out var idx)) splatIndicesArr[i] = idx;
                    }
                }
            }
            if (Current.HasProperty(LayerSplatIndicesArr)) Current.SetFloatArray(LayerSplatIndicesArr, splatIndicesArr);
#endif

            // 设置层数
            Current.SetInt(LayerCount, layerCount);

            var master = profile.roadRecipe?.masterOpacity ?? 1f;
            Current.SetFloat(PreviewAlpha, Mathf.Clamp01(alpha * master));
            Current.SetFloat(OpaquePreview, profile.opaquePreview ? 1f : 0f);
            Current.SetFloat(MasterOpacity, master);
            Current.SetFloat(EdgeFadeStart, 0.7f);
            Current.SetFloat(EdgeFadeEnd, 1f);
            Current.SetFloat(PathSamplesId, 64f);
            // 将整体不透明度同时推送到遮罩强度，用户可在Inspector调整PreviewAlpha或MasterOpacity
            Current.SetFloat(MaskStrengthId, master);
            // 新增：根据不透明预览设置遮罩阈值，避免回退到 MaskAtlas 时出现半透明边缘
            var maskThreshold = profile.opaquePreview ? 0.2f : 0.0f;
            if (Current.HasProperty(MaskThresholdId)) Current.SetFloat(MaskThresholdId, maskThreshold);
            Current.SetFloat(LayerIndexId, 0f);
            if (Current.HasProperty(ZTestId)) Current.SetInt(ZTestId, profile.enableDepthTest ? 4 : 8);
            // Prepare mask atlas texture even for stylized single-layer preview
            SetupMaskTextures(profile);

            // Generate and bind LUT so shader can sample accurate weights
            // 移除重复调用，统一由一次调用完成生成与绑定
            // SetupMaskTextures(profile); // 使用默认路径长度，实际应该从PathSpine获取
        }

        /// <summary>
        ///     Generates or updates the mask atlas texture that stores per-layer mask weights.
        ///     This replaces the legacy 1D RGBA LUT system and supports an arbitrary number of layers.
        /// </summary>
        private void SetupMaskTextures(PathProfile profile)
        {
            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            if (layers == null || layers.Count == 0)
            {
                // 无有效图层则绑定白纹理，确保着色器能正常工作
                if (Current.HasProperty(MaskAtlas))
                {
                    Current.SetTexture(MaskAtlas, Texture2D.whiteTexture);
                    Current.SetFloat(AtlasInvHeight, 1f);
                }
                return;
            }

            // 收集层信息（不限制层数）
            List<PreviewPipelineUtility.PreviewLayerInfo> layerInfos = new List<PreviewPipelineUtility.PreviewLayerInfo>(layers.Count);
            var worldWidth = Mathf.Max(0.1f, profile.roadWidth);

            foreach (var roadLayer in layers)
            {
                if (roadLayer is not { enabled: true }) continue;
                var tLayer = roadLayer.contentLayer;

                Texture2D tex = null;
                try
                {
                    if (tLayer && tLayer.diffuseTexture != null)
                    {
                        tex = tLayer.diffuseTexture;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PreviewMaterialManager] Failed to access TerrainLayer.diffuseTexture while building mask atlas: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
                if (tex == null) continue;

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
                Current.SetTexture(MaskAtlas, Texture2D.whiteTexture);
                Current.SetFloat(AtlasInvHeight, 1f);
                return;
            }

            // 使用外部推送的真实路径长度；若未知则采用保守默认
            var effectivePathLength = _pathLength > 0f ? _pathLength : 100f;
            _maskAtlas = PreviewPipelineUtility.BuildMaskAtlas(_maskAtlas, layerInfos, worldWidth, effectivePathLength);
            if (!Current.HasProperty(MaskAtlas)) return;
            Current.SetTexture(MaskAtlas, _maskAtlas ?? Texture2D.whiteTexture);
            Current.SetFloat(AtlasInvHeight, _maskAtlas && _maskAtlas.height > 0 ? 1f / _maskAtlas.height : 1f);
            Current.SetFloat(PathSamplesId, 64f);
            // Stylized shader expects layer index uniform (always 0 for single-layer preview)
            Current.SetFloat(LayerIndexId, 0f);
        }

        private void ApplyStylized(PathProfile profile)
        {
            var layersList = profile.roadRecipe?.GetLayers();
            var layer = layersList != null && layersList.Count > 0 ? layersList[0]?.contentLayer : null;

            Texture2D tex = null;
            try
            {
                if (layer && layer.diffuseTexture != null)
                {
                    tex = layer.diffuseTexture;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialManager] Failed to access TerrainLayer.diffuseTexture for stylized preview: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }

            if (tex != null)
            {
                Current.SetTexture(LayerTex, tex);
                var sz = layer.tileSize;
                if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
                if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;
                var tiling = LayerTilingUtility.CalcLayerTiling(profile.roadWidth, layer);
                Current.SetVector(LayerTiling, new Vector4(tiling.x, tiling.y, 0, 0));
                Current.SetColor(LayerTint, layer.specular); // assuming specular used as tint currently
            }
            else
            {
                Current.SetTexture(LayerTex, Texture2D.whiteTexture);
                Current.SetVector(LayerTiling, Vector4.one);
            }

            var master = profile.roadRecipe?.masterOpacity ?? 1f;
            Current.SetFloat(LayerOpacity, master);
            Current.SetFloat(MaskStrengthId, master);
            Current.SetFloat(Mode, 0f);
            Current.SetFloat(PathSamplesId, 64f);
            Current.SetFloat(LayerIndexId, 0f);
            if (Current.HasProperty(ZTestId)) Current.SetInt(ZTestId, profile.enableDepthTest ? 4 : 8);
            // 新增：Stylized 预览也支持不透明预览
            if (Current.HasProperty(OpaquePreview)) Current.SetFloat(OpaquePreview, profile.opaquePreview ? 1f : 0f);
            // 新增：为单层风格化预览设置遮罩阈值，保证边缘清晰
            var maskThresholdStylized = profile.opaquePreview ? 0.2f : 0.0f;
            if (Current.HasProperty(MaskThresholdId)) Current.SetFloat(MaskThresholdId, maskThresholdStylized);

            // 确保单层预览也能获取遮罩贴图（0号层）以应用透明度渐变
            SetupMaskTextures(profile);
        }

        private void SetLayer(int index, TerrainLayer layer, float worldWidth)
        {
            try
            {
                Texture2D tex = null;
                // Safely access TerrainLayer.diffuseTexture; Unity may throw if the asset is invalid/destroyed
                if (layer != null)
                {
                    // Guard against Unity's fake-null: use implicit bool check first
                    if (layer && layer.diffuseTexture != null)
                    {
                        tex = layer.diffuseTexture;
                    }
                }

                if (tex != null)
                {
                    Current.SetTexture($"_Layer{index}_Texture", tex);
                    Current.SetColor($"_Layer{index}_Color", Color.white);
                }
                else
                {
                    Current.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                    Current.SetColor($"_Layer{index}_Color", Color.white);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PreviewMaterialManager] Failed to read TerrainLayer at index {index}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Fallback to safe defaults so preview continues rendering
                Current.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                Current.SetColor($"_Layer{index}_Color", Color.white);
            }
        }

        private enum ShaderFlavor
        {
            Splat,
            Stylized,
            Unknown
        }
        // 新增：GPU 预览相关属性
#if UNITY_EDITOR
        private static readonly int SplatWeightsID = Shader.PropertyToID("_SplatWeights");
        private static readonly int UseSplatWeightsID = Shader.PropertyToID("_UseSplatWeights");
        private static readonly int TerrainPositionID = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeID = Shader.PropertyToID("_TerrainSize");
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int LayerSplatIndicesArr = Shader.PropertyToID("_LayerSplatIndices");
#endif

        // GPU 预览目标 Terrain（仅在 Editor 环境下使用）
#if UNITY_EDITOR
        private UnityEngine.Terrain _targetTerrain;
        /// <summary>
        ///     全局开关：是否启用 GPU 实时预览。
        ///     后续可替换为 ProjectSettings / ScriptableObject 配置。
        /// </summary>
        public static bool EnableGpuPreview = false;
#endif


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
                    // 新增：当切换不透明预览时强制刷新材质
                    hash = hash * 31 + profile.opaquePreview.GetHashCode();
                }

                // Profile 中的路面配方可能在 Inspector 中发生了修改，
                // 仅依赖引用哈希不足以检测到内部字段变化，这里通过序列化为 JSON 的方式
                // 将其所有序列化字段纳入哈希计算，保证任何属性调整都会触发刷新。
#if UNITY_EDITOR
                if (profile?.roadRecipe != null)
                {
                    // Include recipe itself
                    var json = EditorJsonUtility.ToJson(profile.roadRecipe);
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
                                var maskJson = EditorJsonUtility.ToJson(mask);
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
        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }
        private void Clear()
        {
            ReleaseMaterial();
            ReleaseMaskAtlas();
            _dirty = true;
        }

        private void ReleaseMaterial()
        {
            if (Current != null)
            {
                Object.DestroyImmediate(Current);
                Current = null;
            }
        }

        private void ReleaseMaskAtlas()
        {
            if (_maskAtlas != null)
            {
                Object.DestroyImmediate(_maskAtlas);
                _maskAtlas = null;
            }
        }

        private bool EnsureMaterial(Material template)
        {
            if (!template) return false;
            if (!Current || Current.shader != template.shader)
            {
                ReleaseMaterial();
                Current = new Material(template)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _flavor = DetectFlavor(Current.shader);
                return true;
            }
            return false;
        }

        private void RefreshMaterial(PathProfile profile, float previewAlpha)
        {
            switch (_flavor)
            {
                case ShaderFlavor.Splat:
                    ApplySplat(profile, previewAlpha);
                    break;
                case ShaderFlavor.Stylized:
                    ApplyStylized(profile);
                    break;
                default:
                    ApplySplat(profile, previewAlpha);
                    break;
            }
        }

#if UNITY_EDITOR
        private void TryBindGpuPreview(PathProfile profile)
        {
            if (!Current) return;

            // 默认走 MaskAtlas 回退路径
            var useGpu = EnableGpuPreview && _targetTerrain && profile && profile.roadRecipe;
            if (!useGpu)
            {
                if (Current.HasProperty(UseSplatWeightsID)) Current.SetInt(UseSplatWeightsID, 0);
                if (Current.HasProperty(SplatWeightsID)) Current.SetTexture(SplatWeightsID, null);
                return;
            }

            // 从缓存获取 RenderTextureArray
            if (EditorGpuPreviewCache.TryGet(_targetTerrain, out var rt) && rt)
            {
                // 绑定 GPU 生成的权重纹理
                if (Current.HasProperty(SplatWeightsID)) Current.SetTexture(SplatWeightsID, rt);
                if (Current.HasProperty(UseSplatWeightsID)) Current.SetInt(UseSplatWeightsID, 1);

                // 推送地形参数供着色器采样世界坐标
                var td = _targetTerrain.terrainData;
                var pos = _targetTerrain.GetPosition();
                var size = td.size;
                if (Current.HasProperty(TerrainPositionID)) Current.SetVector(TerrainPositionID, new Vector4(pos.x, pos.z, 0f, 0f));
                if (Current.HasProperty(TerrainSizeID)) Current.SetVector(TerrainSizeID, new Vector4(size.x, size.z, 0f, 0f));
                if (Current.HasProperty(AlphamapResolutionID))
                {
                    var res = td.alphamapResolution;
                    Current.SetVector(AlphamapResolutionID, new Vector4(res, res, 0f, 0f));
                }
            }
            else
            {
                // 无缓存：解除绑定，回退到 MaskAtlas
                if (Current.HasProperty(UseSplatWeightsID)) Current.SetInt(UseSplatWeightsID, 0);
                if (Current.HasProperty(SplatWeightsID)) Current.SetTexture(SplatWeightsID, null);
            }
        }
#endif

            #endregion
    }
}
