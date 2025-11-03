using System;
using System.Collections.Generic;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Preview;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using BlendMode = __temp.MrPathV2.Runtime.Core.BlendMode;
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
        private static readonly int LayerCount = PreviewShaderContracts.Properties.LayerCount;
        private static readonly int PreviewAlpha = PreviewShaderContracts.Properties.PreviewAlpha;
        private static readonly int OpaquePreview = PreviewShaderContracts.Properties.OpaquePreview;
        private static readonly int MaskAtlas = PreviewShaderContracts.Properties.MaskAtlas;
        private static readonly int AtlasInvHeight = PreviewShaderContracts.Properties.AtlasInvHeight;
        private static readonly int LayerTex = PreviewShaderContracts.Properties.LayerTex;
        private static readonly int LayerTiling = PreviewShaderContracts.Properties.LayerTiling;
        private static readonly int LayerTint = PreviewShaderContracts.Properties.LayerTint;
        private static readonly int LayerOpacity = PreviewShaderContracts.Properties.LayerOpacity;
        private static readonly int Mode = PreviewShaderContracts.Properties.BlendMode;
        private static readonly int LayerTilingsArr = PreviewShaderContracts.Properties.LayerTilingsArr;
        private static readonly int LayerOpacitiesArr = PreviewShaderContracts.Properties.LayerOpacitiesArr;
        private static readonly int LayerBlendModesArr = PreviewShaderContracts.Properties.LayerBlendModesArr;
        private static readonly int PathSamplesId = PreviewShaderContracts.Properties.PathSamples;
        private static readonly int LayerIndexId = PreviewShaderContracts.Properties.LayerIndex;
        private static readonly int MaskStrengthId = PreviewShaderContracts.Properties.MaskStrength;
        private static readonly int ZTestId = PreviewShaderContracts.Properties.ZTest;
        private static readonly int MaskThresholdId = PreviewShaderContracts.Properties.MaskThreshold;
        private static readonly int MeshRepeatAcrossId = PreviewShaderContracts.Properties.MeshRepeatAcross;
        private static readonly int MeshRepeatAlongId = PreviewShaderContracts.Properties.MeshRepeatAlong;
        private static readonly int AcrossScaleId = PreviewShaderContracts.Properties.AcrossScale;
        private static readonly int LayerTexturesId = PreviewShaderContracts.Properties.LayerTextures;
        private static readonly int UseLayerTexArrayId = PreviewShaderContracts.Properties.UseLayerTexArray;
        private static readonly int PrevResultTexId = PreviewShaderContracts.Properties.PrevResultTex;

        private readonly List<Material> m_CachedList = new List<Material>(1);
        private bool m_Dirty = true;
        private ShaderFlavor m_Flavor = ShaderFlavor.Unknown;

        private int m_LastHash = -1;

        // Cached combined mask LUT (RGBA channels for up to 4 layers)
        // private Texture2D _maskLUT;
        private static Texture2D m_TransparentPrevTex; // 1x1 RGBA(0,0,0,0)

        private static Texture2D EnsureTransparentPrevTex()
        {
            if (m_TransparentPrevTex) return m_TransparentPrevTex;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            tex.name = "__PreviewTransparentPrevTex";
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            tex.Apply(false, false);
            m_TransparentPrevTex = tex;
            return m_TransparentPrevTex;
        }
        // Future: cached 2D mask atlas
        private Texture2D m_MaskAtlas;
        // 新增：图层贴图数组的缓存与生命周期管理
        private Texture2DArray m_LayerTexArray;
        private RenderTexture m_LayerRtArray;

        // 新增：纹理数组缓存机制
        private struct TextureArrayCacheKey
        {
            public readonly int LayerCount;
            public readonly int Width;
            public readonly int Height;
            public readonly string TextureHashes; // 所有纹理的哈希值组合

            public TextureArrayCacheKey(int layerCount, int width, int height, string textureHashes)
            {
                this.LayerCount = layerCount;
                this.Width = width;
                this.Height = height;
                this.TextureHashes = textureHashes;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is TextureArrayCacheKey)) return false;
                var other = (TextureArrayCacheKey)obj;
                return LayerCount == other.LayerCount &&
                       Width == other.Width &&
                       Height == other.Height &&
                       TextureHashes == other.TextureHashes;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + LayerCount;
                    hash = hash * 31 + Width;
                    hash = hash * 31 + Height;
                    hash = hash * 31 + (TextureHashes?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }

        private TextureArrayCacheKey? m_CurrentCacheKey;
        private CommandBuffer m_ReusableCommandBuffer; // 复用CommandBuffer

        // 新增：记录路径长度供构建 MaskAtlas 使用
        private float m_PathLength = -1f;
        private bool m_Disposed;

        public Material Current { get; private set; }

        // 公共参数推送：统一控制预览的通用属性
        private void PushCommonPreviewParams(PathProfile profile, int layerCount, float alpha)
        {
            var recipe = profile.roadRecipe;
            var master = recipe?.masterOpacity ?? 1f;
        
            // 层数与不透明逻辑
            Current.SetInt(LayerCount, Mathf.Max(1, layerCount));
            var isOpaque = profile.opaquePreview; // 移除单层自动不透明，避免矩形遮盖
            Current.SetFloat(PreviewAlpha, isOpaque ? 1f : Mathf.Clamp01(alpha));
            Current.SetFloat(OpaquePreview, isOpaque ? 1f : 0f);
        
            // 遮罩强度与阈值（与不透明预览联动）
            Current.SetFloat(MaskStrengthId, master);
            var maskThreshold = isOpaque ? 0.2f : 0.0f;
            if (Current.HasProperty(MaskThresholdId)) Current.SetFloat(MaskThresholdId, maskThreshold);
        
            // 深度测试、路径采样数等通用属性
            if (Current.HasProperty(ZTestId)) Current.SetInt(ZTestId, profile.enableDepthTest ? 4 : 8);
            Current.SetFloat(PathSamplesId, 64f);
        
            // 统一 AcrossScale 与 MeshRepeat 默认映射，减少材质侧分散控制
            if (Current.HasProperty(AcrossScaleId)) Current.SetFloat(AcrossScaleId, 1f);
            if (Current.HasProperty(MeshRepeatAcrossId)) Current.SetFloat(MeshRepeatAcrossId, 1f);
            if (Current.HasProperty(MeshRepeatAlongId)) Current.SetFloat(MeshRepeatAlongId, 1f);
        }

        // 层参数推送：数组形式（tiling/opacity/blend），并优先绑定 Texture2DArray
        private int PushLayerParams(PathProfile profile)
        {
            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            var layerCount = layers?.Count ?? 0;
            if (layerCount == 0) layerCount = 1; // 至少一个图层

            var isMultiLayerShader = Current.shader.name.Contains("PathPreviewSplatMulti");
            var maxLayers = isMultiLayerShader ? 16 : 4;

            var tilingsArr = new Vector4[maxLayers];
            var opacitiesArr = new float[maxLayers];
            var blendModesArr = new float[maxLayers];

            // 收集纹理用于尝试构建 Texture2DArray
            var texList = new List<Texture2D>();
            int sliceCount = 0;
            int width = -1, height = -1;
            var textureHashBuilder = new System.Text.StringBuilder();
#if UNITY_2019_1_OR_NEWER
            var formatSet = false;
            var graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
#endif

            for (var i = 0; i < Mathf.Min(maxLayers, layerCount); i++)
            {
                TerrainLayer tl = null;
                var layerOpacity = 0f;
                var blendMode = BlendMode.Normal;

                if (layers != null && i < layers.Count)
                {
                    var rl = layers[i];
                    if (rl != null && rl.enabled)
                    {
                        tl = rl.contentLayer;
                        layerOpacity = rl.opacity;
                        blendMode = rl.blendMode;
                    }
                }

                // Tiling
                var t = (tl && tl.diffuseTexture) ? PreviewPipelineUtility.CalcLayerTiling(profile.roadWidth, tl) : Vector2.one;
                tilingsArr[i] = new Vector4(t.x, t.y, 0, 0);

                // Opacity/Blend
                var master = recipe?.masterOpacity ?? 1f;
                opacitiesArr[i] = layers == null || layers.Count == 0 ? 1f : Mathf.Clamp01(layerOpacity * master);
                blendModesArr[i] = (float)blendMode;

                // 推送每层颜色（TerrainLayer.specular 作为 tint）

                Current.SetColor($"_Layer{i}_Color", GetTerrainLayerTint(tl));

                // 尝试收集数组纹理
                var tex = tl && tl.diffuseTexture ? tl.diffuseTexture : null;
                if (tex)
                {
                    texList.Add(tex);
                    sliceCount++;
                    if (width < 0) { width = tex.width; height = tex.height; }
                    // 添加纹理哈希到缓存键
                    textureHashBuilder.Append(tex.GetInstanceID()).Append(",");
#if UNITY_2019_1_OR_NEWER
                    if (!formatSet) { graphicsFormat = tex.graphicsFormat; formatSet = true; }
#endif
                }
                else
                {
                    // 若任一层缺失纹理，则放弃数组方案（改用旧的逐层推送）
                    sliceCount = -1;
                    textureHashBuilder.Append("null,");
                }
            }

            // 推送数组属性（供着色器采样）
            Current.SetVectorArray(LayerTilingsArr, tilingsArr);
            Current.SetFloatArray(LayerOpacitiesArr, opacitiesArr);
            Current.SetFloatArray(LayerBlendModesArr, blendModesArr);

#if UNITY_EDITOR
            // 推送 GPU 预览所需的 splat 索引数组
            var layerCountForIndices = isMultiLayerShader ? Mathf.Min(maxLayers, layerCount) : Mathf.Min(4, layerCount);
            var splatIndicesArr = new float[maxLayers];
            for (var i = 0; i < maxLayers; i++) splatIndicesArr[i] = -1f;
            if (EnableGpuPreview && m_TargetTerrain && recipe)
            {
                var map = LayerResolver.ResolveEnsurePresent(m_TargetTerrain, recipe);
                if (map != null && layers != null)
                {
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

            // 优先使用数组路径：只要所有层都有纹理即可（尺寸允许不一致，GPU Blit 自动缩放到首个纹理尺寸）
            var canUseArray = sliceCount > 0;

            // 检查缓存是否可用
            var textureHashes = textureHashBuilder.ToString();
            var newCacheKey = new TextureArrayCacheKey(sliceCount, width, height, textureHashes);
            var canReuseCache = m_CurrentCacheKey.HasValue && m_CurrentCacheKey.Value.Equals(newCacheKey) && m_LayerRtArray != null && m_LayerRtArray.IsCreated();

            if (canUseArray)
            {
                try
                {
                    // 如果缓存可用，直接使用
                    if (canReuseCache)
                    {
                        if (Current.HasProperty(LayerTexturesId)) Current.SetTexture(LayerTexturesId, m_LayerRtArray);
                        if (Current.HasProperty(UseLayerTexArrayId)) Current.SetFloat(UseLayerTexArrayId, 1f);
                    }
                    else
                    {
                        // 释放旧数组（包含 Texture2DArray 与 RenderTexture 数组）
                        ReleaseLayerTexArray();

                        // 使用 RenderTexture Tex2DArray 作为统一目标，GPU Blit 解码/缩放
                        var desc = new RenderTextureDescriptor(width, height)
                        {
                            graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                            dimension = TextureDimension.Tex2DArray,
                            volumeDepth = texList.Count,
                            enableRandomWrite = false,
                            useMipMap = false,
                            msaaSamples = 1
                        };
                        m_LayerRtArray = new RenderTexture(desc)
                        {
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Repeat,
                            name = "Preview_Layers_RTArray"
                        };
                        if (!m_LayerRtArray.Create())
                        {
                            throw new Exception("Failed to create RenderTexture Tex2DArray.");
                        }

                        // 复用CommandBuffer避免频繁创建
                        if (m_ReusableCommandBuffer == null)
                        {
                            m_ReusableCommandBuffer = new CommandBuffer { name = "BuildLayerRTArray" };
                        }
                        else
                        {
                            m_ReusableCommandBuffer.Clear();
                        }

                        for (var slice = 0; slice < texList.Count; slice++)
                        {
                            m_ReusableCommandBuffer.SetRenderTarget(m_LayerRtArray, 0, CubemapFace.Unknown, slice);
                            m_ReusableCommandBuffer.ClearRenderTarget(false, true, Color.white);
                            m_ReusableCommandBuffer.Blit(texList[slice], BuiltinRenderTextureType.CurrentActive);
                        }
                        Graphics.ExecuteCommandBuffer(m_ReusableCommandBuffer);
                        // 不释放CommandBuffer，保留复用

                        // 更新缓存键
                        m_CurrentCacheKey = newCacheKey;

                        if (Current.HasProperty(LayerTexturesId)) Current.SetTexture(LayerTexturesId, m_LayerRtArray);
                        if (Current.HasProperty(UseLayerTexArrayId)) Current.SetFloat(UseLayerTexArrayId, 1f);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PreviewMaterialManager] 构建 Texture2DArray 失败，已回退逐层绑定。原因: {e.Message}");
                    if (Current.HasProperty(UseLayerTexArrayId)) Current.SetFloat(UseLayerTexArrayId, 0f);
                    if (Current.HasProperty(LayerTexturesId)) Current.SetTexture(LayerTexturesId, null);
                    // 回退：逐层绑定已有纹理，缺失则使用白纹理
                    var maxLayersToBind = Mathf.Min(maxLayers, layerCount);
                    for (var i = 0; i < maxLayersToBind; i++)
                    {
                        TerrainLayer tl = null;
                        if (layers != null && i < layers.Count)
                        {
                            var rl = layers[i];
                            if (rl != null && rl.enabled)
                            {
                                tl = rl.contentLayer;
                            }
                        }
                        SetLayer(i, tl, profile.roadWidth);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[PreviewMaterialManager] Texture2DArray 不可用或存在缺失纹理，已回退逐层绑定。");
                if (Current.HasProperty(UseLayerTexArrayId)) Current.SetFloat(UseLayerTexArrayId, 0f);
                if (Current.HasProperty(LayerTexturesId)) Current.SetTexture(LayerTexturesId, null);
                var maxLayersToBind = Mathf.Min(maxLayers, layerCount);
                for (var i = 0; i < maxLayersToBind; i++)
                {
                    TerrainLayer tl = null;
                    if (layers != null && i < layers.Count)
                    {
                        var rl = layers[i];
                        if (rl != null && rl.enabled)
                        {
                            tl = rl.contentLayer;
                        }
                    }
                    SetLayer(i, tl, profile.roadWidth);
                }
            }

            return layerCount;
        }

        public List<Material> GetRenderMaterials()
        {
            if (m_Dirty)
            {
                m_CachedList.Clear();
                if (Current) m_CachedList.Add(Current);
                m_Dirty = false;
            }
            return m_CachedList;
        }

#if UNITY_EDITOR
        /// <summary>
        ///     设置当前预览所关联的 Terrain（用于从 <see cref="Gpu Preview Cache" /> 获取缓存的 alphamap RenderTextureArray）
        /// </summary>
        /// <param name="terrain">目标 Terrain</param>
        public void SetTargetTerrain(UnityEngine.Terrain terrain)
        {
            m_TargetTerrain = terrain;
        }
#endif

        // 新增：供外部推送路径长度与Mesh重复参数
        public void SetPathLength(float length)
        {
            m_PathLength = length;
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
            if (m_Disposed) return;
            if (!profile || !template)
            {
                Clear();
                return;
            }

            var newHash = CalculateHash(profile, template, previewAlpha);
            var needRefresh = newHash != m_LastHash || Current == null;
            m_LastHash = newHash;

            if (needRefresh)
            {
                EnsureMaterial(template);
                RefreshMaterial(profile, previewAlpha);
            }

#if UNITY_EDITOR
            // 绑定（或解除）GPU 实时预览纹理
            TryBindGpuPreview(profile);
#endif

            m_Dirty = true;
        }


        private void ApplySplat(PathProfile profile, float alpha)
        {
            // 推送层参数（含数组/fallback）
            var layerCount = PushLayerParams(profile);
            // 推送通用预览参数（不透明、阈值、深度等）
            PushCommonPreviewParams(profile, layerCount, alpha);
            // 准备遮罩纹理（MaskAtlas 或 GPU 权重）
            SetupMaskTextures(profile);
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
                // 无有效图层则绑定黑纹理，确保着色器透明（不产生矩形遮盖）
                if (Current.HasProperty(MaskAtlas))
                {
                    Current.SetTexture(MaskAtlas, Texture2D.blackTexture);
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
                    GetTerrainLayerTint(tLayer),
                    Mathf.Clamp01(roadLayer.opacity * recipe.masterOpacity),
                    roadLayer.blendMode,
                    roadLayer.layerMask);
                layerInfos.Add(info);
            }
        
            if (layerInfos.Count == 0)
            {
                // Fallback to black texture when nothing to draw so preview stays transparent
                Current.SetTexture(MaskAtlas, Texture2D.blackTexture);
                Current.SetFloat(AtlasInvHeight, 1f);
                return;
            }
        
            // 使用外部推送的真实路径长度；若未知则采用保守默认
            var effectivePathLength = m_PathLength > 0f ? m_PathLength : 100f;
            m_MaskAtlas = PreviewPipelineUtility.BuildMaskAtlas(m_MaskAtlas, layerInfos, worldWidth, effectivePathLength);
            if (!Current.HasProperty(MaskAtlas)) return;
            Current.SetTexture(MaskAtlas, m_MaskAtlas ?? Texture2D.blackTexture);
            Current.SetFloat(AtlasInvHeight, m_MaskAtlas && m_MaskAtlas.height > 0 ? 1f / m_MaskAtlas.height : 1f);
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
                Current.SetColor(LayerTint, GetTerrainLayerTint(layer));
            }
            else
            {
                Current.SetTexture(LayerTex, Texture2D.whiteTexture);
                Current.SetVector(LayerTiling, Vector4.one);
                Current.SetColor(LayerTint, GetTerrainLayerTint(layer));
            }

            // 关键：为风格化预览提供透明的上一帧结果，避免默认 blackTexture 的 Alpha=1 导致整片矩形
            if (Current.HasProperty(PrevResultTexId))
            {
                Current.SetTexture(PrevResultTexId, EnsureTransparentPrevTex());
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

            // 统一 AcrossScale 与 MeshRepeat 默认映射
            if (Current.HasProperty(AcrossScaleId)) Current.SetFloat(AcrossScaleId, 1f);
            if (Current.HasProperty(MeshRepeatAcrossId)) Current.SetFloat(MeshRepeatAcrossId, 1f);
            if (Current.HasProperty(MeshRepeatAlongId)) Current.SetFloat(MeshRepeatAlongId, 1f);

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
                    Current.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
                }
                else
                {
                    Current.SetTexture($"_Layer{index}_Texture", Texture2D.whiteTexture);
                    Current.SetColor($"_Layer{index}_Color", GetTerrainLayerTint(layer));
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
        private UnityEngine.Terrain m_TargetTerrain;

// 从 Terrain 中已加载的 Layer 解析 Tint 颜色（URP 的 DiffuseRemapMax）
private Color GetTerrainLayerTint(TerrainLayer layer)
{
    try
    {
        if (!layer) return Color.white;
        TerrainLayer source = layer;
#if UNITY_EDITOR
        if (m_TargetTerrain && m_TargetTerrain.terrainData && m_TargetTerrain.terrainData.terrainLayers != null)
        {
            var loaded = m_TargetTerrain.terrainData.terrainLayers;
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] == layer)
                {
                    source = loaded[i];
                    break;
                }
            }
        }
#endif
#if UNITY_2019_1_OR_NEWER
        var max = source.diffuseRemapMax; // Vector4
        return new Color(max.x, max.y, max.z, 1f);
#else
        return Color.white;
#endif
    }
    catch
    {
        return Color.white;
    }
}
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
            unchecked
            {
                var hash = 17;
                // 基本引用与显示选项
                hash = hash * 31 + (template?.GetHashCode() ?? 0);
                hash = hash * 31 + alpha.GetHashCode();
                if (profile != null)
                {
                    hash = hash * 31 + profile.enableDepthTest.GetHashCode();
                    hash = hash * 31 + profile.opaquePreview.GetHashCode();
                    hash = hash * 31 + profile.roadWidth.GetHashCode();
                }

                // 轻量哈希：收集 RoadRecipe / Layers / TerrainLayer / Mask 关键字段
                var recipe = profile?.roadRecipe;
                if (recipe == null) return hash;

                hash = hash * 31 + recipe.masterOpacity.GetHashCode();
                var layers = recipe.GetLayers();
                var count = layers != null ? layers.Count : 0;
                hash = hash * 31 + count.GetHashCode();
                if (layers == null || count == 0) return hash;

                for (int i = 0; i < count; i++)
                {
                    var rl = layers[i];
                    if (rl == null)
                    {
                        hash = hash * 31 + 0;
                        continue;
                    }

                    // RoadLayer 基本字段
                    hash = hash * 31 + rl.enabled.GetHashCode();
                    hash = hash * 31 + rl.opacity.GetHashCode();
                    hash = hash * 31 + rl.blendMode.GetHashCode();

                    // TerrainLayer 关键字段（影响贴图与平铺、色调）
                    var tl = rl.contentLayer;
                    if (tl)
                    {
                        try
                        {
                            hash = hash * 31 + tl.GetInstanceID();
                            var tex = tl.diffuseTexture;
                            hash = hash * 31 + (tex ? tex.GetInstanceID() : 0);
                            var sz = tl.tileSize;
                            var off = tl.tileOffset;
                            hash = hash * 31 + sz.x.GetHashCode();
                            hash = hash * 31 + sz.y.GetHashCode();
                            hash = hash * 31 + off.x.GetHashCode();
                            hash = hash * 31 + off.y.GetHashCode();
#if UNITY_2019_1_OR_NEWER
                            var max = tl.diffuseRemapMax;
                            hash = hash * 31 + max.x.GetHashCode();
                            hash = hash * 31 + max.y.GetHashCode();
                            hash = hash * 31 + max.z.GetHashCode();
#endif
                        }
                        catch { /* 避免异常导致刷新失败 */ }
                    }
                    else
                    {
                        hash = hash * 31 + 0;
                    }

                    // Mask 关键字段（不同类型覆盖不同参数）
                    var mask = rl.layerMask;
                    if (mask)
                    {
                        try
                        {
                            // 通用参数
                            hash = hash * 31 + mask.GetType().FullName.GetHashCode();
                            hash = hash * 31 + mask.smooth.GetHashCode();
                            hash = hash * 31 + mask.tiling.x.GetHashCode();
                            hash = hash * 31 + mask.tiling.y.GetHashCode();
                            hash = hash * 31 + mask.offset.x.GetHashCode();
                            hash = hash * 31 + mask.offset.y.GetHashCode();
                            hash = hash * 31 + mask.overallScale.GetHashCode();

                            // Procedural 基类（Strength/Seed）
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.ProceduralMaskBase proc)
                            {
                                hash = hash * 31 + proc.strength.GetHashCode();
                                hash = hash * 31 + proc.seed.GetHashCode();
                            }

                            // 噪声类
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.NoiseMask noise)
                            {
                                hash = hash * 31 + noise.noiseScale.x.GetHashCode();
                                hash = hash * 31 + noise.noiseScale.y.GetHashCode();
                                hash = hash * 31 + noise.uniformScale.GetHashCode();
                                hash = hash * 31 + noise.rotationDeg.GetHashCode();
                                hash = hash * 31 + noise.octaves.GetHashCode();
                                hash = hash * 31 + noise.lacunarity.GetHashCode();
                                hash = hash * 31 + noise.gain.GetHashCode();
                                hash = hash * 31 + noise.useAsymmetricEdges.GetHashCode();
                                hash = hash * 31 + noise.edgeLow.GetHashCode();
                                hash = hash * 31 + noise.edgeHigh.GetHashCode();
                            }
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.PerlinNoiseMask pnoise)
                            {
                                hash = hash * 31 + pnoise.noiseScale.x.GetHashCode();
                                hash = hash * 31 + pnoise.noiseScale.y.GetHashCode();
                                hash = hash * 31 + pnoise.uniformScale.GetHashCode();
                                hash = hash * 31 + pnoise.rotationDeg.GetHashCode();
                                hash = hash * 31 + pnoise.octaves.GetHashCode();
                                hash = hash * 31 + pnoise.lacunarity.GetHashCode();
                                hash = hash * 31 + pnoise.gain.GetHashCode();
                                hash = hash * 31 + pnoise.useAsymmetricEdges.GetHashCode();
                                hash = hash * 31 + pnoise.edgeLow.GetHashCode();
                                hash = hash * 31 + pnoise.edgeHigh.GetHashCode();
                            }
                            // 路肩遮罩
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.ShoulderMask shoulder)
                            {
                                hash = hash * 31 + shoulder.shoulderWidthRatio.GetHashCode();
                                hash = hash * 31 + shoulder.shoulderStrength.GetHashCode();
                                hash = hash * 31 + shoulder.edgeFalloff.GetHashCode();
                                hash = hash * 31 + shoulder.enableLeftShoulder.GetHashCode();
                                hash = hash * 31 + shoulder.enableRightShoulder.GetHashCode();
                            }
                        }
                        catch { /* 忽略异常以保证哈希过程健壮 */ }
                    }
                    else
                    {
                        hash = hash * 31 + 0;
                    }
                }

                return hash;
            }
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            Clear();
            m_Disposed = true;
        }
        private void Clear()
        {
            ReleaseMaterial();
            ReleaseMaskAtlas();
            ReleaseLayerTexArray();
            m_Dirty = true;
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
            if (m_MaskAtlas != null)
            {
                Object.DestroyImmediate(m_MaskAtlas);
                m_MaskAtlas = null;
            }
        }

        // 新增：释放图层贴图数组，避免资源泄漏
        private void ReleaseLayerTexArray()
        {
            if (m_LayerTexArray != null)
            {
                Object.DestroyImmediate(m_LayerTexArray);
                m_LayerTexArray = null;
            }
            if (m_LayerRtArray != null)
            {
                m_LayerRtArray.Release();
                Object.DestroyImmediate(m_LayerRtArray);
                m_LayerRtArray = null;
            }
            // 清除缓存键
            m_CurrentCacheKey = null;
        }

        /// <summary>
        /// 清理所有资源，包括CommandBuffer
        /// </summary>
        public void Cleanup()
        {
            ReleaseLayerTexArray();
            if (m_ReusableCommandBuffer != null)
            {
                m_ReusableCommandBuffer.Release();
                m_ReusableCommandBuffer = null;
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
                m_Flavor = DetectFlavor(Current.shader);
                return true;
            }
            return false;
        }

        private void RefreshMaterial(PathProfile profile, float previewAlpha)
        {
            switch (m_Flavor)
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
            var useGpu = EnableGpuPreview && m_TargetTerrain && profile && profile.roadRecipe;
            if (!useGpu)
            {
                if (Current.HasProperty(UseSplatWeightsID)) Current.SetInt(UseSplatWeightsID, 0);
                if (Current.HasProperty(SplatWeightsID)) Current.SetTexture(SplatWeightsID, null);
                return;
            }

            // 从缓存获取 RenderTextureArray
            if (EditorGpuPreviewCache.TryGet(m_TargetTerrain, out var rt) && rt)
            {
                // 绑定 GPU 生成的权重纹理
                if (Current.HasProperty(SplatWeightsID)) Current.SetTexture(SplatWeightsID, rt);
                if (Current.HasProperty(UseSplatWeightsID)) Current.SetInt(UseSplatWeightsID, 1);

                // 推送地形参数供着色器采样世界坐标
                var td = m_TargetTerrain.terrainData;
                var pos = m_TargetTerrain.GetPosition();
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
