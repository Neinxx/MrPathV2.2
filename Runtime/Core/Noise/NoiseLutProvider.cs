using System;
using UnityEngine;
using MrPathV2.Runtime.Core.Resources;
using Object = UnityEngine.Object;

namespace MrPathV2.Runtime.Core.Noise
{
    /// <summary>
    ///     提供统一的噪声LUT纹理给 CPU 和 GPU：
    ///     - 首次调用时通过 ComputeShader 生成周期性（可重复）的基础噪声纹理；
    ///     - 纹理将被缓存，并在 CPU 遮罩中使用 GetPixelBilinear 进行采样；
    ///     - GPU 侧在构建遮罩图集时将该纹理绑定到 Compute 着色器。
    /// </summary>
    public static class NoiseLutProvider
    {
        // 默认设置
        private const int DefaultSize = 512;
        private const int DefaultPeriod = 64; // LUT 内部的格点周期（控制大图案尺寸）

        private static Texture2D s_Lut;
        private static int s_Size;
        private static int s_Period;

        /// <summary>
        ///     返回（并在必要时生成）用于 CPU/GPU 采样的噪声LUT纹理。
        /// </summary>
        public static Texture2D GetOrCreateLut(int size = DefaultSize, int period = DefaultPeriod)
        {
            size = Mathf.Clamp(size, 64, 2048);
            period = Mathf.Clamp(period, 8, 512);
            if (s_Lut != null && s_Size == size && s_Period == period)
                return s_Lut;

            // 1) 尝试 Compute Shader 生成（仅在硬件支持且内核存在时）
            if (SystemInfo.supportsComputeShaders)
            {
                var cs = ResourceProvider.LoadComputeShader("NoiseLUT");
                // 使用 HasKernel 防止无效内核索引错误
                if (cs != null && cs.HasKernel("BuildNoiseLUT"))
                {
                    RenderTexture rt = null;
                    try
                    {
                        rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
                        {
                            name = "MrPath_NoiseLUT_RT",
                            enableRandomWrite = true,
                            wrapMode = TextureWrapMode.Repeat,
                            filterMode = FilterMode.Bilinear
                        };
                        rt.Create();

                        var kernel = cs.FindKernel("BuildNoiseLUT");
                        cs.SetInt("_Size", size);
                        cs.SetInt("_Period", period);
                        cs.SetTexture(kernel, "_Result", rt);

                        var gx = Mathf.CeilToInt(size / 8.0f);
                        var gy = Mathf.CeilToInt(size / 8.0f);
                        cs.Dispatch(kernel, gx, gy, 1);

                        // 拷贝到可读的 Texture2D（R8 足够）
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(size, size, TextureFormat.R8, false, true)
                        {
                            name = "MrPath_NoiseLUT",
                            wrapMode = TextureWrapMode.Repeat,
                            filterMode = FilterMode.Bilinear
                        };
                        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        // 保持可读以供 CPU 侧 GetPixelBilinear 采样
                        tex.Apply(false, false);
                        RenderTexture.active = prev;

                        s_Lut = tex;
                        s_Size = size;
                        s_Period = period;
                        return s_Lut;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[NoiseLutProvider] Compute 生成失败，回退到CPU生成。{e.Message}");
                    }
                    finally
                    {
                        if (rt != null)
                        {
                            rt.Release();
#if UNITY_EDITOR
                            Object.DestroyImmediate(rt);
#else
                            UnityEngine.Object.Destroy(rt);
#endif
                        }
                    }
                }
            }

            // 2) CPU 回退：生成周期性 value noise LUT
            return GenerateCpuLut(size, period);
        }

        /// <summary>
        ///     绑定 LUT 到 ComputeShader 的纹理参数（如 _NoiseLUT）。
        /// </summary>
        public static void BindToCompute(ComputeShader cs, int kernel, string textureName = "_NoiseLUT")
        {
            var tex = GetOrCreateLut();
            if (tex != null)
            {
                cs.SetTexture(kernel, textureName, tex);
            }
        }

        /// <summary>
        ///     CPU 采样（0..1），内部自动做 Repeat（使用 frac）。建议在外部调用前先将 uv 做旋转/缩放等。
        /// </summary>
        public static float Sample01(float u, float v)
        {
            var tex = GetOrCreateLut();
            if (tex == null) return 0.5f;
            // Wrap to 0..1
            u = u - Mathf.Floor(u);
            v = v - Mathf.Floor(v);
            var c = tex.GetPixelBilinear(u, v);
            return c.r; // R8 保存
        }

        private static Texture2D GenerateCpuLut(int size, int period)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, false, true)
            {
                name = "MrPath_NoiseLUT_CPU",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var colors = new Color[size * size];

            // Tileable value noise on integer lattice with given period
            float Fade(float t)
            {
                return t * t * (3f - 2f * t);
            }

            float Hash(int ix, int iy)
            {
                unchecked
                {
                    var x = (uint)ix;
                    var y = (uint)iy;
                    var h = x * 374761393u + y * 668265263u;
                    h ^= h >> 17;
                    h *= 0xed5ad4bbu;
                    h ^= h >> 11;
                    h *= 0xac4c1b51u;
                    h ^= h >> 15;
                    h *= 0x31848babu;
                    h ^= h >> 14;
                    return (h & 0xFFFFFF) / (float)0x1000000; // 0..1
                }
            }

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var uvx = (x + 0.5f) / Mathf.Max(1, size);
                    var uvy = (y + 0.5f) / Mathf.Max(1, size);
                    var px = uvx * period;
                    var py = uvy * period;
                    var ix = Mathf.FloorToInt(px);
                    var iy = Mathf.FloorToInt(py);
                    var fx = px - ix;
                    var fy = py - iy;

                    var ix1 = (ix + 1) % period;
                    if (ix1 < 0) ix1 += period;
                    var iy1 = (iy + 1) % period;
                    if (iy1 < 0) iy1 += period;
                    ix = ix % period;
                    if (ix < 0) ix += period;
                    iy = iy % period;
                    if (iy < 0) iy += period;

                    var v00 = Hash(ix, iy);
                    var v10 = Hash(ix1, iy);
                    var v01 = Hash(ix, iy1);
                    var v11 = Hash(ix1, iy1);
                    var ux = Fade(fx);
                    var uy = Fade(fy);
                    var a = Mathf.Lerp(v00, v10, ux);
                    var b = Mathf.Lerp(v01, v11, ux);
                    var n = Mathf.Lerp(a, b, uy);
                    colors[x + y * size] = new Color(n, n, n, 1f);
                }
            }

            tex.SetPixels(colors);
            // 保持可读以供 CPU 侧 GetPixelBilinear 采样
            tex.Apply(false, false);
            s_Lut = tex;
            s_Size = size;
            s_Period = period;
            return s_Lut;
        }
    }
}
