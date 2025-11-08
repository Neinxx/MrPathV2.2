// AdaptivePolylineCache.cs
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    ///     分段折线缓存，贴近 Unity Splines 的即时绘制模式：
    ///     - 根据屏幕像素步长与曲率估计生成折线点序列
    ///     - 按段缓存采样结果，控制点或分辨率变化时自动失效
    ///     - 提供 Bezier 与 Catmull-Rom 两种曲线的折线采样
    /// </summary>
    public class AdaptivePolylineCache
    {
        private struct Entry
        {
            public int Hash;             // 控制点哈希 + 分辨率标识
            public Vector3[] Polyline;   // 采样后的点序列
        }

        private readonly Dictionary<int, Entry> _bezierCache = new();
        private readonly Dictionary<int, Entry> _catmullCache = new();

        public void Clear()
        {
            _bezierCache.Clear();
            _catmullCache.Clear();
        }

        private static int HashBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int resKey)
        {
            // 将浮点量化并组合，避免相机轻微抖动导致的误判
            int q(Vector3 v) => HashCode.Combine(
                Mathf.RoundToInt(v.x * 1000f),
                Mathf.RoundToInt(v.y * 1000f),
                Mathf.RoundToInt(v.z * 1000f)
            );
            return HashCode.Combine(q(p0), q(p1), q(p2), q(p3), resKey);
        }

        private static int HashCatmull(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int resKey)
        {
            int q(Vector3 v) => HashCode.Combine(
                Mathf.RoundToInt(v.x * 1000f),
                Mathf.RoundToInt(v.y * 1000f),
                Mathf.RoundToInt(v.z * 1000f)
            );
            return HashCode.Combine(q(p0), q(p1), q(p2), q(p3), resKey);
        }

        /// <summary>
        ///     获取 Bezier 段的折线采样，使用缓存以降低每帧计算与GC。
        /// </summary>
        public Vector3[] GetBezier(int segmentIndex, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float pixelStep, Camera cam, bool lowQuality)
        {
            var resKey = Mathf.RoundToInt(pixelStep * 10f) + (lowQuality ? 1 : 0);
            var hash = HashBezier(p0, p1, p2, p3, resKey);

            if (_bezierCache.TryGetValue(segmentIndex, out var entry) && entry.Hash == hash && entry.Polyline != null)
                return entry.Polyline;

            var res = EstimateBezierResolution(p0, p1, p2, p3, pixelStep, cam, lowQuality);
            var poly = SampleBezierUniform(p0, p1, p2, p3, res);

            _bezierCache[segmentIndex] = new Entry { Hash = hash, Polyline = poly };
            return poly;
        }

        /// <summary>
        ///     获取 Catmull-Rom 段的折线采样，使用缓存以降低每帧计算与GC。
        /// </summary>
        public Vector3[] GetCatmull(int segmentIndex, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float pixelStep, Camera cam, bool lowQuality)
        {
            var resKey = Mathf.RoundToInt(pixelStep * 10f) + (lowQuality ? 1 : 0);
            var hash = HashCatmull(p0, p1, p2, p3, resKey);

            if (_catmullCache.TryGetValue(segmentIndex, out var entry) && entry.Hash == hash && entry.Polyline != null)
                return entry.Polyline;

            var res = EstimateCatmullResolution(p0, p1, p2, p3, pixelStep, cam, lowQuality);
            var poly = SampleCatmullUniform(p0, p1, p2, p3, res);

            _catmullCache[segmentIndex] = new Entry { Hash = hash, Polyline = poly };
            return poly;
        }

        private static int EstimateBezierResolution(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float pixelStep, Camera cam, bool lowQuality)
        {
#if UNITY_EDITOR
            var a = HandleUtility.WorldToGUIPoint(p0);
            var b = HandleUtility.WorldToGUIPoint(p3);
            var chord = (b - a).magnitude;
            float DistToLine(Vector3 p)
            {
                var gp = HandleUtility.WorldToGUIPoint(p);
                var v = b - a;
                var n = new Vector2(-v.y, v.x); // 2D法线
                return Mathf.Abs(Vector2.Dot(gp - a, n.normalized));
            }
            var curv = Mathf.Max(DistToLine(p1), DistToLine(p2));
            var baseRes = Mathf.CeilToInt(chord / Mathf.Max(pixelStep, 2f));
            var bonus = Mathf.Clamp(Mathf.RoundToInt(curv / Mathf.Max(pixelStep, 2f)) * 2, 0, 64);
            var res = baseRes + bonus;
            var minRes = lowQuality ? 8 : 20;
            var maxRes = lowQuality ? 256 : 512;
            return Mathf.Clamp(res, minRes, maxRes);
#else
            return lowQuality ? 8 : 20;
#endif
        }

        private static int EstimateCatmullResolution(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float pixelStep, Camera cam, bool lowQuality)
        {
#if UNITY_EDITOR
            var a = HandleUtility.WorldToGUIPoint(p1);
            var b = HandleUtility.WorldToGUIPoint(p2);
            var chord = (b - a).magnitude;
            // 简化曲率估计：通过二次差分近似
            var c0 = HandleUtility.WorldToGUIPoint(p0);
            var c3 = HandleUtility.WorldToGUIPoint(p3);
            var d1 = (b - a).magnitude;
            var d0 = (a - c0).magnitude;
            var d2 = (c3 - b).magnitude;
            var curv = Mathf.Abs(d1 - 0.5f * (d0 + d2));
            var baseRes = Mathf.CeilToInt(chord / Mathf.Max(pixelStep, 2f));
            var bonus = Mathf.Clamp(Mathf.RoundToInt(curv / Mathf.Max(pixelStep, 2f)) * 2, 0, 64);
            var res = baseRes + bonus;
            var minRes = lowQuality ? 8 : 20;
            var maxRes = lowQuality ? 256 : 512;
            return Mathf.Clamp(res, minRes, maxRes);
#else
            return lowQuality ? 8 : 20;
#endif
        }

        private static Vector3[] SampleBezierUniform(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int resolution)
        {
            var count = resolution + 1;
            var arr = new Vector3[count];
            for (int i = 0; i <= resolution; i++)
            {
                var t = i / (float)resolution;
                arr[i] = Bezier(p0, p1, p2, p3, t);
            }
            return arr;
        }

        private static Vector3[] SampleCatmullUniform(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int resolution)
        {
            var count = resolution + 1;
            var arr = new Vector3[count];
            for (int i = 0; i <= resolution; i++)
            {
                var t = i / (float)resolution;
                arr[i] = Catmull(p0, p1, p2, p3, t);
            }
            return arr;
        }

        private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;
            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

        private static Vector3 Catmull(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var tt = t * t;
            var ttt = tt * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
            );
        }
    }
}

