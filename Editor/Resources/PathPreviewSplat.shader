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
        
        // 新增：条带 LUT 纹理（RGBA 通道分别存储每层权重, 横向分辨率固定 256）
        _MaskLUT ("Mask LUT (RGBA weights)", 2D) = "white" {}
        // 新增：横向 UV 缩放，用于将重复的 UV.x 映射回 0..1 区间
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
            // 新增：条带 LUT
            TEXTURE2D(_MaskLUT); SAMPLER(sampler_MaskLUT);
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
                // 采样条带 LUT：中心0 边缘1 (基于横向UV)
                // 首先根据 _AcrossScale 将任意平铺后的 UV.x 映射回 0..1 区间
                float scaledU = frac(input.uv.x * _AcrossScale);
                float across = saturate(abs(scaledU * 2.0 - 1.0));
                 half4 mask = SAMPLE_TEXTURE2D(_MaskLUT, sampler_MaskLUT, float2(across, 0.5));
                     // 如果未提供 LUT（全 0 或全 1 情况下不可靠），回退到顶点色权重
                     half weightSum = mask.r + mask.g + mask.b + mask.a;
                     if (weightSum < 1e-4)
                     {
                         mask = input.color;
                         weightSum = mask.r + mask.g + mask.b + mask.a;
                     }

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