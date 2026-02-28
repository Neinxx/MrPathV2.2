using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using MrPathV2.Extensions;

namespace MrPathV2
{
    /// <summary>
    /// 将 StylizedRoadRecipe 的数据烘焙为 Job 友好的结构。
    /// 统一生成遮罩采样条（Strip），并记录 BlendMode 与不透明度，供预览与地形涂刷共享。
    /// </summary>
    public struct RecipeData : System.IDisposable
    {
        [ReadOnly] public NativeArray<int> terrainLayerIndices; // 与 Terrain 的 splat 索引对应（预览可为 -1）
        [ReadOnly] public NativeArray<int> blendModes;          // 对应 BlendMode 的枚举整数值
        [ReadOnly] public NativeArray<float> opacities;         // 每层不透明度（0~1）

        // 统一的遮罩采样条：把每层的遮罩（Gradient/Noise/Texture）采样为固定长度的一维数组
        [ReadOnly] public NativeArray<float> strips;            // 长度 = stripResolution * Length
        [ReadOnly] public NativeArray<int2> stripSlices;        // 每层在 strips 中的起始偏移与长度（length = stripResolution）
        [ReadOnly] public int stripResolution;                  // 采样条分辨率（固定长度）

        // 兼容旧实现：仍保留曲线关键帧（用于外部可能的评估复用），但当前共享算法使用 strips
        [ReadOnly] public NativeArray<Keyframe> gradientKeys;   // 合并后的所有关键帧
        [ReadOnly] public NativeArray<int2> gradientKeySlices;  // 每层对应的 keys 片段范围
        [ReadOnly] public NativeArray<float4> maskLUT256;   // 长度=256，每像素RGBA对应前4层
        public int Length { get; private set; }

        public RecipeData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap, float roadWorldWidth, Allocator allocator)
        {
            var blends = recipe?.blendLayers?.ToArray() ?? System.Array.Empty<BlendLayer>();
            Length = blends.Length;
            terrainLayerIndices = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<int>(Length, allocator);
            blendModes = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<int>(Length, allocator);
            opacities = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<float>(Length, allocator);
            stripResolution = 128; // 统一采样分辨率（足够平滑且计算开销低）
            strips = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<float>(math.max(1, stripResolution) * math.max(1, Length), allocator);
            stripSlices = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<int2>(Length, allocator);
            gradientKeySlices = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<int2>(Length, allocator);
            // 新增：为 256x1 RGBA LUT 分配空间
            maskLUT256 = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<float4>(256, allocator);

            int totalKeyframes = 0;
            if (blends != null)
            {
                foreach (var b in blends)
                {
                    var activeMask = b?.GetActiveMask();
                    var gradAsset = activeMask as GradientMask;
                    var keys = gradAsset != null ? (gradAsset.gradient?.keys ?? System.Array.Empty<Keyframe>())
                                                 : (b?.blendMask?.gradient?.keys ?? System.Array.Empty<Keyframe>());
                    totalKeyframes += keys.Length;
                }
            }
            gradientKeys = MrPathV2.Extensions.NativeArrayExtensions.CreateTracked<Keyframe>(math.max(1, totalKeyframes), allocator);

            int keyOffset = 0;
            int stripOffset = 0;
            for (int i = 0; i < Length; i++)
            {
                var b = blends[i];
                int idx = (b?.terrainLayer != null && terrainLayerMap != null && terrainLayerMap.ContainsKey(b.terrainLayer))
                    ? terrainLayerMap[b.terrainLayer] : -1;
                terrainLayerIndices[i] = idx;

                blendModes[i] = b != null ? (int)b.blendMode : 0; // 默认 Normal=0
                opacities[i] = Mathf.Clamp01(b != null ? b.opacity * recipe.masterOpacity : recipe.masterOpacity);

                // 兼容：若使用 GradientMask 资产则读取其曲线关键帧，否则读取旧字段
                var activeMask = b?.GetActiveMask();
                var gradAsset = activeMask as GradientMask;
                var keys = gradAsset != null ? (gradAsset.gradient?.keys ?? System.Array.Empty<Keyframe>())
                                             : (b?.blendMask?.gradient?.keys ?? System.Array.Empty<Keyframe>());
                for (int k = 0; k < keys.Length; k++) gradientKeys[keyOffset + k] = keys[k];
                gradientKeySlices[i] = new int2(keyOffset, keys.Length);
                keyOffset += keys.Length;

                // 采样遮罩为一维 Strip（-1..1 -> 0..stripResolution-1）
                stripSlices[i] = new int2(stripOffset, stripResolution);
                for (int s = 0; s < stripResolution; s++)
                {
                    float t = s / (float)(stripResolution - 1);    // 0..1
                    float pos = Mathf.Lerp(-1f, 1f, t);             // -1..1（横向位置）
                    float v = 1f;
                    var activeBrush = b?.GetActiveMask(); // 新资产引用
                    if (activeBrush != null)
                    {
                        v = Mathf.Clamp01(activeBrush.Evaluate(pos, roadWorldWidth));
                    }
                    else
                    {
                        // 回退到旧字段逻辑
                        var mask = b?.blendMask;
                        if (mask != null)
                        {
                            switch (mask.maskType)
                            {
                                case BlendMaskType.PositionalGradient:
                                    v = mask.gradient != null ? Mathf.Clamp01(mask.gradient.Evaluate(pos)) : 1f;
                                    break;
                                case BlendMaskType.ProceduralNoise:
                                    float scale = Mathf.Max(0.0001f, mask.noiseScale);
                                    v = Mathf.Clamp01(Mathf.PerlinNoise(s / scale, 0.5f) * mask.noiseStrength);
                                    break;
                                case BlendMaskType.CustomTexture:
                                    if (mask.customTexture != null)
                                    {
                                        var tex2D = mask.customTexture as Texture2D;
                                        if (tex2D != null && tex2D.isReadable)
                                        {
                                            int texX = Mathf.Clamp(Mathf.RoundToInt(t * (tex2D.width - 1)), 0, tex2D.width - 1);
                                            int texY = tex2D.height / 2;
                                            var c = tex2D.GetPixel(texX, texY);
                                            v = c.a;
                                        }
                                        else v = 1f;
                                    }
                                    else v = 0f;
                                    break;
                                default:
                                    v = 1f;
                                    break;
                            }
                        }
                    }
                    // 应用不透明度后写入条带
                    v = Mathf.Clamp01(v * opacities[i]);
                    strips[stripOffset + s] = v;
                }
                stripOffset += stripResolution;
            }
            // 生成 LUT：256长度，每像素RGBA存4层(已混合并归一化)
            for (int p = 0; p < 256; p++)
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
                // 不再对权重做归一化，保留原始不透明度，后续使用阶段再统一归一化/Clamp
                r = math.clamp(r, 0f, 1f);
                g = math.clamp(g, 0f, 1f);
                b = math.clamp(b, 0f, 1f);
                a = math.clamp(a, 0f, 1f);
                maskLUT256[p] = new float4(r, g, b, a);
            }

            // ------ 统一长度，避免后续 Job 越界访问 ------
            // 由于部分数组可能根据 Length=0 而大小为0，需保证 Length 不超过各数组最小长度
            int minLen = math.min(Length, math.min(blendModes.Length, math.min(opacities.Length, stripSlices.Length)));
            Length = minLen;
        }

        // 验证数据是否已创建
        public bool IsCreated => terrainLayerIndices.IsCreated;

        public void Dispose()
        {
            if (terrainLayerIndices.IsCreated) terrainLayerIndices.Dispose();
            if (blendModes.IsCreated) blendModes.Dispose();
            if (opacities.IsCreated) opacities.Dispose();
            if (strips.IsCreated) strips.Dispose();
            if (stripSlices.IsCreated) stripSlices.Dispose();
            if (gradientKeys.IsCreated) gradientKeys.Dispose();
            if (gradientKeySlices.IsCreated) gradientKeySlices.Dispose();
            if (maskLUT256.IsCreated) maskLUT256.Dispose();
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
        public static RecipeData BakeRecipe(StylizedRoadRecipe recipe, Allocator allocator, float roadWorldWidth = -1)
        {
            if (roadWorldWidth < 0) roadWorldWidth = 10f; // 默认宽度
            return new RecipeData(recipe, null, roadWorldWidth, allocator);
        }

        /// <summary>
        /// 创建默认的 RecipeData
        /// </summary>
        public static RecipeData CreateDefaultRecipe(Allocator allocator)
        {
            return new RecipeData(null, null, 10f, allocator);
        }
    }
}