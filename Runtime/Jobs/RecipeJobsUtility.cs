using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2._2.Runtime.Core; // add near top
using __temp.MrPathV2._2.Runtime.Core.BlendMasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    /// <summary>
    /// 将 StylizedRoadRecipe 的数据烘焙为 Job 友好的结构。
    /// 统一生成遮罩采样条（Strip），并记录 BlendMode 与不透明度，供预览与地形涂刷共享。
    /// </summary>
    public struct RecipeData : System.IDisposable
    {
        [ReadOnly] public NativeArray<int> TerrainLayerIndices; // 与 Terrain 的 splat 索引对应（预览可为 -1）
        [ReadOnly] public NativeArray<int> BlendModes;          // 对应 BlendMode 的枚举整数值
        [ReadOnly] public NativeArray<float> Opacities;         // 每层不透明度（0~1）

        // 统一的遮罩采样条：把每层的遮罩（Gradient/Noise/Texture）采样为固定长度的一维数组
        [ReadOnly] public NativeArray<float> Strips;            // 长度 = stripResolution * Length
        [ReadOnly] public NativeArray<int2> StripSlices;        // 每层在 strips 中的起始偏移与长度（length = stripResolution）
        [ReadOnly] public readonly int StripResolution;                  // 采样条分辨率（固定长度）

        // 兼容旧实现：仍保留曲线关键帧（用于外部可能的评估复用），但当前共享算法使用 strips
        [ReadOnly] public NativeArray<Keyframe> GradientKeys;   // 合并后的所有关键帧
        [ReadOnly] public NativeArray<int2> GradientKeySlices;  // 每层对应的 keys 片段范围
        [ReadOnly] public NativeArray<float4> MaskLut256;   // 删除该字段及相关逻辑
        // 新 2D MaskAtlas，每行对应一层，单通道 R 保存权重
        [ReadOnly] public NativeArray<float> MaskAtlas;    // 长度 = atlasWidth * atlasHeight
        public readonly int AtlasWidth;
        public readonly int AtlasHeight;
        public readonly int PathSamples;
        public readonly float MaskThreshold;  // 遮罩阈值，用于匹配GPU着色器逻辑
        public int Length { get; }

        public RecipeData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap, float roadWorldWidth, float roadWorldLength, Allocator allocator)
        {
            var roadLayers = recipe?.GetLayers()?.ToArray() ?? System.Array.Empty<RoadLayer>();
            Length = roadLayers.Length;
            TerrainLayerIndices = Extensions.NativeArrayExtensions.CreateTracked<int>(Length, allocator);
            BlendModes = Extensions.NativeArrayExtensions.CreateTracked<int>(Length, allocator);
            Opacities = Extensions.NativeArrayExtensions.CreateTracked<float>(Length, allocator);
            StripResolution = 128; // 统一采样分辨率（足够平滑且计算开销低）
            Strips = Extensions.NativeArrayExtensions.CreateTracked<float>(math.max(1, StripResolution) * math.max(1, Length), allocator);
            StripSlices = Extensions.NativeArrayExtensions.CreateTracked<int2>(Length, allocator);
            GradientKeySlices = Extensions.NativeArrayExtensions.CreateTracked<int2>(Length, allocator);
            // 已弃用: maskLUT256 逻辑已被 MaskAtlas 取代，但为兼容旧 Job 结构体仍分配空数组确保 IsCreated=true
            MaskLut256 = Extensions.NativeArrayExtensions.CreateTracked<float4>(1, allocator); // length 1, minimal

            // 设定 MaskAtlas 分辨率（与 Preview 保持一致，可后续参数化）
            AtlasWidth = 256;
            PathSamples = 64; // 纵向采样数，可后续做成可配置
            AtlasHeight = math.max(1, Length * PathSamples);
            MaskAtlas = Extensions.NativeArrayExtensions.CreateTracked<float>(AtlasWidth * AtlasHeight, allocator);
            
            // 设置遮罩阈值以匹配GPU着色器逻辑（默认0，与预览材质设置一致）
            MaskThreshold = 0f;

            _disposed = false;

            var totalKeyframes = 0;
            totalKeyframes += (from b in roadLayers select b?.layerMask as GradientMask into gradAsset select gradAsset ? (gradAsset.gradient?.keys ?? System.Array.Empty<Keyframe>()) : System.Array.Empty<Keyframe>() into keys select keys.Length).Sum();
            GradientKeys = Extensions.NativeArrayExtensions.CreateTracked<Keyframe>(math.max(1, totalKeyframes), allocator);

            var keyOffset = 0;
            var stripOffset = 0;
            for (var i = 0; i < Length; i++)
            {
                var b = roadLayers[i];
                var activeMask = b?.layerMask;
                 var idx = (b?.contentLayer && terrainLayerMap != null && terrainLayerMap.TryGetValue(b.contentLayer, out var value))
                    ? value : -1;
                TerrainLayerIndices[i] = idx;

                BlendModes[i] = b != null ? (int)b.blendMode : 0; // 默认 Normal=0
                if (recipe)
                    Opacities[i] = Mathf.Clamp01(b != null ? b.opacity * recipe.masterOpacity : recipe.masterOpacity);

                var gradAsset = b?.layerMask as GradientMask;
                var keys = gradAsset ? (gradAsset.gradient?.keys ?? System.Array.Empty<Keyframe>()) : System.Array.Empty<Keyframe>();
                for (var k = 0; k < keys.Length; k++) GradientKeys[keyOffset + k] = keys[k];
                GradientKeySlices[i] = new int2(keyOffset, keys.Length);
                keyOffset += keys.Length;

                StripSlices[i] = new int2(stripOffset, StripResolution);
                for (var s = 0; s < StripResolution; s++)
                {
                    var t = s / (float)(StripResolution - 1);    // 0..1
                    var pos = Mathf.Lerp(-1f, 1f, t);             // -1..1
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
            // 生成 LUT：256长度，每像素RGBA存4层(已混合并归一化)
            /*for (int p = 0; p < 256; p++)
            {
                float normalizedDist = p / 255f; // 0..1 center=0 edges=1
                float r = 0, g = 0, b = 0, a = 0;
                for (int li = 0; li < math.min(4, Length); li++)
                {
                    // 计算遮罩值（已包含不透明度）
                    int sliceStart = stripSlices[li].x;
                    int res = stripResolution;
                    float fIdx = normalizedDist * (res - 1);
                    int ia = (int)math.floor(fIdx);
                    ia = math.clamp(ia, 0, res - 1);
                    int ib = math.min(ia + 1, res - 1);
                    float w = fIdx - ia;
                    float va = strips[sliceStart + ia];
                    float vb = strips[sliceStart + ib];
                    float v = math.lerp(va, vb, w);
                    // Blend
                    int mode = blendModes[li];
                    switch (li)
                    {
                        case 0: r = TerrainJobsUtility.Blend(r, v, mode); break;
                        case 1: g = TerrainJobsUtility.Blend(g, v, mode); break;
                        case 2: b = TerrainJobsUtility.Blend(b, v, mode); break;
                        case 3: a = TerrainJobsUtility.Blend(a, v, mode); break;
                    }
                }
                r = math.clamp(r, 0f, 1f);
                g = math.clamp(g, 0f, 1f);
                b = math.clamp(b, 0f, 1f);
                a = math.clamp(a, 0f, 1f);
                // maskLUT256[p] = new float4(r, g, b, a);
            }*/
            // 待移除的旧 LUT 生成逻辑已清理

            // 生成 2D MaskAtlas：
            // Y 方向先是 layer，再是 pathProgress 采样，共 Length * PathSamples 行。
            for (var li = 0; li < Length; li++)
            {
                var sliceStart = StripSlices[li].x;
                var res = StripResolution;

                for (var py = 0; py < PathSamples; py++)
                {
                    var pathProgress = py / (float)(PathSamples - 1); // 0..1
                    var rowIndex = li * PathSamples + py;

                    for (var x = 0; x < AtlasWidth; x++)
                    {
                        var t = x / (float)(AtlasWidth - 1);
                        var fIdx = t * (res - 1);
                        var ia = (int)math.floor(fIdx);
                        ia = math.clamp(ia, 0, res - 1);
                        var ib = math.min(ia + 1, res - 1);
                        var w = fIdx - ia;
                        var va = Strips[sliceStart + ia];
                        var vb = Strips[sliceStart + ib];
                        var v = math.lerp(va, vb, w);
                        // 写入 atlas
                        var idx = rowIndex * AtlasWidth + x;
                        MaskAtlas[idx] = v; // 先用横向strip值，稍后将考虑沿 pathProgress 的变化
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

            if (TerrainLayerIndices.IsCreated) TerrainLayerIndices.Dispose();
            if (BlendModes.IsCreated) BlendModes.Dispose();
            if (Opacities.IsCreated) Opacities.Dispose();
            if (Strips.IsCreated) Strips.Dispose();
            if (StripSlices.IsCreated) StripSlices.Dispose();
            if (GradientKeys.IsCreated) GradientKeys.Dispose();
            if (GradientKeySlices.IsCreated) GradientKeySlices.Dispose();
            if (MaskLut256.IsCreated) MaskLut256.Dispose();
            if (MaskAtlas.IsCreated) MaskAtlas.Dispose();
        }
    }

    /// <summary>
    /// RecipeJobsUtility 静态工具类
    /// </summary>
    public static class RecipeJobsUtility
    {
        /// <summary>
        /// 烘焙 StylizedRoadRecipe 为 Job 友好的数据结构
        /// </summary>
        public static RecipeData BakeRecipe(StylizedRoadRecipe recipe, Allocator allocator, float roadWorldWidth = -1, float roadWorldLength = -1)
        {
            if (roadWorldWidth < 0) roadWorldWidth = 10f; // 默认宽度
            if (roadWorldLength < 0) roadWorldLength = 100f; // 默认长度
            return new RecipeData(recipe, null, roadWorldWidth, roadWorldLength, allocator);
        }

        /// <summary>
        /// 创建默认的 RecipeData
        /// </summary>
        public static RecipeData CreateDefaultRecipe(Allocator allocator)
        {
            return new RecipeData(null, null, 10f, 100f, allocator);
        }
    }
}