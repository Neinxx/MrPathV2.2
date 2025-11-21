using MrPathV2.Runtime.Core.Noise;
using Unity.Mathematics;
using UnityEngine;

namespace MrPathV2.Runtime.Core.NoiseRuntime
{
    public interface INoiseSampler
    {
        float Sample01(float u, float v);
    }

    public static class NoiseEvalUtils
    {
        private static INoiseSampler s_Sampler;
        private static float[] s_LutData;
        private static int s_LutSize;

        public static void SetSampler(INoiseSampler sampler)
        {
            s_Sampler = sampler;
        }

        private static void EnsureCpuCache()
        {
            if (s_Sampler != null) return;
            if (s_LutData != null && s_LutSize > 0) return;
            var tex = NoiseLutProvider.GetOrCreateLut();
            if (tex == null) return;
            var colors = tex.GetPixels();
            s_LutSize = tex.width;
            s_LutData = new float[colors.Length];
            for (var i = 0; i < colors.Length; i++) s_LutData[i] = colors[i].r;
        }

        private static float Sample01(float u, float v)
        {
            if (s_Sampler != null) return s_Sampler.Sample01(u, v);
            EnsureCpuCache();
            if (s_LutData == null || s_LutSize <= 0) return NoiseLutProvider.Sample01(u, v);
            var stx = u - Mathf.Floor(u);
            var sty = v - Mathf.Floor(v);
            var sx = stx * s_LutSize - 0.5f;
            var sy = sty * s_LutSize - 0.5f;
            var i0x = (int)Mathf.Floor(sx);
            var i0y = (int)Mathf.Floor(sy);
            var fx = sx - i0x;
            var fy = sy - i0y;
            int Wrap(int a)
            {
                var w = a % s_LutSize;
                return w < 0 ? w + s_LutSize : w;
            }
            var w0x = Wrap(i0x);
            var w0y = Wrap(i0y);
            var w1x = Wrap(i0x + 1);
            var w1y = Wrap(i0y + 1);
            var c00 = s_LutData[w0x + w0y * s_LutSize];
            var c10 = s_LutData[w1x + w0y * s_LutSize];
            var c01 = s_LutData[w0x + w1y * s_LutSize];
            var c11 = s_LutData[w1x + w1y * s_LutSize];
            var cx0 = Mathf.Lerp(c00, c10, fx);
            var cx1 = Mathf.Lerp(c01, c11, fx);
            return Mathf.Lerp(cx0, cx1, fy);
        }

        public static float EvaluateFbm01(in NoiseParamsDto p, float u, float v)
        {
            var x = u * math.max(1e-5f, p.ScaleX);
            var y = v * math.max(1e-5f, p.ScaleY);
            var rx = x * p.Cos - y * p.Sin + p.SeedX;
            var ry = x * p.Sin + y * p.Cos + p.SeedY;
            var amp = 1f;
            var freq = 1f;
            var sum = 0f;
            var norm = 0f;
            var oct = math.max(1, p.Octaves);
            var lac = math.max(1f, p.Lacunarity);
            var g = math.clamp(p.Gain, 0f, 1f);
            for (var i = 0; i < oct; i++)
            {
                var s = Sample01(rx * freq, ry * freq);
                sum += s * amp;
                norm += amp;
                freq *= lac;
                amp *= g;
            }
            return norm > 1e-5f ? sum / norm : 0f;
        }

        public static float EvaluateWorley01(in WorleyParamsDto p, float u, float v)
        {
            var x = u * math.max(1e-5f, p.ScaleX);
            var y = v * math.max(1e-5f, p.ScaleY);
            var rx = x * p.Cos - y * p.Sin + p.SeedX;
            var ry = x * p.Sin + y * p.Cos + p.SeedY;
            var px = rx * p.CellPeriod;
            var py = ry * p.CellPeriod;
            var ix = (int)math.floor(px);
            var iy = (int)math.floor(py);
            var fx = px - ix;
            var fy = py - iy;
            float dmin = 1e9f;
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
            var n = math.saturate(dmin);
            return p.Invert ? (1f - n) : n;
        }

        public static float EvaluateStripe01(float u, float v, float scaleX, float scaleY, float cos, float sin, float period, float jitter)
        {
            var x = u * math.max(1e-5f, scaleX);
            var y = v * math.max(1e-5f, scaleY);
            var rx = x * cos - y * sin;
            var ry = x * sin + y * cos;
            var phase = rx * period + Sample01(rx, ry) * jitter;
            var s = 0.5f * (math.sin(phase) + 1f);
            return s;
        }

        static float HashFloat2(int x, int y)
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
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
