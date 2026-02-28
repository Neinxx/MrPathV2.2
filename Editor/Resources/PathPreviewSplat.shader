Shader "MrPath/PathPreviewSplat"
{
    Properties
    {
        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
        [Space]
        _PreviewAlpha("Preview Alpha", Range(0, 1)) = 0.6
        _MaskStrength ("Mask Strength", Range(0,4)) = 1

        [Header(Layers)]
        // Define textures 0 - 15. Tiling/Opacity/BlendMode are now provided via arrays
        _Layer0_Texture("Layer 0", 2D) = "white" {}
        _Layer0_Color("Layer 0 Color", Color) = (1,1,1,1)
        _Layer1_Texture("Layer 1", 2D) = "white" {}
        _Layer1_Color("Layer 1 Color", Color) = (1,1,1,1)
        _Layer2_Texture("Layer 2", 2D) = "white" {}
        _Layer2_Color("Layer 2 Color", Color) = (1,1,1,1)
        _Layer3_Texture("Layer 3", 2D) = "white" {}
        _Layer3_Color("Layer 3 Color", Color) = (1,1,1,1)
        _Layer4_Texture("Layer 4", 2D) = "white" {}
        _Layer4_Color("Layer 4 Color", Color) = (1,1,1,1)
        _Layer5_Texture("Layer 5", 2D) = "white" {}
        _Layer5_Color("Layer 5 Color", Color) = (1,1,1,1)
        _Layer6_Texture("Layer 6", 2D) = "white" {}
        _Layer6_Color("Layer 6 Color", Color) = (1,1,1,1)
        _Layer7_Texture("Layer 7", 2D) = "white" {}
        _Layer7_Color("Layer 7 Color", Color) = (1,1,1,1)
        _Layer8_Texture("Layer 8", 2D) = "white" {}
        _Layer8_Color("Layer 8 Color", Color) = (1,1,1,1)
        _Layer9_Texture("Layer 9", 2D) = "white" {}
        _Layer9_Color("Layer 9 Color", Color) = (1,1,1,1)
        _Layer10_Texture("Layer 10", 2D) = "white" {}
        _Layer10_Color("Layer 10 Color", Color) = (1,1,1,1)
        _Layer11_Texture("Layer 11", 2D) = "white" {}
        _Layer11_Color("Layer 11 Color", Color) = (1,1,1,1)
        _Layer12_Texture("Layer 12", 2D) = "white" {}
        _Layer12_Color("Layer 12 Color", Color) = (1,1,1,1)
        _Layer13_Texture("Layer 13", 2D) = "white" {}
        _Layer13_Color("Layer 13 Color", Color) = (1,1,1,1)
        _Layer14_Texture("Layer 14", 2D) = "white" {}
        _Layer14_Color("Layer 14 Color", Color) = (1,1,1,1)
        _Layer15_Texture("Layer 15", 2D) = "white" {}
        _Layer15_Color("Layer 15 Color", Color) = (1,1,1,1)

        // Masking
        _MaskAtlas ("Mask Atlas", 2D) = "white" {}
        _AtlasInvHeight ("Atlas Inv Height", Float) = 1
        _MaskThreshold("Mask Threshold", Range(0,1)) = 0
        _AcrossScale("Across Scale", Float) = 1
        _LayerCount("Layer Count", Int) = 1
        // 新增：每层沿路径采样行数
        _PathSamples("Path Samples", Float) = 64
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+100" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/__temp/MrPathV2.2/Runtime/Shaders/BlendLayer.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;            // original mesh UV for across-mask calcs
                float2 worldUV : TEXCOORD1;       // world-space UV (XZ)
                half4 color : COLOR;              // vertex color (kept in COLOR semantic to free TEXCOORDs)
                float4 positionHCS : SV_POSITION;
            };

            // Texture declarations 0-15 (samplers are shared default repeat)
            TEXTURE2D(_Layer0_Texture); half4 _Layer0_Color;
            TEXTURE2D(_Layer1_Texture); half4 _Layer1_Color;
            TEXTURE2D(_Layer2_Texture); half4 _Layer2_Color;
            TEXTURE2D(_Layer3_Texture); half4 _Layer3_Color;
            TEXTURE2D(_Layer4_Texture); half4 _Layer4_Color;
            TEXTURE2D(_Layer5_Texture); half4 _Layer5_Color;
            TEXTURE2D(_Layer6_Texture); half4 _Layer6_Color;
            TEXTURE2D(_Layer7_Texture); half4 _Layer7_Color;
            TEXTURE2D(_Layer8_Texture); half4 _Layer8_Color;
            TEXTURE2D(_Layer9_Texture); half4 _Layer9_Color;
            TEXTURE2D(_Layer10_Texture); half4 _Layer10_Color;
            TEXTURE2D(_Layer11_Texture); half4 _Layer11_Color;
            TEXTURE2D(_Layer12_Texture); half4 _Layer12_Color;
            TEXTURE2D(_Layer13_Texture); half4 _Layer13_Color;
            TEXTURE2D(_Layer14_Texture); half4 _Layer14_Color;
            TEXTURE2D(_Layer15_Texture); half4 _Layer15_Color;

            SAMPLER(sampler_LinearRepeat);
            SAMPLER(sampler_LinearClamp);

            TEXTURE2D(_MaskAtlas);
            float _AtlasInvHeight;
            float _MaskThreshold;
            float _PreviewAlpha;
            float _MaskStrength; // 控制遮罩整体强度
            float _AcrossScale;
            int _LayerCount;
            float _PathSamples; // 新增

            // Unified arrays pushed from C#
            CBUFFER_START(UnityPerMaterial)
                float4 _LayerTilings[16];
                float  _LayerOpacities[16];
                float  _LayerBlendModes[16];
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(worldPos);
                o.uv = input.uv;
                o.worldUV = worldPos.xz;
                o.color = input.color;
                return o;
            }

            half4 SampleLayerTexture(int idx, float2 uv)
            {
                switch(idx)
                {
                    case 0: return SAMPLE_TEXTURE2D(_Layer0_Texture, sampler_LinearRepeat, uv) * _Layer0_Color;
                    case 1: return SAMPLE_TEXTURE2D(_Layer1_Texture, sampler_LinearRepeat, uv) * _Layer1_Color;
                    case 2: return SAMPLE_TEXTURE2D(_Layer2_Texture, sampler_LinearRepeat, uv) * _Layer2_Color;
                    case 3: return SAMPLE_TEXTURE2D(_Layer3_Texture, sampler_LinearRepeat, uv) * _Layer3_Color;
                    case 4: return SAMPLE_TEXTURE2D(_Layer4_Texture, sampler_LinearRepeat, uv) * _Layer4_Color;
                    case 5: return SAMPLE_TEXTURE2D(_Layer5_Texture, sampler_LinearRepeat, uv) * _Layer5_Color;
                    case 6: return SAMPLE_TEXTURE2D(_Layer6_Texture, sampler_LinearRepeat, uv) * _Layer6_Color;
                    case 7: return SAMPLE_TEXTURE2D(_Layer7_Texture, sampler_LinearRepeat, uv) * _Layer7_Color;
                    case 8: return SAMPLE_TEXTURE2D(_Layer8_Texture, sampler_LinearRepeat, uv) * _Layer8_Color;
                    case 9: return SAMPLE_TEXTURE2D(_Layer9_Texture, sampler_LinearRepeat, uv) * _Layer9_Color;
                    case 10: return SAMPLE_TEXTURE2D(_Layer10_Texture, sampler_LinearRepeat, uv) * _Layer10_Color;
                    case 11: return SAMPLE_TEXTURE2D(_Layer11_Texture, sampler_LinearRepeat, uv) * _Layer11_Color;
                    case 12: return SAMPLE_TEXTURE2D(_Layer12_Texture, sampler_LinearRepeat, uv) * _Layer12_Color;
                    case 13: return SAMPLE_TEXTURE2D(_Layer13_Texture, sampler_LinearRepeat, uv) * _Layer13_Color;
                    case 14: return SAMPLE_TEXTURE2D(_Layer14_Texture, sampler_LinearRepeat, uv) * _Layer14_Color;
                    case 15: return SAMPLE_TEXTURE2D(_Layer15_Texture, sampler_LinearRepeat, uv) * _Layer15_Color;
                    default: return half4(1,1,1,1);
                }
            }

            float2 GetLayerTiling(int idx) { return max(_LayerTilings[idx].xy, float2(0.0001,0.0001)); }
            float  GetLayerOpacity(int idx) { return _LayerOpacities[idx]; }
            float  GetLayerBlendMode(int idx){ return _LayerBlendModes[idx]; }

            #define BlendLayer(baseColor, layerColor, mode, opacity) ApplyBlend(baseColor, layerColor, mode, opacity)

            half4 frag(Varyings input) : SV_Target
            {
                // 利用 LayerTilings[0].x 反推出跨宽度的纹理重复次数，使 across 基于世界坐标
                float acrossRepeat = max(_LayerTilings[0].x, 1e-5);
                float acrossPos = input.uv.x / acrossRepeat; // 0..1 从左到右
                float across = saturate(abs(acrossPos * 2.0 - 1.0) * _AcrossScale);

                // 计算沿路径的进度 (0..1)。使用世界坐标
                // repeat mask along path according to y-tiling: multiply then take frac
                float pathRepeat   = max(_LayerTilings[0].y, 1.0);
                float pathProgress = frac(input.uv.y * pathRepeat);
                
                // uv.y 中已带有沿路径的重复系数 (Tiling.y)，因此直接取 frac 得到局部进度 (0..1)
             //   float pathProgress = frac(input.uv.y);
                
                // 计算沿路径的进度 (0..1)。uv.y 已包含 Tiling.y，因此直接取 frac 得到局部重复进度
                // 计算沿路径的局部进度 (0..1)。uv.y 已包含 Tiling.y 的重复信息，因此直接使用 frac 获取即可
              //  float pathProgress = frac(input.uv.y);
                
                // 初始完全透明
                half4 finalColor = half4(0,0,0,0);
                float maxMask = 0.0;

                int maxLayers = min(_LayerCount, 16);
                for(int i=0;i<maxLayers;i++)
                {
                    float weight = SampleMaskAtlas2D(
                        _MaskAtlas,
                        sampler_LinearClamp,
                        across,
                        pathProgress,
                        i,
                        _PathSamples,
                        _AtlasInvHeight,
                        _MaskThreshold) * _MaskStrength;
                    if(weight < 1e-4) continue;

                    // Track the strongest mask influence for alpha computation later
                    maxMask = max(maxMask, weight);

                    float2 tiling = GetLayerTiling(i);
                    float2 layerUV = input.worldUV * tiling;
                    half4 layerColor = SampleLayerTexture(i, layerUV);
                    // 仅透明度受 mask 影响，颜色保持原值
                    layerColor.a   *= weight;

                    float opacity = GetLayerOpacity(i);
                    float mode = GetLayerBlendMode(i);
                    finalColor = BlendLayer(finalColor, layerColor, mode, opacity);
                }

                // Compute final alpha based on combined mask strength for better visual fidelity
                if(all(finalColor.rgb == 0))
                {
                    finalColor.a = 0;
                }
                else
                {
                    // Use the maximum mask weight scaled by preview alpha as the final alpha
                    finalColor.a = saturate(maxMask * _PreviewAlpha);
                }
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}