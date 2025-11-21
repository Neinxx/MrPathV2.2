using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Core.Noise;
using UnityEngine;
using MrPathV2.Runtime.Core.Resources;
using Object = UnityEngine.Object;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using MrPathV2.Runtime.Jobs;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     Generates a 2-D mask atlas containing the 1-D mask lookup for every blend layer *and*
    ///     for multiple longitudinal samples along the path.
    ///     Layout:
    ///     ‑ The atlas width represents horizontal samples across the path (-1 … +1 in canonical UV).
    ///     ‑ The atlas height is <c>layerCount * pathSamples</c>. Each layer occupies a vertical slice of
    ///     <c>pathSamples</c> rows. Within each slice, <c>py</c> (0 … pathSamples-1) maps to
    ///     <c>pathProgress = py / (pathSamples-1)</c>.
    ///     Shaders and CPU jobs should sample the atlas like so:
    ///     u = acrossRoad;                    // 0 … 1
    ///     v = (layerIndex * pathSamples + pathProgress * (pathSamples-1) + 0.5) * _AtlasInvHeight;
    ///     The atlas is stored as an <see cref="TextureFormat.R8" /> with bilinear filtering and clamp wrap mode.
    /// </summary>
    public static class MaskAtlasGenerator
    {

        private const int DefaultPathSamples = 64;

        /// <summary>
        ///     Builds or updates a mask atlas texture (GPU preferred, CPU fallback).
        /// </summary>
        public static Texture2D BuildMaskAtlas(
            Texture2D reuse,
            IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth,
            float pathLength = 100f,
            int baseResolution = 256,
            float maskThreshold = 0f)
        {
            using (ProfilingMarkers.MaskAtlasGeneratorBuild.Auto())
            {
                if (layers == null || layers.Count == 0)
                {
                    if (reuse == null || reuse.width != 1 || reuse.height != 1 || reuse.format != TextureFormat.R8)
                    {
#if UNITY_EDITOR
                        if (reuse != null) Object.DestroyImmediate(reuse);
#else
                        if (reuse != null) Object.Destroy(reuse);
#endif
                        reuse = new Texture2D(1, 1, TextureFormat.R8, false, true)
                        {
                            wrapMode = TextureWrapMode.Clamp
                        };
                    }
                    reuse.SetPixel(0, 0, Color.white);
                    reuse.Apply(false, false);
                    return reuse;
                }

                var layerCount = layers.Count;
                var width = Mathf.Clamp(baseResolution, 16, 2048);
                var pathSamples = SuggestPathSamples(pathLength);
                var height = layerCount * pathSamples;

                var needCreate = reuse == null || reuse.width != width || reuse.height != height || reuse.format != TextureFormat.R8;
                if (needCreate)
                {
#if UNITY_EDITOR
                    if (reuse != null) Object.DestroyImmediate(reuse);
#else
                    if (reuse != null) Object.Destroy(reuse);
#endif
                    reuse = new Texture2D(width, height, TextureFormat.R8, false, true)
                    {
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        name = "MrPath_MaskAtlas"
                    };
                }

                // 若任何遮罩声明不支持 GPU，则强制走 CPU 路径，确保稳定性
                var allGpuSupported = true;
                for (var i = 0; i < layers.Count; i++)
                {
                    var m = layers[i].Mask;
                    if (m != null && !m.SupportsGpu)
                    {
                        allGpuSupported = false;
                        break;
                    }
                }

                // Try GPU path (compute shader in Resources: MaskAtlas.compute)
                var supportsCompute = SystemInfo.supportsComputeShaders;
                var cs = supportsCompute && allGpuSupported ? ResourceProvider.LoadComputeShader("MaskAtlas") : null;
                if (cs != null && cs.HasKernel("BuildAtlas"))
                {
                    try
                    {
                        return BuildAtlasGpu(cs, reuse, layers, worldWidth, pathLength, width, pathSamples, maskThreshold);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MaskAtlasGenerator] GPU build failed, falling back to CPU. {e.Message}");
                    }
                }

                // CPU fallback
                BuildAtlasCpu(reuse, layers, worldWidth, pathLength, width, pathSamples, maskThreshold);
                return reuse;
            }
        }

        /// <summary>
        ///     建议的纵向采样数，根据路径长度自适应，默认每米约1~2行。
        /// </summary>
        public static int SuggestPathSamples(float pathLength, float metersPerSample = 0.75f, int min = 64, int max = 2048)
        {
            var mps = Mathf.Max(0.01f, metersPerSample);
            var estimate = Mathf.CeilToInt(Mathf.Max(1f, pathLength) / mps);
            return Mathf.Clamp(estimate, min, max);
        }

        /// <summary>
        ///     重载：允许外部指定纵向采样数以避免沿路径分块。
        /// </summary>
        public static Texture2D BuildMaskAtlas(
            Texture2D reuse,
            IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth,
            float pathLength,
            int baseResolution,
            float maskThreshold,
            int pathSamplesOverride)
        {
            // 复用原实现，但将内部的 pathSamples 替换为自适应/外部值
            using (ProfilingMarkers.MaskAtlasGeneratorBuild.Auto())
            {
                if (layers == null || layers.Count == 0)
                {
                    if (reuse == null || reuse.width != 1 || reuse.height != 1 || reuse.format != TextureFormat.R8)
                    {
#if UNITY_EDITOR
                        if (reuse != null) Object.DestroyImmediate(reuse);
#else
                        if (reuse != null) Object.Destroy(reuse);
#endif
                        reuse = new Texture2D(1, 1, TextureFormat.R8, false, true)
                        {
                            wrapMode = TextureWrapMode.Clamp
                        };
                    }
                    reuse.SetPixel(0, 0, Color.white);
                    reuse.Apply(false, false);
                    return reuse;
                }

                var layerCount = layers.Count;
                var width = Mathf.Clamp(baseResolution, 16, 2048);
                var pathSamples = pathSamplesOverride > 0 ? Mathf.Clamp(pathSamplesOverride, 2, 4096) : DefaultPathSamples;
                var height = layerCount * pathSamples;

                var needCreate = reuse == null || reuse.width != width || reuse.height != height || reuse.format != TextureFormat.R8;
                if (needCreate)
                {
#if UNITY_EDITOR
                    if (reuse != null) Object.DestroyImmediate(reuse);
#else
                    if (reuse != null) Object.Destroy(reuse);
#endif
                    reuse = new Texture2D(width, height, TextureFormat.R8, false, true)
                    {
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        name = "MrPath_MaskAtlas"
                    };
                }

                var allGpuSupported = true;
                for (var i = 0; i < layers.Count; i++)
                {
                    var m = layers[i].Mask;
                    if (m != null && !m.SupportsGpu)
                    {
                        allGpuSupported = false;
                        break;
                    }
                }

                var supportsCompute = SystemInfo.supportsComputeShaders;
                var cs = supportsCompute && allGpuSupported ? ResourceProvider.LoadComputeShader("MaskAtlas") : null;
                if (cs != null && cs.HasKernel("BuildAtlas"))
                {
                    try
                    {
                        return BuildAtlasGpu(cs, reuse, layers, worldWidth, pathLength, width, pathSamples, maskThreshold);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MaskAtlasGenerator] GPU build failed, falling back to CPU. {e.Message}");
                    }
                }

                BuildAtlasCpu(reuse, layers, worldWidth, pathLength, width, pathSamples, maskThreshold);
                return reuse;
            }
        }

        private static Texture2D BuildAtlasGpu(
            ComputeShader cs,
            Texture2D target,
            IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth,
            float pathLength,
            int atlasWidth,
            int pathSamples,
            float maskThreshold)
        {
            var layerCount = layers.Count;
            var atlasHeight = layerCount * pathSamples;

            // Pack params
            var maskParams = new GpuMaskParams[layerCount];
            var opacities = new float[layerCount];
            for (var i = 0; i < layerCount; i++)
            {
                maskParams[i] = PackMaskParams(layers[i].Mask);
                opacities[i] = Mathf.Clamp01(layers[i].Opacity);
            }

            var maskBuf = new ComputeBuffer(layerCount, Marshal.SizeOf(typeof(GpuMaskParams)), ComputeBufferType.Structured);
            var opaBuf = new ComputeBuffer(layerCount, sizeof(float), ComputeBufferType.Structured);
            maskBuf.SetData(maskParams);
            opaBuf.SetData(opacities);

            var rt = new RenderTexture(atlasWidth, atlasHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "MaskAtlasRT",
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();

            var kernel = cs.FindKernel("BuildAtlas");
            cs.SetInt("_AtlasWidth", atlasWidth);
            cs.SetInt("_LayerCount", layerCount);
            cs.SetInt("_PathSamples", pathSamples);
            cs.SetFloat("_WorldWidth", Mathf.Max(0.001f, worldWidth));
            cs.SetFloat("_PathLength", Mathf.Max(0.001f, pathLength));
            cs.SetBuffer(kernel, "_MaskParams", maskBuf);
            cs.SetBuffer(kernel, "_Opacities", opaBuf);
            cs.SetTexture(kernel, "_Atlas", rt);
            cs.SetFloat("_MaskThreshold", Mathf.Clamp01(maskThreshold));

            // Bind shared Noise LUT for unified CPU/GPU noise sampling
            var lut = NoiseLutProvider.GetOrCreateLut();
            if (lut != null)
            {
                cs.SetInt("_NoiseLutSize", lut.width);
                cs.SetTexture(kernel, "_NoiseLUT", lut);
            }
            else
            {
                // 若生成失败，置尺寸为0，compute侧将使用0.5常量，保持健壮
                cs.SetInt("_NoiseLutSize", 0);
            }

            var gx = Mathf.CeilToInt(atlasWidth / 8.0f);
            var gy = Mathf.CeilToInt(atlasHeight / 8.0f);
            cs.Dispatch(kernel, gx, gy, 1);

            // Copy into Texture2D for shader (use ReadPixels to avoid base format mismatch warnings)
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            target.ReadPixels(new Rect(0, 0, atlasWidth, atlasHeight), 0, 0);
            target.Apply(false);
            RenderTexture.active = prev;

            maskBuf.Release();
            opaBuf.Release();
            rt.Release();
#if UNITY_EDITOR
            Object.DestroyImmediate(rt);
#else
            Object.Destroy(rt);
#endif
            return target;
        }

        private static void BuildAtlasCpu(
            Texture2D target,
            IList<PreviewPipelineUtility.PreviewLayerInfo> layers,
            float worldWidth,
            float pathLength,
            int atlasWidth,
            int pathSamples,
            float maskThreshold)
        {
            var layerCount = layers.Count;
            var atlasHeight = layerCount * pathSamples;
            var workload = atlasWidth * atlasHeight;
            var canParallel = workload >= 131072;
            if (!canParallel)
            {
                var data = new byte[atlasWidth * atlasHeight];
                for (var i = 0; i < data.Length; i++) data[i] = 255;
                for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    var layer = layers[layerIndex];
                    var mask = layer.Mask;
                    for (var py = 0; py < pathSamples; py++)
                    {
                        var progress = (float)py / Mathf.Max(1, pathSamples - 1);
                        for (var px = 0; px < atlasWidth; px++)
                        {
                            var across01 = (float)px / Mathf.Max(1, atlasWidth - 1);
                            var across = across01 * 2.0f - 1.0f;
                            var value = mask != null ? mask.Evaluate(across, progress, worldWidth, pathLength) : 1.0f;
                            var shaped = maskThreshold <= 0f ? value : Mathf.Clamp01((value - maskThreshold) / Mathf.Max(1e-5f, 1f - maskThreshold));
                            var finalValue = Mathf.Clamp01(shaped);
                            var idx = py + layerIndex * pathSamples;
                            data[px + idx * atlasWidth] = (byte)(finalValue * 255.0f);
                        }
                    }
                }
                target.SetPixelData(data, 0);
                target.Apply(false);
                return;
            }
            var maskParams = new GpuMaskParams[layerCount];
            for (var i = 0; i < layerCount; i++) maskParams[i] = PackMaskParams(layers[i].Mask);
            var lut = NoiseLutProvider.GetOrCreateLut();
            var lutSize = lut ? lut.width : 0;
            NativeArray<float> lutArr = default;
            if (lutSize > 0)
            {
                var tmp = lut.GetPixels();
                lutArr = new NativeArray<float>(lutSize * lutSize, Allocator.TempJob);
                for (var i = 0; i < tmp.Length; i++) lutArr[i] = tmp[i].r;
            }
            var outR = new NativeArray<byte>(atlasWidth * atlasHeight, Allocator.TempJob);
            var maskNative = new NativeArray<GpuMaskParams>(maskParams, Allocator.TempJob);
            var job = new BuildMaskAtlasJob
            {
                outR = outR,
                maskParams = maskNative,
                noiseLut = lutArr,
                noiseLutSize = lutSize,
                atlasWidth = atlasWidth,
                pathSamples = pathSamples,
                worldWidth = worldWidth,
                pathLength = pathLength,
                maskThreshold = maskThreshold
            };
            var handle = job.Schedule(layerCount * pathSamples, 1);
            handle.Complete();
            var dataBytes = outR.ToArray();
            target.SetPixelData(dataBytes, 0);
            target.Apply(false);
            maskNative.Dispose();
            outR.Dispose();
            if (lutArr.IsCreated) lutArr.Dispose();
        }

        private static GpuMaskParams PackMaskParams(BlendMaskBase mask)
        {
            var gpuMask = new GpuMaskParams
            {
                MaskType = 0,
                Strength = 1.0f
            };
            if (mask == null || !mask.SupportsGpu) return gpuMask;

            // 统一从遮罩对象收集参数
            var dto = new GpuMaskParamsData();
            mask.FillGpuParams(ref dto);

            // 映射到运行时 GPU 结构（与 HLSL 对齐）
            gpuMask.MaskType = dto.MaskType;
            gpuMask.Strength = dto.Strength;

            gpuMask.ShoulderParams = new GpuShoulderMaskParams
            {
                ShoulderWidthRatio = dto.ShoulderParams.ShoulderWidthRatio,
                PositionRatio = dto.ShoulderParams.PositionRatio,
                ShoulderStrength = dto.ShoulderParams.ShoulderStrength,
                EdgeFalloff = dto.ShoulderParams.EdgeFalloff,
                EnableLeftShoulder = dto.ShoulderParams.EnableLeftShoulder,
                EnableRightShoulder = dto.ShoulderParams.EnableRightShoulder,
                Tiling = dto.ShoulderParams.Tiling,
                Offset = dto.ShoulderParams.Offset,
                OverallScale = dto.ShoulderParams.OverallScale,
                Smooth = dto.ShoulderParams.Smooth,
                Pad1 = 0f,
                Pad2 = 0f
            };

            gpuMask.NoiseParams = new GpuNoiseMaskParams
            {
                Strength = dto.NoiseParams.Strength,
                Seed = dto.NoiseParams.Seed,
                Tiling = dto.NoiseParams.Tiling,
                Offset = dto.NoiseParams.Offset,
                OverallScale = dto.NoiseParams.OverallScale,
                Smooth = dto.NoiseParams.Smooth,
                NoiseScale = dto.NoiseParams.NoiseScale,
                RotationRad = dto.NoiseParams.RotationRad,
                Octaves = dto.NoiseParams.Octaves,
                Lacunarity = dto.NoiseParams.Lacunarity,
                Gain = dto.NoiseParams.Gain,
                UseAsymmetricEdges = dto.NoiseParams.UseAsymmetricEdges ? 1 : 0,
                EdgeLow = dto.NoiseParams.EdgeLow,
                EdgeHigh = dto.NoiseParams.EdgeHigh,
                Period = dto.NoiseParams.Period,
                Jitter = dto.NoiseParams.Jitter,
                Invert = dto.NoiseParams.Invert,
                Variant = dto.NoiseParams.Variant
            };

            return gpuMask;
        }

        public static ComputeBuffer BuildLayerParamsBuffer(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap)
        {
            var layers = recipe ? recipe.GetLayers().Where(l => l != null && l.enabled).ToArray() : Array.Empty<RoadLayer>();
            var arr = new LayerParams[layers.Length];
            for (var i = 0; i < layers.Length; i++)
            {
                var rl = layers[i];
                var splatIndex = rl.contentLayer != null && terrainLayerMap != null && terrainLayerMap.TryGetValue(rl.contentLayer, out var idx) ? idx : int.MaxValue;
                arr[i] = new LayerParams
                {
                    blend_mode = MapBlendMode(rl.blendMode),
                    opacity = Mathf.Clamp01(rl.opacity * (recipe ? recipe.masterOpacity : 1f)),
                    texture_index = 0u,
                    terrain_layer_splat_index = (uint)splatIndex,
                    tiling_offset = new Vector4(1, 1, 0, 0),
                    tint_color = Color.white,
                    mask_params = PackMaskParams(rl.layerMask)
                };
            }
            var buf = new ComputeBuffer(arr.Length, Marshal.SizeOf(typeof(LayerParams)), ComputeBufferType.Structured);
            buf.SetData(arr);
            return buf;
        }

        private static uint MapBlendMode(Runtime.Core.BlendMode m)
        {
            if (m == Runtime.Core.BlendMode.AlphaClip) return 7u;
            if (m == Runtime.Core.BlendMode.Overlay) return 2u;
            if (m == Runtime.Core.BlendMode.Lerp) return 1u;
            return 0u;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LayerParams
        {
            public uint blend_mode;
            public float opacity;
            public uint texture_index;
            public uint terrain_layer_splat_index;
            public Vector4 tiling_offset;
            public Color tint_color;
            public GpuMaskParams mask_params;
        }

        public static ComputeBuffer BuildSliceRecipeMaskBuffer(RecipeData recipeData, int alphamapLayerCount)
        {
            var sliceCount = (alphamapLayerCount + 3) / 4;
            var arr = new uint[sliceCount];
            for (var s = 0; s < sliceCount; s++)
            {
                var bits = 0u;
                for (var c = 0; c < 4; c++)
                {
                    var idx = s * 4 + c;
                    var isRecipe = false;
                    for (var k = 0; k < recipeData.Length; k++)
                    {
                        if (recipeData.TerrainLayerIndices[k] == idx) { isRecipe = true; break; }
                    }
                    if (isRecipe) bits |= (1u << c);
                }
                arr[s] = bits;
            }
            var buf = new ComputeBuffer(arr.Length, sizeof(uint), ComputeBufferType.Structured);
            buf.SetData(arr);
            return buf;
        }

        public static Dictionary<TerrainLayer, int> BuildTerrainLayerMap(TerrainData td)
        {
            var dict = new Dictionary<TerrainLayer, int>();
            var tls = td.terrainLayers;
            for (var i = 0; i < tls.Length; i++)
            {
                var tl = tls[i];
                if (tl) dict[tl] = i;
            }
            return dict;
        }

        // GPU param structs (must match HLSL in BlendMaskLibrary.hlsl)
        [StructLayout(LayoutKind.Sequential)]
        private struct GpuShoulderMaskParams
        {
            public float ShoulderWidthRatio;
            public float PositionRatio; // 新增：路肩中心相对边缘的位移比例 (0..1)
            public float ShoulderStrength;
            public float EdgeFalloff;
            public int EnableLeftShoulder;
            public int EnableRightShoulder;
            public Vector2 Tiling;
            public Vector2 Offset;
            public float OverallScale;
            public float Smooth;
            public float Pad1;
            public float Pad2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuNoiseMaskParams
        {
            public float Strength;
            public float Seed;
            public Vector2 Tiling;
            public Vector2 Offset;
            public float OverallScale;
            public float Smooth;
            public Vector2 NoiseScale;
            public float RotationRad;
            public int Octaves;
            public float Lacunarity;
            public float Gain;
            public int UseAsymmetricEdges;
            public float EdgeLow;
            public float EdgeHigh;
            public float Period;
            public float Jitter;
            public int Invert;
            public int Variant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuMaskParams
        {
            public int MaskType;
            public float Strength;
            public float Pad2, Pad3;
            public GpuNoiseMaskParams NoiseParams;
            public GpuShoulderMaskParams ShoulderParams;
        }

        [BurstCompile]
        private struct BuildMaskAtlasJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<byte> outR;
            [ReadOnly] public NativeArray<GpuMaskParams> maskParams;
            [ReadOnly] public NativeArray<float> noiseLut;
            [ReadOnly] public int noiseLutSize;
            [ReadOnly] public int atlasWidth;
            [ReadOnly] public int pathSamples;
            [ReadOnly] public float worldWidth;
            [ReadOnly] public float pathLength;
            [ReadOnly] public float maskThreshold;

            public void Execute(int index)
            {
                var layerIndex = index / pathSamples;
                var py = index % pathSamples;
                var progress = (float)py / math.max(1, pathSamples - 1);
                var mp = maskParams[layerIndex];
                for (var px = 0; px < atlasWidth; px++)
                {
                    var across01 = (float)px / math.max(1, atlasWidth - 1);
                    var across = across01 * 2f - 1f;
                    var value = EvaluateMask(mp, progress, across, worldWidth);
                    var shaped = maskThreshold <= 0f ? value : math.saturate((value - maskThreshold) / math.max(1e-5f, 1f - maskThreshold));
                    outR[px + index * atlasWidth] = (byte)(math.saturate(shaped) * 255f);
                }
            }

            private float2 ComputeUV(float progress, float signedDistance, float roadWidth, float2 tiling, float2 offset)
            {
                var halfRoad = math.max(1e-4f, roadWidth * 0.5f);
                var xNorm = math.clamp(signedDistance / halfRoad, -1f, 1f);
                var u01 = 0.5f * (xNorm + 1f);
                var repeatX = tiling.x;
                var repeatY = tiling.y;
                var denomX = math.abs(repeatX) < 1e-4f ? 1e-4f * (repeatX == 0f ? 1f : math.sign(repeatX)) : repeatX;
                var denomY = math.abs(repeatY) < 1e-4f ? 1e-4f * (repeatY == 0f ? 1f : math.sign(repeatY)) : repeatY;
                var u = u01 * denomX + offset.x;
                var v = progress * denomY + offset.y;
                return new float2(u, v);
            }

            private float SampleLut(float2 uv)
            {
                if (noiseLutSize <= 0) return 0.5f;
                var st = math.frac(uv) * (float)noiseLutSize - 0.5f;
                var i0x = (int)math.floor(st.x);
                var i0y = (int)math.floor(st.y);
                var fx = st.x - i0x;
                var fy = st.y - i0y;
                var size = noiseLutSize;
                int Wrap(int a) { var w = a % size; return w < 0 ? w + size : w; }
                var w0x = Wrap(i0x);
                var w0y = Wrap(i0y);
                var w1x = Wrap(i0x + 1);
                var w1y = Wrap(i0y + 1);
                var c00 = noiseLut[w0x + w0y * size];
                var c10 = noiseLut[w1x + w0y * size];
                var c01 = noiseLut[w0x + w1y * size];
                var c11 = noiseLut[w1x + w1y * size];
                var cx0 = math.lerp(c00, c10, fx);
                var cx1 = math.lerp(c01, c11, fx);
                return math.lerp(cx0, cx1, fy);
            }

            private float EvaluateNoise(float progress, float signedDistance, float roadWidth, GpuNoiseMaskParams p)
            {
                var uv = ComputeUV(progress, signedDistance, roadWidth, p.Tiling, p.Offset);
                var m = new float2(uv.x * math.max(p.NoiseScale.x, 1e-6f), uv.y * math.max(p.NoiseScale.y, 1e-6f));
                var s = math.sin(p.RotationRad);
                var c = math.cos(p.RotationRad);
                var mx = c * m.x - s * m.y;
                var my = s * m.x + c * m.y;
                var sx = math.abs(math.sin(p.Seed * 12.9898f) * 43758.5453f);
                var sy = math.abs(math.sin(p.Seed * 78.233f) * 12345.678f);
                var seedX = sx - math.floor(sx);
                var seedY = sy - math.floor(sy);
                mx += seedX;
                my += seedY;
                var n01 = 0f;
                if (p.Variant == 0)
                {
                    var amplitude = 1f;
                    var frequency = 1f;
                    var sum = 0f;
                    var norm = 0f;
                    var oct = math.max(p.Octaves, 1);
                    for (var i = 0; i < oct; i++)
                    {
                        var s01 = SampleLut(new float2(mx * frequency, my * frequency));
                        sum += s01 * amplitude;
                        norm += amplitude;
                        frequency *= math.max(1f, p.Lacunarity);
                        amplitude *= math.saturate(p.Gain);
                    }
                    n01 = norm > 1e-5f ? sum / norm : 0f;
                }
                else if (p.Variant == 1)
                {
                    var phase = mx * p.Period + SampleLut(new float2(mx, my)) * p.Jitter;
                    n01 = 0.5f * (math.sin(phase) + 1f);
                }
                else if (p.Variant == 2)
                {
                    var px = mx * p.Period;
                    var py = my * p.Period;
                    var ix = (int)math.floor(px);
                    var iy = (int)math.floor(py);
                    var fx = px - ix;
                    var fy = py - iy;
                    float HashFloat2(int x, int y)
                    {
                        uint ux = (uint)x;
                        uint uy = (uint)y;
                        uint h = ux * 374761393u + uy * 668265263u;
                        h ^= h >> 17;
                        h *= 0xed5ad4bbu;
                        h ^= h >> 11;
                        h *= 0xac4c1b51u;
                        h ^= h >> 15;
                        h *= 0x31848babu;
                        h ^= h >> 14;
                        return (h & 0xFFFFFF) / (float)0x1000000; // 0..1
                    }
                    var dmin = 1e9f;
                    for (var dy = 0; dy <= 1; dy++)
                    {
                        for (var dx = 0; dx <= 1; dx++)
                        {
                            var cx = ix + dx;
                            var cy = iy + dy;
                            var h = HashFloat2(cx, cy);
                            var jx = (h * 1.618f) - math.floor(h * 1.618f);
                            var jy = (h * 2.414f) - math.floor(h * 2.414f);
                            jx = math.lerp(0.5f, jx, p.Jitter);
                            jy = math.lerp(0.5f, jy, p.Jitter);
                            var vx = dx + jx - fx;
                            var vy = dy + jy - fy;
                            var d = math.sqrt(vx * vx + vy * vy);
                            if (d < dmin) dmin = d;
                        }
                    }
                    n01 = math.saturate(dmin);
                    if (p.Invert != 0) n01 = 1f - n01;
                }
                var pre = math.saturate(n01 * p.Strength) * p.OverallScale;
                if (p.UseAsymmetricEdges != 0)
                {
                    var e0 = p.EdgeLow;
                    var e1 = p.EdgeHigh;
                    if (e0 > e1) { var t = e0; e0 = e1; e1 = t; }
                    var t2 = math.saturate((pre - e0) / math.max(1e-6f, e1 - e0));
                    return t2 * t2 * (3f - 2f * t2);
                }
                if (p.Smooth <= 1e-5f) return math.saturate(pre);
                var edge0 = p.Smooth * 0.5f;
                var edge1 = 1f - p.Smooth * 0.5f;
                var tt = math.saturate((pre - edge0) / math.max(1e-6f, edge1 - edge0));
                return tt * tt * (3f - 2f * tt);
            }

            private float ShoulderStripeWeight(float distanceFromPath, float center, float stripeHalf, float falloffW)
            {
                var d = math.abs(distanceFromPath - center);
                if (falloffW <= 1e-6f) return d <= stripeHalf ? 1f : 0f;
                var t = 1f - math.saturate((d - stripeHalf) / falloffW);
                return math.saturate(t);
            }

            private float EvaluateShoulder(float signedDistance, float roadWidth, GpuShoulderMaskParams p)
            {
                var halfRoad = math.max(1e-5f, roadWidth * 0.5f);
                var inset = math.saturate(p.PositionRatio) * halfRoad;
                var leftC = -halfRoad + inset;
                var rightC = halfRoad - inset;
                var stripeHalf = math.max(1e-5f, p.ShoulderWidthRatio * roadWidth * 0.5f);
                var falloffW = math.max(1e-5f, p.EdgeFalloff * roadWidth);
                var wLeft = p.EnableLeftShoulder != 0 ? ShoulderStripeWeight(signedDistance, leftC, stripeHalf, falloffW) : 0f;
                var wRight = p.EnableRightShoulder != 0 ? ShoulderStripeWeight(signedDistance, rightC, stripeHalf, falloffW) : 0f;
                var shoulderShape = math.max(wLeft, wRight) * p.ShoulderStrength;
                var pre = math.saturate(shoulderShape) * p.OverallScale;
                if (p.Smooth <= 1e-5f) return math.saturate(pre);
                var edge0 = p.Smooth * 0.5f;
                var edge1 = 1f - p.Smooth * 0.5f;
                var tt = math.saturate((pre - edge0) / math.max(1e-6f, edge1 - edge0));
                return tt * tt * (3f - 2f * tt);
            }

            private float EvaluateShoulderNoise(GpuShoulderMaskParams sp, GpuNoiseMaskParams np, float progress, float signedDistance, float roadWidth)
            {
                var shoulderVal = EvaluateShoulder(signedDistance, roadWidth, sp);
                var noiseVal = EvaluateNoise(progress, signedDistance, roadWidth, np);
                var pre = math.saturate(shoulderVal * noiseVal) * sp.OverallScale;
                if (sp.Smooth <= 1e-5f) return math.saturate(pre);
                var edge0 = sp.Smooth * 0.5f;
                var edge1 = 1f - sp.Smooth * 0.5f;
                var tt = math.saturate((pre - edge0) / math.max(1e-6f, edge1 - edge0));
                var sm = tt * tt * (3f - 2f * tt);
                return math.saturate(sm);
            }

            private float EvaluateMask(GpuMaskParams mask, float progress, float signedDistance, float roadWidth)
            {
                if (mask.MaskType == 0) return 1f;
                var m = 1f;
                if (mask.MaskType == 1) m = EvaluateShoulder(signedDistance, roadWidth, mask.ShoulderParams);
                else if (mask.MaskType == 2) m = EvaluateNoise(progress, signedDistance, roadWidth, mask.NoiseParams);
                else if (mask.MaskType == 4) m = EvaluateShoulderNoise(mask.ShoulderParams, mask.NoiseParams, progress, signedDistance, roadWidth);
                return math.saturate(m * mask.Strength);
            }
        }
    }
}
