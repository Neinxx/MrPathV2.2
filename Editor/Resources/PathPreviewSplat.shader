Shader "MrPath/PathPreviewSplat"
{
    Properties
    {
        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8 // Default to Always (8). Use LEqual (4) for normal depth.
        [Space]
        _PreviewAlpha("Preview Alpha", Range(0, 1)) = 0.5
        
        [Header(Layers)]
        _Layer0_Texture("Layer 0 (R)", 2D) = "white" {}
        _Layer0_Tiling("Layer 0 Tiling", Vector) = (1, 1, 0, 0)
        _Layer0_Color("Layer 0 Color", Color) = (1, 1, 1, 1)
        _Layer1_Texture("Layer 1 (G)", 2D) = "white" {}
        _Layer1_Tiling("Layer 1 Tiling", Vector) = (1, 1, 0, 0)
        _Layer1_Color("Layer 1 Color", Color) = (1, 1, 1, 1)
        _Layer2_Texture("Layer 2 (B)", 2D) = "white" {}
        _Layer2_Tiling("Layer 2 Tiling", Vector) = (1, 1, 0, 0)
        _Layer2_Color("Layer 2 Color", Color) = (1, 1, 1, 1)
        _Layer3_Texture("Layer 3 (A)", 2D) = "white" {}
        _Layer3_Tiling("Layer 3 Tiling", Vector) = (1, 1, 0, 0)
        _Layer3_Color("Layer 3 Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay+100" 
        }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest] // Use the value from our property

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_Layer0_Texture); SAMPLER(sampler_Layer0_Texture); float4 _Layer0_Texture_ST; float2 _Layer0_Tiling; half4 _Layer0_Color;
            TEXTURE2D(_Layer1_Texture); SAMPLER(sampler_Layer1_Texture); float4 _Layer1_Texture_ST; float2 _Layer1_Tiling; half4 _Layer1_Color;
            TEXTURE2D(_Layer2_Texture); SAMPLER(sampler_Layer2_Texture); float4 _Layer2_Texture_ST; float2 _Layer2_Tiling; half4 _Layer2_Color;
            TEXTURE2D(_Layer3_Texture); SAMPLER(sampler_Layer3_Texture); float4 _Layer3_Texture_ST; float2 _Layer3_Tiling; half4 _Layer3_Color;
            float _PreviewAlpha;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 t0 = max(_Layer0_Tiling.xy, float2(0.0001, 0.0001));
                float2 t1 = max(_Layer1_Tiling.xy, float2(0.0001, 0.0001));
                float2 t2 = max(_Layer2_Tiling.xy, float2(0.0001, 0.0001));
                float2 t3 = max(_Layer3_Tiling.xy, float2(0.0001, 0.0001));

                float2 uv0 = input.uv * t0;
                float2 uv1 = input.uv * t1;
                float2 uv2 = input.uv * t2;
                float2 uv3 = input.uv * t3;

                half4 col0 = SAMPLE_TEXTURE2D(_Layer0_Texture, sampler_Layer0_Texture, uv0) * _Layer0_Color;
                half4 col1 = SAMPLE_TEXTURE2D(_Layer1_Texture, sampler_Layer1_Texture, uv1) * _Layer1_Color;
                half4 col2 = SAMPLE_TEXTURE2D(_Layer2_Texture, sampler_Layer2_Texture, uv2) * _Layer2_Color;
                half4 col3 = SAMPLE_TEXTURE2D(_Layer3_Texture, sampler_Layer3_Texture, uv3) * _Layer3_Color;

                half4 finalColor = col0 * input.color.r + col1 * input.color.g + col2 * input.color.b + col3 * input.color.a;
                finalColor.a = saturate(_PreviewAlpha);

                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}