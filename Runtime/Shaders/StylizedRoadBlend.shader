Shader "MrPathV2/StylizedRoadBlend"
{
    Properties
    {
        _PrevResultTex ("Previous Result", 2D) = "black" {}
        _LayerTex     ("Layer Texture", 2D)  = "white" {}
        _MaskAtlas    ("Mask Atlas",   2D)  = "white" {}
        _AtlasInvHeight ("Atlas Inv Height", Float) = 1
        _MaskThreshold  ("Mask Threshold", Range(0,1)) = 0
        _MaskRowCenter  ("Mask Row Center", Float) = 0.5
        _LayerTiling    ("Layer Tiling", Vector) = (1, 1, 0, 0)
        _LayerTint      ("Layer Tint", Color) = (1, 1, 1, 1)
        _LayerOpacity   ("Layer Opacity", Range(0,1)) = 1
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
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            sampler2D _PrevResultTex;
            sampler2D _LayerTex;
            sampler2D _MaskAtlas;

            float _AtlasInvHeight;
            float _MaskThreshold;
            float _MaskRowCenter;   // (rowIndex+0.5)*_AtlasInvHeight

            float4 _LayerTiling;
            float4 _LayerTint;
            float  _LayerOpacity;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                return OUT;
            }

            float4 frag (Varyings IN) : SV_Target
            {
                // Sample mask grayscale (R channel) along the mask atlas row
                float across = saturate(abs(frac(IN.uv.x) * 2.0 - 1.0));
                float mask   = tex2D(_MaskAtlas, float2(across, _MaskRowCenter)).r;

                // Apply threshold (soft step to allow feather)
                mask = saturate((mask - _MaskThreshold) / max(1e-5, 1.0 - _MaskThreshold));

                // Early out when mask is effectively zero to avoid unnecessary texture fetches/blending
                if (mask <= 1e-3)
                {
                    return tex2D(_PrevResultTex, IN.uv);
                }

                float4 prevResult = tex2D(_PrevResultTex, IN.uv);
                float4 layerColor = tex2D(_LayerTex, IN.uv * _LayerTiling.xy + _LayerTiling.zw) * _LayerTint;

                // Use mask grayscale as transparency only; keep original color intact
                float opacity = _LayerOpacity * mask;

                // Standard alpha blend (lerp) between previous result and layer color
                return lerp(prevResult, layerColor, opacity);
            }
            ENDHLSL
        }
    }
}