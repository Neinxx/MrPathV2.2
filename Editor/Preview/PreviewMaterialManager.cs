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
        private readonly List<Material> m_CachedList = new List<Material>(1);
        private bool m_Dirty = true;
        private ShaderFlavor m_Flavor = ShaderFlavor.Unknown;

        private int m_LastHash = -1;

        // Cached combined mask LUT (RGBA channels for up to 4 layers)
        // private Texture2D _maskLUT;
        private static Texture2D m_TransparentPrevTex; // 1x1 RGBA(0,0,0,0)

        // Future: cached 2D mask atlas
        private Texture2D m_MaskAtlas;
        private RenderTexture m_LayerRtArray;

        // Texture array cache mechanism
        private struct TextureArrayCacheKey
        {
            public readonly int LayerCount;
            public readonly int Width;
            public readonly int Height;
            public readonly string TextureHashes; // Hash combination of all textures

            public TextureArrayCacheKey(int layerCount, int width, int height, string textureHashes)
            {
                LayerCount = layerCount;
                Width = width;
                Height = height;
                TextureHashes = textureHashes;
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
        private CommandBuffer m_ReusableCommandBuffer; // Reusable CommandBuffer

        // Record path length for building MaskAtlas
        private float m_PathLength = -1f;
        private bool m_Disposed;

        public Material Current { get; private set; }

        // Public parameter push: uniformly control common preview properties
        private void PushCommonPreviewParams(PathProfile profile, int layerCount, float alpha)
        {
            if (Current == null) return;

            var parameterSetter = new PreviewMaterialParameterSetter(Current, profile);
            parameterSetter.SetCommonPreviewParameters(layerCount, alpha);
        }

        // Layer parameter push: array form (tiling/opacity/blend), and prefer to bind Texture2DArray
        private int PushLayerParams(PathProfile profile)
        {
            if (Current == null) return 0;

            var recipe = profile.roadRecipe;
            var layers = recipe?.GetLayers();
            var layerCount = layers?.Count ?? 0;
            if (layerCount == 0) layerCount = 1; // At least one layer

            var isMultiLayerShader = Current.shader.name.Contains("PathPreviewSplatMulti");
            var maxLayers = isMultiLayerShader ? 16 : 4;

            var parameterSetter = new PreviewMaterialParameterSetter(Current, profile);
            var processedLayerCount = parameterSetter.SetLayerParametersAsArrays(maxLayers, layers);

#if UNITY_EDITOR
            // Push splat index array required for GPU preview
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
            if (Current.HasProperty(PreviewShaderContracts.Properties.LayerSplatIndicesArr)) 
                Current.SetFloatArray(PreviewShaderContracts.Properties.LayerSplatIndicesArr, splatIndicesArr);
#endif

            // Collect textures for trying to build Texture2DArray
            var texList = new List<Texture2D>();
            int sliceCount = 0;
            int width = -1, height = -1;
            var textureHashBuilder = new System.Text.StringBuilder();

            for (var i = 0; i < Mathf.Min(maxLayers, layerCount); i++)
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

                // Try to collect array textures
                var tex = tl && tl.diffuseTexture ? tl.diffuseTexture : null;
                if (tex)
                {
                    texList.Add(tex);
                    sliceCount++;
                    if (width < 0)
                    {
                        width = tex.width;
                        height = tex.height;
                    }
                    // Add texture hash to cache key
                    textureHashBuilder.Append(tex.GetInstanceID()).Append(",");
                }
                else
                {
                    // If any layer is missing a texture, abandon the array approach (use the old per-layer push)
                    sliceCount = -1;
                    textureHashBuilder.Append("null,");
                }
            }

            // Prefer array path: as long as all layers have textures (sizes can be inconsistent, GPU Blit automatically scales to the first texture size)
            var canUseArray = sliceCount > 0;

            // Check if cache is available
            var textureHashes = textureHashBuilder.ToString();
            var newCacheKey = new TextureArrayCacheKey(sliceCount, width, height, textureHashes);
            var canReuseCache = m_CurrentCacheKey.HasValue && m_CurrentCacheKey.Value.Equals(newCacheKey) && m_LayerRtArray != null && m_LayerRtArray.IsCreated();

            if (canUseArray)
            {
                var textureArrayManager = new PreviewTextureArrayManager();
                var success = textureArrayManager.TryCreateAndBindTextureArray(Current, texList, width, height, textureHashes);
                
                if (!success)
                {
                    HandleFallbackTextureBinding(maxLayers, layerCount, layers, profile, parameterSetter);
                }
            }
            else
            {
                HandleFallbackTextureBinding(maxLayers, layerCount, layers, profile, parameterSetter);
            }

            return processedLayerCount;
        }

        private void HandleFallbackTextureBinding(int maxLayers, int layerCount, IReadOnlyList<RoadLayer> layers, PathProfile profile, PreviewMaterialParameterSetter parameterSetter)
        {
            Debug.LogWarning("[PreviewMaterialManager] Texture2DArray not available or missing textures, fallback to per-layer binding.");
            if (Current.HasProperty(PreviewShaderContracts.Properties.UseLayerTexArray)) 
                Current.SetFloat(PreviewShaderContracts.Properties.UseLayerTexArray, 0f);
            if (Current.HasProperty(PreviewShaderContracts.Properties.LayerTextures)) 
                Current.SetTexture(PreviewShaderContracts.Properties.LayerTextures, null);
                
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
                parameterSetter.SetLayerParameters(i, tl);
            }
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
        ///     Sets the Terrain associated with the current preview (used to get cached alphamap RenderTextureArray from <see cref="Gpu Preview Cache" />)
        /// </summary>
        /// <param name="terrain">Target Terrain</param>
        public void SetTargetTerrain(UnityEngine.Terrain terrain)
        {
            m_TargetTerrain = terrain;
        }
#endif

        // New: for external push of path length and Mesh repeat parameters
        public void SetPathLength(float length)
        {
            m_PathLength = length;
        }

        public void SetMeshRepeats(float across, float along)
        {
            if (!Current) return;
            if (Current.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAcross)) 
                Current.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAcross, Mathf.Max(1e-4f, across));
            if (Current.HasProperty(PreviewShaderContracts.Properties.MeshRepeatAlong)) 
                Current.SetFloat(PreviewShaderContracts.Properties.MeshRepeatAlong, Mathf.Max(1e-4f, along));
        }

        // Restore Update method (removed by previous edit), keep material refresh and GPU binding logic
        public void Update(PathProfile profile, Material template, float previewAlpha)
        {
            if (m_Disposed) return;
            
            if (profile == null || template == null)
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
            // Bind (or unbind) GPU real-time preview texture
            TryBindGpuPreview(profile);
#endif

            m_Dirty = true;
        }

        private void ApplySplat(PathProfile profile, float alpha)
        {
            if (Current == null) return;

            // Push layer parameters (including array/fallback)
            var layerCount = PushLayerParams(profile);
            // Push common preview parameters (opacity, threshold, depth, etc.)
            PushCommonPreviewParams(profile, layerCount, alpha);
            // Prepare mask texture (MaskAtlas or GPU weights)
#if UNITY_EDITOR
            // If GPU weights are available, skip MaskAtlas construction (early return)
            var gpuPreviewAvailable = EnableGpuPreview && m_TargetTerrain && EditorGpuPreviewCache.TryGet(m_TargetTerrain, out var rt) && rt;
            if (!gpuPreviewAvailable)
            {
                SetupMaskTextures(profile);
                // CPU 路径：推送 Terrain 参数，供 shader 进行世界坐标采样
                PushCpuTerrainParameters();
            }
            else
            {
                // WYSIWYG: 即使使用 GPU 权重，也提供“白色”占位Atlas，避免任何地方将两者相乘导致变黑
                if (Current.HasProperty(PreviewShaderContracts.Properties.MaskAtlas))
                    Current.SetTexture(PreviewShaderContracts.Properties.MaskAtlas, Texture2D.whiteTexture);
                if (Current.HasProperty(PreviewShaderContracts.Properties.AtlasInvHeight))
                    Current.SetFloat(PreviewShaderContracts.Properties.AtlasInvHeight, 1f);
            }
#else
            SetupMaskTextures(profile);
#endif
        }

        /// <summary>
        ///     Generates or updates the mask atlas texture that stores per-layer mask weights.
        ///     This replaces the legacy 1D RGBA LUT system and supports an arbitrary number of layers.
        /// </summary>
        private void SetupMaskTextures(PathProfile profile)
        {
            if (Current == null) return;
            
            var maskAtlasGenerator = new PreviewMaskAtlasGenerator(Current, profile, m_PathLength);
            m_MaskAtlas = maskAtlasGenerator.GenerateMaskAtlas(m_MaskAtlas);
        }

        private void ApplyStylized(PathProfile profile)
        {
            if (Current == null) return;

            var layersList = profile.roadRecipe?.GetLayers();
            var layer = layersList != null && layersList.Count > 0 ? layersList[0]?.contentLayer : null;
            
            var parameterSetter = new PreviewMaterialParameterSetter(Current, profile);
            parameterSetter.SetStylizedParameters(layer);

            // Key: Provide transparent previous frame result for stylized preview to avoid default blackTexture Alpha=1 causing entire rectangle
            if (Current.HasProperty(PreviewShaderContracts.Properties.PrevResultTex))
            {
                Current.SetTexture(PreviewShaderContracts.Properties.PrevResultTex, EnsureTransparentPrevTex());
            }

            // Ensure single-layer preview can also get mask texture (layer 0) to apply transparency gradient
            SetupMaskTextures(profile);

#if UNITY_EDITOR
            // Stylized 的 CPU 预览同样需要 Terrain 参数用于世界坐标与地形 UV 的换算
            PushCpuTerrainParameters();
#endif
        }

        private enum ShaderFlavor
        {
            Splat,
            Stylized,
            Unknown
        }
        
        // New: GPU preview related properties
#if UNITY_EDITOR
        private UnityEngine.Terrain m_TargetTerrain;
        
        /// <summary>
        ///     Global switch: whether to enable GPU real-time preview.
        ///     Can be replaced with ProjectSettings / ScriptableObject configuration later.
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
                // Basic references and display options
                hash = hash * 31 + (template?.GetHashCode() ?? 0);
                hash = hash * 31 + alpha.GetHashCode();
                if (profile != null)
                {
                    hash = hash * 31 + profile.enableDepthTest.GetHashCode();
                    hash = hash * 31 + profile.opaquePreview.GetHashCode();
                    hash = hash * 31 + profile.roadWidth.GetHashCode();
                }

                // Lightweight hash: collect RoadRecipe / Layers / TerrainLayer / Mask key fields
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

                    // RoadLayer basic fields
                    hash = hash * 31 + rl.enabled.GetHashCode();
                    hash = hash * 31 + rl.opacity.GetHashCode();
                    hash = hash * 31 + rl.blendMode.GetHashCode();

                    // TerrainLayer key fields (affecting texture and tiling, tint)
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
                        catch
                        { /* Avoid exceptions causing refresh failure */
                        }
                    }
                    else
                    {
                        hash = hash * 31 + 0;
                    }

                    // Mask key fields (different parameters for different types)
                    var mask = rl.layerMask;
                    if (mask)
                    {
                        try
                        {
                            // General parameters
                            hash = hash * 31 + mask.GetType().FullName.GetHashCode();
                            hash = hash * 31 + mask.smooth.GetHashCode();
                            hash = hash * 31 + mask.tiling.x.GetHashCode();
                            hash = hash * 31 + mask.tiling.y.GetHashCode();
                            hash = hash * 31 + mask.offset.x.GetHashCode();
                            hash = hash * 31 + mask.offset.y.GetHashCode();
                            hash = hash * 31 + mask.overallScale.GetHashCode();

                            // Procedural base class (Strength/Seed)
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.ProceduralMaskBase proc)
                            {
                                hash = hash * 31 + proc.strength.GetHashCode();
                                hash = hash * 31 + proc.seed.GetHashCode();
                            }

                            // Noise classes
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
                            // Shoulder mask
                            if (mask is __temp.MrPathV2.Runtime.Core.BlendMasks.ShoulderMask shoulder)
                            {
                                hash = hash * 31 + shoulder.shoulderWidthRatio.GetHashCode();
                                hash = hash * 31 + shoulder.shoulderStrength.GetHashCode();
                                hash = hash * 31 + shoulder.edgeFalloff.GetHashCode();
                                hash = hash * 31 + shoulder.enableLeftShoulder.GetHashCode();
                                hash = hash * 31 + shoulder.enableRightShoulder.GetHashCode();
                            }
                        }
                        catch
                        { /* Ignore exceptions to ensure hash process robustness */
                        }
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

        // New: release layer texture array to avoid resource leaks
        private void ReleaseLayerTexArray()
        {
            if (m_LayerRtArray != null)
            {
                m_LayerRtArray.Release();
                Object.DestroyImmediate(m_LayerRtArray);
                m_LayerRtArray = null;
            }
            // Clear cache key
            m_CurrentCacheKey = null;
        }

        /// <summary>
        /// Clean up all resources, including CommandBuffer
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
            
            var gpuBinder = new PreviewGpuBinder();
            gpuBinder.SetTargetTerrain(m_TargetTerrain);
            gpuBinder.TryBindGpuPreview(Current, EnableGpuPreview, profile.roadRecipe);
        }

        /// <summary>
        /// 在 CPU 预览路径推送 Terrain 参数，保持与 GPU 预览一致的世界坐标采样。
        /// </summary>
        private void PushCpuTerrainParameters()
        {
            if (!Current) return;
            if (!m_TargetTerrain) return;
            var td = m_TargetTerrain.terrainData;
            if (!td) return;

            var pos = m_TargetTerrain.GetPosition();
            var size = td.size;
            if (Current.HasProperty(PreviewShaderContracts.Properties.TerrainPosition))
                Current.SetVector(PreviewShaderContracts.Properties.TerrainPosition, new Vector4(pos.x, pos.z, 0f, 0f));
            if (Current.HasProperty(PreviewShaderContracts.Properties.TerrainSize))
                Current.SetVector(PreviewShaderContracts.Properties.TerrainSize, new Vector4(size.x, size.z, 0f, 0f));
            if (Current.HasProperty(PreviewShaderContracts.Properties.AlphamapResolution))
            {
                var res = td.alphamapResolution;
                Current.SetVector(PreviewShaderContracts.Properties.AlphamapResolution, new Vector4(res, res, 0f, 0f));
            }
        }
#endif

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

        #endregion
    }
}