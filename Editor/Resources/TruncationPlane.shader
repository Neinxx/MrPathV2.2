Shader "MrPath/TruncationPlaneURP"
{
    Properties
    {
        _MainTex("MainTex (ignored)", 2D) = "white" {}
        _MaskTex0("MaskTex0", 2D) = "white" {}
        _MaskTex1("MaskTex1", 2D) = "white" {}
        _MaskTex2("MaskTex2", 2D) = "white" {}
        _MaskTex3("MaskTex3", 2D) = "white" {}

        _Opacity0("Opacity0", Float) = 1
        _Opacity1("Opacity1", Float) = 1
        _Opacity2("Opacity2", Float) = 1
        _Opacity3("Opacity3", Float) = 1

        _BlendMode0("BlendMode0", Float) = 0
        _BlendMode1("BlendMode1", Float) = 0
        _BlendMode2("BlendMode2", Float) = 0
        _BlendMode3("BlendMode3", Float) = 0

        _LayerCount("LayerCount", Float) = 0
        _Normalize("Normalize RGBA", Float) = 1
        _RedChannel("Red Channel", Float) = 1
        _GreenChannel("Green Channel", Float) = 1
        _BlueChannel("Blue Channel", Float) = 1
        _AlphaChannel("Alpha Channel", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane"
        }
        LOD 100

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 引入URP核心库
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 统一权重混合库（与 C# / Compute 保持一致）
            #include "BlendingLibrary.hlsl"

            // 纹理和采样器声明（URP方式）
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_MaskTex0);
            SAMPLER(sampler_MaskTex0);
            TEXTURE2D(_MaskTex1);
            SAMPLER(sampler_MaskTex1);
            TEXTURE2D(_MaskTex2);
            SAMPLER(sampler_MaskTex2);
            TEXTURE2D(_MaskTex3);
            SAMPLER(sampler_MaskTex3);

            // 材质属性
            CBUFFER_START(UnityPerMaterial)
                float _Opacity0;
                float _Opacity1;
                float _Opacity2;
                float _Opacity3;
                float _BlendMode0;
                float _BlendMode1;
                float _BlendMode2;
                float _BlendMode3;
                float _LayerCount;
                float _Normalize;
                float _RedChannel;
                float _GreenChannel;
                float _BlueChannel;
                float _AlphaChannel;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 将对象空间位置转换到齐次裁剪空间
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; // 0..1 across rect
                return output;
            }

            float SampleMask(TEXTURE2D(tex), SAMPLER(samplerTex), float u, float opacity)
            {
                float4 texSample = SAMPLE_TEXTURE2D(tex, samplerTex, float2(u, 0.5));
                float  maskValue = texSample.r;

                // 修正：黑色遮罩剔除地形layer，遮罩值越小剔除越多
                // 当遮罩为黑色(0)时完全剔除，遮罩为白色(1)时完全保留
                // 直接使用遮罩值，不需要额外的剔除计算

                return saturate(maskValue * opacity);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float u = saturate(input.uv.x);
                int   count = (int)_LayerCount;
                float r = 0, g = 0, b = 0, a = 0;

                if(count > 0)
                {
                    float v0 = SampleMask(_MaskTex0, sampler_MaskTex0, u, _Opacity0);
                    r = BlendWeight(r, v0, (int)_BlendMode0);
                    r *= _RedChannel;
                }
                if(count > 1)
                {
                    float v1 = SampleMask(_MaskTex1, sampler_MaskTex1, u, _Opacity1);
                    g = BlendWeight(g, v1, (int)_BlendMode1);
                    g *= _GreenChannel;
                }
                if(count > 2)
                {
                    float v2 = SampleMask(_MaskTex2, sampler_MaskTex2, u, _Opacity2);
                    b = BlendWeight(b, v2, (int)_BlendMode2);
                    b *= _BlueChannel;
                }
                if(count > 3)
                {
                    float v3 = SampleMask(_MaskTex3, sampler_MaskTex3, u, _Opacity3);
                    a = BlendWeight(a, v3, (int)_BlendMode3);
                    a *= _AlphaChannel;
                }

                if(_Normalize > 0.5)
                {
                    float4 weights = float4(r, g, b, a);
                    weights = NormalizeWeightsKeep(weights);
                    r = weights.r;
                    g = weights.g;
                    b = weights.b;
                    a = weights.a;
                }

                return half4(r, g, b, a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}