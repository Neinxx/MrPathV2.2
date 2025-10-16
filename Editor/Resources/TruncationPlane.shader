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
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane" }
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
            float _Opacity0, _Opacity1, _Opacity2, _Opacity3;
            float _BlendMode0, _BlendMode1, _BlendMode2, _BlendMode3;
            float _LayerCount;
            float _Normalize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                // 将对象空间位置转换到齐次裁剪空间
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; // 0..1 across rect
                return output;
            }

            float Blend(float baseValue, float layerValue, int blendModeOrdinal)
            {
                // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
                switch(blendModeOrdinal)
                {
                    case 1: return baseValue * layerValue; // Multiply
                    case 2: return saturate(baseValue + layerValue); // Add
                    case 3: // Overlay
                        return baseValue < 0.5 ? (2.0 * baseValue * layerValue) : 
                                               (1.0 - 2.0 * (1.0 - baseValue) * (1.0 - layerValue));
                    case 4: return 1.0 - (1.0 - baseValue) * (1.0 - layerValue); // Screen
                    case 5: return lerp(baseValue, layerValue, saturate(layerValue)); // Lerp
                    case 6: return saturate(baseValue + layerValue); // Additive
                    default: return layerValue; // Normal
                }
            }

            float SampleMask(TEXTURE2D(tex), SAMPLER(samplerTex), float u, float opacity)
            {
                float v = SAMPLE_TEXTURE2D(tex, samplerTex, float2(u, 0.5)).r;
                return saturate(v * opacity);
            }

            half4 frag (Varyings input) : SV_Target
            {
                float u = saturate(input.uv.x);
                int count = (int)_LayerCount;
                float r = 0, g = 0, b = 0, a = 0;

                if (count > 0)
                {
                    float v0 = SampleMask(_MaskTex0, sampler_MaskTex0, u, _Opacity0);
                    r = Blend(r, v0, (int)_BlendMode0);
                }
                if (count > 1)
                {
                    float v1 = SampleMask(_MaskTex1, sampler_MaskTex1, u, _Opacity1);
                    g = Blend(g, v1, (int)_BlendMode1);
                }
                if (count > 2)
                {
                    float v2 = SampleMask(_MaskTex2, sampler_MaskTex2, u, _Opacity2);
                    b = Blend(b, v2, (int)_BlendMode2);
                }
                if (count > 3)
                {
                    float v3 = SampleMask(_MaskTex3, sampler_MaskTex3, u, _Opacity3);
                    a = Blend(a, v3, (int)_BlendMode3);
                }

                if (_Normalize > 0.5)
                {
                    float sum = r + g + b + a;
                    if (sum > 1e-6)
                    {
                        float inv = 1.0 / sum;
                        r *= inv; 
                        g *= inv; 
                        b *= inv; 
                        a *= inv;
                    }
                }

                return half4(r, g, b, a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
