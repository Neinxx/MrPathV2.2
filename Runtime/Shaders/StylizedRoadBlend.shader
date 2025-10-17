Shader "MrPathV2/StylizedRoadBlend"
{
    Properties
    {
        _PrevResultTex("Previous Result", 2D) = "black" {}
        _LayerTex("Layer Texture", 2D) = "white" {}
        _MaskLUT("Mask LUT", 2D) = "white" {}
        _LayerTiling("Layer Tiling", Vector) = (1, 1, 0, 0)
        _LayerTint("Layer Tint", Color) = (1, 1, 1, 1)
        _LayerOpacity("Layer Opacity", Range(0, 1)) = 1
        _BlendMode("Blend Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            sampler2D _PrevResultTex;
            sampler2D _LayerTex;
            sampler2D _MaskLUT;
            float4 _LayerTiling;
            float4 _LayerTint;
            float _LayerOpacity;
            int _BlendMode;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float mask = tex2D(_MaskLUT, float2(IN.uv.x, 0.5)).r;
                float4 prevResult = tex2D(_PrevResultTex, IN.uv);
                float4 layerColor = tex2D(_LayerTex, IN.uv * _LayerTiling.xy + _LayerTiling.zw) * _LayerTint;
                
                // 修正：黑色遮罩剔除地形layer，遮罩值越小剔除越多
                // 当遮罩为黑色(0)时完全剔除，遮罩为白色(1)时完全保留
                layerColor.rgb *= mask; // 直接使用遮罩值作为剔除系数
                
                layerColor.a = _LayerOpacity * mask;

                // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
                switch (_BlendMode)
                {
                    case 1: // Multiply
                        return lerp(prevResult, prevResult * layerColor, layerColor.a);
                    case 2: // Add
                        return prevResult + layerColor * layerColor.a;
                    case 3: // Overlay
                        float4 overlay = float4(
                            prevResult.r < 0.5 ? (2 * prevResult.r * layerColor.r) : (1 - 2 * (1 - prevResult.r) * (1 - layerColor.r)),
                            prevResult.g < 0.5 ? (2 * prevResult.g * layerColor.g) : (1 - 2 * (1 - prevResult.g) * (1 - layerColor.g)),
                            prevResult.b < 0.5 ? (2 * prevResult.b * layerColor.b) : (1 - 2 * (1 - prevResult.b) * (1 - layerColor.b)),
                            prevResult.a
                        );
                        return lerp(prevResult, overlay, layerColor.a);
                    case 4: // Screen
                        float4 screen = 1.0f - (1.0f - prevResult) * (1.0f - layerColor);
                        return lerp(prevResult, screen, layerColor.a);
                    case 5: // Lerp
                        return lerp(prevResult, layerColor, layerColor.a);
                    case 6: // Additive
                         return prevResult + layerColor * layerColor.a;
                    default: // Normal
                        return lerp(prevResult, layerColor, layerColor.a);
                }
            }
            ENDHLSL
        }
    }
}