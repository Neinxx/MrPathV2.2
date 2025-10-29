using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using NativeArrayExtensions = MrPathV2.Runtime.Jobs.Extensions.NativeArrayExtensions;
// add near top

namespace MrPathV2
{
    /// <summary>
    ///     将 StylizedRoadRecipe 的数据烘焙为 Job 友好的结构。
    ///     统一生成遮罩采样条（Strip），并记录 BlendMode 与不透明度，供预览与地形涂刷共享。
    /// </summary>
    public struct RecipeData : IDisposable
    {
        [ReadOnly] public NativeArray<int> TerrainLayerIndices; // 与 Terrain 的 splat 索引对应（预览可为 -1）
        [ReadOnly] public NativeArray<int> BlendModes; // 对应 BlendMode 的枚举整数值
        [ReadOnly] public NativeArray<float> Opacities; // 每层不透明度（0~1）

        // 统一的遮罩采样条：把每层的遮罩（Gradient/Noise/Texture）采样为固定长度的一维数组
        [ReadOnly] public NativeArray<float> Strips; // 长度 = stripResolution * Length
        [ReadOnly] public NativeArray<int2> StripSlices; // 每层在 strips 中的起始偏移与长度（length = stripResolution）
        [ReadOnly] public int StripResolution; // 采样条分辨率（固定长度）

        // 兼容旧实现：仍保留曲线关键帧（用于外部可能的评估复用），但当前共享算法使用 strips
        [ReadOnly] public NativeArray<Keyframe> GradientKeys; // 合并后的所有关键帧
        [ReadOnly] public NativeArray<int2> GradientKeySlices; // 每层对应的 keys 片段范围
        [ReadOnly] public NativeArray<float4> MaskLut256; // 删除该字段及相关逻辑
        // 新 2D MaskAtlas，每行对应一层，单通道 R 保存权重
        [ReadOnly] public NativeArray<float> MaskAtlas; // 长度 = atlasWidth * atlasHeight
        public int AtlasWidth;
        public int AtlasHeight;
        public int PathSamples;
        public int Length { get; set; }

        public RecipeData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap,
                  float roadWorldWidth, float roadWorldLength, Allocator allocator) : this()
      {
    // 初始化基础数据结构
    InitializeBaseData(recipe, allocator);

    // 初始化图层相关数据
    InitializeLayerData(recipe, terrainLayerMap, roadWorldWidth, roadWorldLength);

    // 初始化渐变关键帧数据
    InitializeGradientData(recipe);

    // 初始化遮罩条纹数据
    InitializeStripData(recipe, roadWorldWidth, roadWorldLength);

    // 初始化遮罩图集数据
    InitializeMaskAtlasData(recipe, roadWorldWidth, roadWorldLength);

    _disposed = false;
}

/// <summary>
/// 初始化基础数据结构
/// </summary>
private void InitializeBaseData(StylizedRoadRecipe recipe, Allocator allocator)
{
    var roadLayers = recipe?.GetLayers()?.Where(l => l != null && l.enabled).ToArray() ?? Array.Empty<RoadLayer>();
    Length = roadLayers.Length;
    TerrainLayerIndices = NativeArrayExtensions.CreateTracked<int>(Length, allocator);
    BlendModes = NativeArrayExtensions.CreateTracked<int>(Length, allocator);
    Opacities = NativeArrayExtensions.CreateTracked<float>(Length, allocator);

    // 统一采样分辨率（足够平滑且计算开销低）
    StripResolution = 128;
    Strips = NativeArrayExtensions.CreateTracked<float>(math.max(1, StripResolution) * math.max(1, Length), allocator);
    StripSlices = NativeArrayExtensions.CreateTracked<int2>(Length, allocator);
    GradientKeySlices = NativeArrayExtensions.CreateTracked<int2>(Length, allocator);

    // 已弃用: maskLUT256 逻辑已被 MaskAtlas 取代，但为兼容旧 Job 结构体仍分配空数组确保 IsCreated=true
    MaskLut256 = NativeArrayExtensions.CreateTracked<float4>(1, allocator); // length 1, minimal

    // 设定 MaskAtlas 分辨率（与 Preview 保持一致，可后续参数化）
    AtlasWidth = 256;
    PathSamples = 64; // 纵向采样数，可后续做成可配置
    AtlasHeight = math.max(1, Length * PathSamples);
    MaskAtlas = NativeArrayExtensions.CreateTracked<float>(AtlasWidth * AtlasHeight, allocator);
}

/// <summary>
/// 初始化图层相关数据
/// </summary>
private void InitializeLayerData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap,
                                float roadWorldWidth, float roadWorldLength)
{
    var roadLayers = recipe?.GetLayers()?.Where(l => l != null && l.enabled).ToArray() ?? Array.Empty<RoadLayer>();

    for (var i = 0; i < Length; i++)
    {
        var layer = roadLayers[i];
        var activeMask = layer?.layerMask;

        // 设置地形图层索引
        var idx = layer?.contentLayer && terrainLayerMap != null && terrainLayerMap.TryGetValue(layer.contentLayer, out var value)
            ? value
            : -1;
        TerrainLayerIndices[i] = idx;

        // 设置混合模式
        BlendModes[i] = layer != null ? (int)layer.blendMode : 0; // 默认 Normal=0

        // 设置不透明度
        if (recipe)
            Opacities[i] = Mathf.Clamp01(layer != null ? layer.opacity * recipe.masterOpacity : recipe.masterOpacity);
    }
}

/// <summary>
/// 初始化渐变关键帧数据
/// </summary>
private void InitializeGradientData(StylizedRoadRecipe recipe)
{
    var roadLayers = recipe?.GetLayers()?.Where(l => l != null && l.enabled).ToArray() ?? Array.Empty<RoadLayer>();

    // 计算总关键帧数
    var totalKeyframes = 0;
    totalKeyframes += (from layer in roadLayers
                       select layer?.layerMask as GradientMask into gradAsset
                       select gradAsset ? gradAsset.gradient?.keys ?? Array.Empty<Keyframe>() : Array.Empty<Keyframe>() into keys
                       select keys.Length).Sum();

    GradientKeys = NativeArrayExtensions.CreateTracked<Keyframe>(math.max(1, totalKeyframes), Allocator.Persistent);

    // 填充关键帧数据
    var keyOffset = 0;
    for (var i = 0; i < Length; i++)
    {
        var layer = roadLayers[i];
        var gradAsset = layer?.layerMask as GradientMask;
        var keys = gradAsset ? gradAsset.gradient?.keys ?? Array.Empty<Keyframe>() : Array.Empty<Keyframe>();

        for (var k = 0; k < keys.Length; k++)
            GradientKeys[keyOffset + k] = keys[k];

        GradientKeySlices[i] = new int2(keyOffset, keys.Length);
        keyOffset += keys.Length;
    }
}

/// <summary>
/// 初始化遮罩条纹数据
/// </summary>
private void InitializeStripData(StylizedRoadRecipe recipe, float roadWorldWidth, float roadWorldLength)
{
    var roadLayers = recipe?.GetLayers()?.Where(l => l != null && l.enabled).ToArray() ?? Array.Empty<RoadLayer>();
    var stripOffset = 0;

    for (var i = 0; i < Length; i++)
    {
        var layer = roadLayers[i];
        var activeMask = layer?.layerMask;

        StripSlices[i] = new int2(stripOffset, StripResolution);

        for (var s = 0; s < StripResolution; s++)
        {
            var t = s / (float)(StripResolution - 1); // 0..1
            var pos = Mathf.Lerp(-1f, 1f, t); // -1..1
            var v = 1f;

            if (activeMask)
            {
                v = Mathf.Clamp01(activeMask.Evaluate(pos, roadWorldWidth, roadWorldLength));
            }

            // 默认填充 1 (不影响底层)，再应用不透明度
            v = Mathf.Clamp01(v * Opacities[i]);
            Strips[stripOffset + s] = v;
        }

        stripOffset += StripResolution;
    }
}

/// <summary>
/// 初始化遮罩图集数据
/// </summary>
private void InitializeMaskAtlasData(StylizedRoadRecipe recipe, float roadWorldWidth, float roadWorldLength)
{
    var roadLayers = recipe?.GetLayers()?.Where(l => l != null && l.enabled).ToArray() ?? Array.Empty<RoadLayer>();

    // 生成 2D MaskAtlas：
    // Y 方向先是 layer，再是 pathProgress 采样，共 Length * PathSamples 行。
    for (var layerIndex = 0; layerIndex < Length; layerIndex++)
    {
        var activeMask = roadLayers.Length > layerIndex ? roadLayers[layerIndex]?.layerMask : null;

        for (var py = 0; py < PathSamples; py++)
        {
            var pathProgress = py / (float)(PathSamples - 1); // 0..1
            var rowIndex = layerIndex * PathSamples + py;

            for (var x = 0; x < AtlasWidth; x++)
            {
                var t = x / (float)(AtlasWidth - 1);
                var pos = Mathf.Lerp(-1f, 1f, t); // -1..1 横向

                var v = 1f;
                if (activeMask)
                {
                    v = Mathf.Clamp01(activeMask.Evaluate(pos, pathProgress, roadWorldWidth, roadWorldLength));
                }

                // 应用不透明度（已与 GPU 对齐）：mask * per-layer opacity * masterOpacity
                v = Mathf.Clamp01(v * Opacities[layerIndex]);

                // 写入 atlas
                MaskAtlas[rowIndex * AtlasWidth + x] = v;
            }
        }
    }
}


        // 验证数据是否已创建
        public bool IsCreated => TerrainLayerIndices.IsCreated;

        // 标记是否已释放，避免重复 Dispose
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            TerrainLayerIndices.SafeDispose();
            BlendModes.SafeDispose();
            Opacities.SafeDispose();
            Strips.SafeDispose();
            StripSlices.SafeDispose();
            GradientKeys.SafeDispose();
            GradientKeySlices.SafeDispose();
            MaskLut256.SafeDispose();
            MaskAtlas.SafeDispose();
        }
    }

    /// <summary>
    ///     RecipeJobsUtility 静态工具类
    /// </summary>
    public static class RecipeJobsUtility
    {
        /// <summary>
        ///     烘焙 StylizedRoadRecipe 为 Job 友好的数据结构
        /// </summary>
        public static RecipeData BakeRecipe(StylizedRoadRecipe recipe, Allocator allocator, float roadWorldWidth = -1, float roadWorldLength = -1)
        {
            if (roadWorldWidth < 0) roadWorldWidth = 10f; // 默认宽度
            if (roadWorldLength < 0) roadWorldLength = 100f; // 默认长度
            return new RecipeData(recipe, null, roadWorldWidth, roadWorldLength, allocator);
        }

        /// <summary>
        ///     创建默认的 RecipeData
        /// </summary>
        public static RecipeData CreateDefaultRecipe(Allocator allocator) => new RecipeData(null, null, 10f, 100f, allocator);
    }
}
