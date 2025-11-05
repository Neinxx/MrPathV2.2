using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace __temp.MrPathV2.Runtime.Core
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
            int baseResolution = 256)
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
                var pathSamples = DefaultPathSamples;
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

                // Try GPU path (compute shader in Resources: MaskAtlas.compute)
                var cs = Resources.Load<ComputeShader>("MaskAtlas");
                if (cs != null)
                {
                    try
                    {
                        return BuildAtlasGpu(cs, reuse, layers, worldWidth, pathLength, width, pathSamples);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MaskAtlasGenerator] GPU build failed, falling back to CPU. {e.Message}");
                    }
                }

                // CPU fallback
                BuildAtlasCpu(reuse, layers, worldWidth, pathLength, width, pathSamples);
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
            int pathSamples)
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
            int pathSamples)
        {
            var layerCount = layers.Count;
            var atlasHeight = layerCount * pathSamples;

            // Init pixels
            var pixels = new Color32[atlasWidth * atlasHeight];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);

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
                        // 遮罩权重不再叠乘图层不透明度，保持与GPU路径一致（权重仅代表 mask）
                        var finalValue = Mathf.Clamp01(value);
                        var idx = py + layerIndex * pathSamples;
                        pixels[px + idx * atlasWidth] = new Color32((byte)(finalValue * 255.0f), 0, 0, 255);
                    }
                }
            }
            target.SetPixels32(pixels);
            target.Apply(false);
        }

        private static GpuMaskParams PackMaskParams(BlendMaskBase mask)
        {
            var gpuMask = new GpuMaskParams
            {
                MaskType = 0,
                Strength = 1.0f
            };
            if (mask == null) return gpuMask;

            // 统一从遮罩对象收集参数
            var dto = new GpuMaskParamsData();
            mask.FillGpuParams(ref dto);

            // 映射到运行时 GPU 结构（与 HLSL 对齐）
            gpuMask.MaskType = dto.MaskType;
            gpuMask.Strength = dto.Strength;

            gpuMask.ShoulderParams = new GpuShoulderMaskParams
            {
                ShoulderWidthRatio = dto.ShoulderParams.ShoulderWidthRatio,
                ShoulderStrength = dto.ShoulderParams.ShoulderStrength,
                EdgeFalloff = dto.ShoulderParams.EdgeFalloff,
                EnableLeftShoulder = dto.ShoulderParams.EnableLeftShoulder ? 1 : 0,
                EnableRightShoulder = dto.ShoulderParams.EnableRightShoulder ? 1 : 0,
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
                AlgorithmId = dto.NoiseParams.AlgorithmId,
                UseAsymmetricEdges = dto.NoiseParams.UseAsymmetricEdges ? 1 : 0,
                EdgeLow = dto.NoiseParams.EdgeLow,
                EdgeHigh = dto.NoiseParams.EdgeHigh,
                Pad1 = 0f
            };

            return gpuMask;
        }

        // GPU param structs (must match HLSL in BlendMaskLibrary.hlsl)
        [StructLayout(LayoutKind.Sequential)]
        private struct GpuShoulderMaskParams
        {
            public float ShoulderWidthRatio;
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
            public int AlgorithmId;
            public int UseAsymmetricEdges;
            public float EdgeLow;
            public float EdgeHigh;
            public float Pad1;
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
    }
}
