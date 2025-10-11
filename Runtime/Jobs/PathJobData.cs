// PathJobData.cs (最终安全访问器版)
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
namespace MrPathV2
{

    /// <summary>
    /// (保持不变)
    /// </summary>
    public struct PathSpineForJob
    {

        [ReadOnly] public NativeArray<Vector3> points;
        [ReadOnly] public NativeArray<Vector3> tangents;
        [ReadOnly] public NativeArray<Vector3> surfaceNormals;

        public PathSpineForJob(NativeArray<Vector3> p, NativeArray<Vector3> t, NativeArray<Vector3> n)
        {
            points = p;
            tangents = t;
            surfaceNormals = n;
        }

    }

    /// <summary>
    /// ✨【最终安全版：数据管家】✨
    /// </summary>
    public struct PathProfileJobData : IDisposable
    {
        [ReadOnly] private NativeArray<float> _widths;
        [ReadOnly] private NativeArray<float> _hOffsets;
        [ReadOnly] private NativeArray<float> _vOffsets;
        [ReadOnly] private NativeArray<int> _terrainLayerIndices;
        [ReadOnly] private NativeArray<Keyframe> _allGradientKeys;
        [ReadOnly] private NativeArray<int2> _gradientKeySlices;

        public int Length { get; private set; }

        public PathProfileJobData(List<PathLayer> layers, Dictionary<TerrainLayer, int> terrainLayerMap, Allocator allocator)
        {
            // ... 构造函数逻辑完全不变 ...
            Length = layers.Count;
            _widths = new NativeArray<float>(Length, allocator);
            _hOffsets = new NativeArray<float>(Length, allocator);
            _vOffsets = new NativeArray<float>(Length, allocator);
            _terrainLayerIndices = new NativeArray<int>(Length, allocator);
            _gradientKeySlices = new NativeArray<int2>(Length, allocator);

            int totalKeyframes = 0;
            foreach (var layer in layers)
            {
                var blendLayers = layer.terrainPaintingRecipe?.blendLayers;
                if (blendLayers != null && blendLayers.Count > 0)
                {
                    var first = blendLayers[0];
                    var gradAsset = first.mask as MrPathV2.GradientMask;
                    var curve = gradAsset != null ? gradAsset.gradient : first.blendMask.gradient;
                    totalKeyframes += (curve != null ? curve.keys.Length : 0);
                }
            }
            _allGradientKeys = new NativeArray<Keyframe>(totalKeyframes, allocator);

            int keyframeOffset = 0;
            for (int i = 0; i < Length; i++)
            {
                var layer = layers[i];
                _widths[i] = layer.width;
                _hOffsets[i] = layer.horizontalOffset;
                _vOffsets[i] = layer.verticalOffset;

                var blendLayer = layer.terrainPaintingRecipe?.blendLayers?.Count > 0 ? layer.terrainPaintingRecipe.blendLayers[0] : null;
                var terrainLayerAsset = blendLayer?.terrainLayer;

                _terrainLayerIndices[i] = (terrainLayerAsset != null && terrainLayerMap != null && terrainLayerMap.ContainsKey(terrainLayerAsset))
                    ? terrainLayerMap[terrainLayerAsset] : -1;

                var gradAsset2 = blendLayer?.mask as MrPathV2.GradientMask;
                var gradient = gradAsset2 != null ? gradAsset2.gradient : (blendLayer?.blendMask?.gradient ?? new AnimationCurve());
                var keys = gradient.keys;

                for (int k = 0; k < keys.Length; k++) _allGradientKeys[keyframeOffset + k] = keys[k];

                _gradientKeySlices[i] = new int2(keyframeOffset, keys.Length);
                keyframeOffset += keys.Length;
            }
        }

        /// <summary>
        /// ✨【安全访问器】✨
        /// 它现在直接持有对 NativeArray 的引用（通过值拷贝），不再需要任何 unsafe 代码。
        /// </summary>
        public readonly struct LayerAccessor
        {
            // 直接持有对数据数组的“引用”
            [ReadOnly] private readonly NativeArray<float> _widths;
            [ReadOnly] private readonly NativeArray<float> _hOffsets;
            [ReadOnly] private readonly NativeArray<float> _vOffsets;
            [ReadOnly] private readonly NativeArray<int> _terrainLayerIndices;
            [ReadOnly] private readonly NativeArray<Keyframe> _allGradientKeys;
            [ReadOnly] private readonly NativeArray<int2> _gradientKeySlices;
            private readonly int _index;

            public LayerAccessor(in PathProfileJobData data, int index)
            {
                // 将所有数组的引用拷贝过来
                _widths = data._widths;
                _hOffsets = data._hOffsets;
                _vOffsets = data._vOffsets;
                _terrainLayerIndices = data._terrainLayerIndices;
                _allGradientKeys = data._allGradientKeys;
                _gradientKeySlices = data._gradientKeySlices;
                _index = index;
            }

            // 属性访问逻辑不变，但现在是完全安全的
            public float Width => _widths[_index];
            public float HOffset => _hOffsets[_index];
            public float VOffset => _vOffsets[_index];
            public int TerrainLayerIndex => _terrainLayerIndices[_index];

            // 评估曲线的逻辑不变，但现在是完全安全的
            public float EvaluateGradient(float time)
            {
                int2 slice = _gradientKeySlices[_index];
                int start = slice.x;
                int count = slice.y;

                if (count == 0) return 0;
                // ... 内部逻辑完全不变 ...
                if (count == 1) return _allGradientKeys[start].value;

                int end = start + count - 1;
                for (int i = start; i < end; i++)
                {
                    var key1 = _allGradientKeys[i];
                    var key2 = _allGradientKeys[i + 1];
                    if (key1.time <= time && key2.time >= time)
                    {
                        float segmentDuration = key2.time - key1.time;
                        if (segmentDuration <= 0.0001f) return key1.value;
                        float t = (time - key1.time) / segmentDuration;
                        return math.lerp(key1.value, key2.value, t);
                    }
                }
                if (time < _allGradientKeys[start].time) return _allGradientKeys[start].value;
                return _allGradientKeys[end].value;
            }
        }

        // 索引器不变
        public readonly LayerAccessor this[int index] => new(in this, index);

        /// <summary>
        /// 释放所有内部的NativeArray。
        /// </summary>
        public void Dispose()
        {
            if (_widths.IsCreated) _widths.Dispose();
            if (_hOffsets.IsCreated) _hOffsets.Dispose();
            if (_vOffsets.IsCreated) _vOffsets.Dispose();
            if (_terrainLayerIndices.IsCreated) _terrainLayerIndices.Dispose();
            if (_allGradientKeys.IsCreated) _allGradientKeys.Dispose();
            if (_gradientKeySlices.IsCreated) _gradientKeySlices.Dispose();
        }
    }
}