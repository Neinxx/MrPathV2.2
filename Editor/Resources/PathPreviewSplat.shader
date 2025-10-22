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
        
        // Mask Atlas storing per-layer weights in vertical slices (RGBA irrelevant)
        _MaskAtlas ("Mask Atlas", 2D) = "white" {}
        _AtlasInvHeight ("Atlas Inv Height", Float) = 1
        _MaskThreshold("Mask Threshold", Range(0,1)) = 0
        // Across Scale: maps repeating UV.x back into 0..1 range
        _AcrossScale ("Across Scale", Float) = 1
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
             // Mask Atlas
             TEXTURE2D(_MaskAtlas); SAMPLER(sampler_MaskAtlas);
             float _AtlasInvHeight;
             float _MaskThreshold;
             float _PreviewAlpha;
             float _AcrossScale;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color; // 仍保留，避免顶点声明变更
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample per-layer weights from mask atlas slices
                float scaledU = frac(input.uv.x * _AcrossScale);
                float across = saturate(abs(scaledU * 2.0 - 1.0));

                half4 mask;
                mask.r = SAMPLE_TEXTURE2D(_MaskAtlas, sampler_MaskAtlas, float2(across, (0.5) * _AtlasInvHeight)).r;
                 mask.g = SAMPLE_TEXTURE2D(_MaskAtlas, sampler_MaskAtlas, float2(across, (1.5) * _AtlasInvHeight)).r;
                 mask.b = SAMPLE_TEXTURE2D(_MaskAtlas, sampler_MaskAtlas, float2(across, (2.5) * _AtlasInvHeight)).r;
                 mask.a = SAMPLE_TEXTURE2D(_MaskAtlas, sampler_MaskAtlas, float2(across, (3.5) * _AtlasInvHeight)).r;

                // Apply threshold
                mask = saturate((mask - _MaskThreshold) / max(1e-5, 1.0 - _MaskThreshold));

                half weightSum = mask.r + mask.g + mask.b + mask.a;

                // 若权重总和接近 0，直接丢弃像素，避免显示为黑色
                if (weightSum < 1e-4)
                {
                    clip(-1); // 立即剔除
                }

                float2 t0 = max(_Layer0_Tiling.xy, float2(0.0001, 0.0001));
                float2 t1 = max(_Layer1_Tiling.xy, float2(0.0001, 0.0001));
                float2 t2 = max(_Layer2_Tiling.xy, float2(0.0001, 0.0001));
                float2 t3 = max(_Layer3_Tiling.xy, float2(0.0001, 0.0001));

                // Mesh 已经在生成时应用了 tiling，因此此处不再二次缩放，避免与地形 UV 不一致
                float2 uv0 = input.uv;
                float2 uv1 = input.uv;
                float2 uv2 = input.uv;
                float2 uv3 = input.uv;

                half4 col0 = SAMPLE_TEXTURE2D(_Layer0_Texture, sampler_Layer0_Texture, uv0) * _Layer0_Color;
                half4 col1 = SAMPLE_TEXTURE2D(_Layer1_Texture, sampler_Layer1_Texture, uv1) * _Layer1_Color;
                half4 col2 = SAMPLE_TEXTURE2D(_Layer2_Texture, sampler_Layer2_Texture, uv2) * _Layer2_Color;
                half4 col3 = SAMPLE_TEXTURE2D(_Layer3_Texture, sampler_Layer3_Texture, uv3) * _Layer3_Color;

                // 修正：黑色遮罩剔除地形layer，遮罩值越小剔除越多
                // 当遮罩为黑色(0)时完全剔除，遮罩为白色(1)时完全保留
                col0.rgb *= mask.r; // 直接使用遮罩值作为剔除系数
                col1.rgb *= mask.g;
                col2.rgb *= mask.b;
                col3.rgb *= mask.a;

                // 使用 LUT 权重混合，而非顶点色
                half4 finalColor = col0 * mask.r + col1 * mask.g + col2 * mask.b + col3 * mask.a;
                finalColor.a = saturate(_PreviewAlpha);

                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}