Shader "MrPath/PathPreviewSplat"
{
    Properties
    {
        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
        [Space]
        _PreviewAlpha("Preview Alpha", Range(0, 1)) = 0.5

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
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
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
            float _AcrossScale;
            int _LayerCount;

            // Unified arrays pushed from C#
            CBUFFER_START(UnityPerMaterial)
                float4 _LayerTilings[16];
                float  _LayerOpacities[16];
                float  _LayerBlendModes[16];
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
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
                float scaledU = frac(input.uv.x * _AcrossScale);
                float across = saturate(abs(scaledU * 2.0 - 1.0));

                half4 finalColor = half4(0,0,0,1);

                int maxLayers = min(_LayerCount, 16);
                for(int i=0;i<maxLayers;i++)
                {
                    float weight = SAMPLE_TEXTURE2D(_MaskAtlas, sampler_LinearClamp, float2(across, (i + 0.5) * _AtlasInvHeight)).r;
                    weight = saturate((weight - _MaskThreshold) / max(1e-5, 1.0 - _MaskThreshold));
                    if(weight < 1e-4) continue;

                    float2 tiling = GetLayerTiling(i);
                    float2 layerUV = input.uv * tiling;
                    half4 layerColor = SampleLayerTexture(i, layerUV);
                    layerColor.rgb *= weight;

                    float opacity = GetLayerOpacity(i) * weight;
                    float mode = GetLayerBlendMode(i);
                    finalColor = BlendLayer(finalColor, layerColor, mode, opacity);
                }

                if(all(finalColor.rgb == 0))
                    clip(-1);

                finalColor.a = saturate(_PreviewAlpha);
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}