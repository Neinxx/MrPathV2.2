using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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
        public int Length { get; private set; }

        public RecipeData(StylizedRoadRecipe recipe, Dictionary<TerrainLayer, int> terrainLayerMap, Allocator allocator)
        {
            var blends = recipe?.blendLayers;
            Length = blends != null ? blends.Count : 0;
            terrainLayerIndices = new NativeArray<int>(Length, allocator);
            blendModes = new NativeArray<int>(Length, allocator);
            opacities = new NativeArray<float>(Length, allocator);
            stripResolution = 128; // 统一采样分辨率（足够平滑且计算开销低）
            strips = new NativeArray<float>(math.max(1, stripResolution) * math.max(1, Length), allocator);
            stripSlices = new NativeArray<int2>(Length, allocator);
            gradientKeySlices = new NativeArray<int2>(Length, allocator);

            int totalKeyframes = 0;
            if (blends != null)
            {
                foreach (var b in blends)
                {
                    var keys = b?.blendMask?.gradient?.keys;
                    if (keys != null) totalKeyframes += keys.Length;
                }
            }
            gradientKeys = new NativeArray<Keyframe>(totalKeyframes, allocator);

            int keyOffset = 0;
            int stripOffset = 0;
            for (int i = 0; i < Length; i++)
            {
                var b = blends[i];
                int idx = (b?.terrainLayer != null && terrainLayerMap != null && terrainLayerMap.ContainsKey(b.terrainLayer))
                    ? terrainLayerMap[b.terrainLayer] : -1;
                terrainLayerIndices[i] = idx;

                blendModes[i] = b != null ? (int)b.blendMode : 0; // 默认 Normal=0
                opacities[i] = Mathf.Clamp01(b != null ? b.opacity : 1f);

                // 兼容：若使用 GradientMask 资产则读取其曲线关键帧，否则读取旧字段
                var gradAsset = b?.mask as GradientMask;
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
                    var brush = b?.mask; // 新资产引用
                    if (brush != null)
                    {
                        v = Mathf.Clamp01(brush.Evaluate(pos));
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
        }

        public void Dispose()
        {
            if (terrainLayerIndices.IsCreated) terrainLayerIndices.Dispose();
            if (blendModes.IsCreated) blendModes.Dispose();
            if (opacities.IsCreated) opacities.Dispose();
            if (strips.IsCreated) strips.Dispose();
            if (stripSlices.IsCreated) stripSlices.Dispose();
            if (gradientKeys.IsCreated) gradientKeys.Dispose();
            if (gradientKeySlices.IsCreated) gradientKeySlices.Dispose();
        }
    }
}