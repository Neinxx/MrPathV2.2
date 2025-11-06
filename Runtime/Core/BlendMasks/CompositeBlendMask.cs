using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2.Runtime.Core.BlendMasks
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Composite Mask")]
    public class CompositeBlendMask : BlendMaskBase
    {
        public enum CompositeMode
        {
            Add,
            Multiply,
            Max,
            Min
        }

        [Tooltip("组合模式：加法/乘法/最大/最小")] public CompositeMode mode = CompositeMode.Multiply;
        [Tooltip("子遮罩条目列表（从上到下顺序叠加）")] public List<Entry> entries = new List<Entry>();

        // 组合遮罩暂不支持 GPU（需要在 HLSL 中引入汇总逻辑）。
        public override bool SupportsGpu => false;

        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            if (entries == null || entries.Count == 0)
                return 1f; // 无子项视为“全通”

            switch (mode)
            {
                case CompositeMode.Multiply:
                {
                    var result = 1f;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e == null || !e.enabled || e.mask == null) continue;
                        var v = Mathf.Clamp01(e.mask.Evaluate(horizontalPosition, pathProgress, worldWidth, pathLength));
                        var wv = Mathf.Lerp(1f, v, Mathf.Clamp01(e.weight)); // 权重 0=忽略，1=完全乘以 v
                        result *= wv;
                        if (result <= 0f) return 0f; // 提前返回
                    }
                    return ApplySmoothing(Mathf.Clamp01(result));
                }
                case CompositeMode.Add:
                {
                    var sum = 0f;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e == null || !e.enabled || e.mask == null) continue;
                        var v = Mathf.Clamp01(e.mask.Evaluate(horizontalPosition, pathProgress, worldWidth, pathLength));
                        sum += v * Mathf.Clamp01(e.weight);
                        if (sum >= 1f) return 1f; // 提前返回
                    }
                    return ApplySmoothing(Mathf.Clamp01(sum));
                }
                case CompositeMode.Max:
                {
                    var mx = 0f;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e == null || !e.enabled || e.mask == null) continue;
                        var v = Mathf.Clamp01(e.mask.Evaluate(horizontalPosition, pathProgress, worldWidth, pathLength));
                        mx = Mathf.Max(mx, v * Mathf.Clamp01(e.weight));
                        if (mx >= 1f) return 1f; // 提前返回
                    }
                    return ApplySmoothing(Mathf.Clamp01(mx));
                }
                case CompositeMode.Min:
                {
                    var mn = 1f;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e == null || !e.enabled || e.mask == null) continue;
                        var v = Mathf.Clamp01(e.mask.Evaluate(horizontalPosition, pathProgress, worldWidth, pathLength));
                        mn = Mathf.Min(mn, v * Mathf.Clamp01(e.weight));
                        if (mn <= 0f) return 0f; // 提前返回
                    }
                    return ApplySmoothing(Mathf.Clamp01(mn));
                }
                default:
                    return 1f;
            }
        }

        [Serializable]
        public class Entry
        {
            public bool enabled = true;
            public BlendMaskBase mask;
            [Range(0f, 1f)] public float weight = 1f;
        }
    }
}
