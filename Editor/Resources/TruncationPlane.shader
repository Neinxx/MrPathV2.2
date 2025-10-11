Shader "Hidden/MrPath/TruncationPlane"
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
        Tags { "RenderType" = "Opaque" "PreviewType" = "Plane" }
        LOD 100

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex0, _MaskTex1, _MaskTex2, _MaskTex3;
            float _Opacity0, _Opacity1, _Opacity2, _Opacity3;
            float _BlendMode0, _BlendMode1, _BlendMode2, _BlendMode3;
            float _LayerCount;
            float _Normalize;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; // 0..1 across rect
                return o;
            }

            float Blend(float baseValue, float layerValue, int blendModeOrdinal)
            {
                // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
                if (blendModeOrdinal == 1) return baseValue * layerValue; // Multiply
                if (blendModeOrdinal == 2) return saturate(baseValue + layerValue); // Add
                if (blendModeOrdinal == 3) // Overlay
                {
                    return baseValue < 0.5 ? (2.0 * baseValue * layerValue) : (1.0 - 2.0 * (1.0 - baseValue) * (1.0 - layerValue));
                }
                if (blendModeOrdinal == 4) return 1.0 - (1.0 - baseValue) * (1.0 - layerValue); // Screen
                if (blendModeOrdinal == 5) return lerp(baseValue, layerValue, saturate(layerValue)); // Lerp
                if (blendModeOrdinal == 6) return saturate(baseValue + layerValue); // Additive
                return layerValue; // Normal
            }

            float SampleMask(sampler2D tex, float u, float opacity)
            {
                float v = tex2D(tex, float2(u, 0.5)).r;
                return saturate(v * opacity);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float u = saturate(i.uv.x);
                int count = (int)_LayerCount;
                float r=0,g=0,b=0,a=0;

                if (count > 0)
                {
                    float v0 = SampleMask(_MaskTex0, u, _Opacity0);
                    r = Blend(r, v0, (int)_BlendMode0);
                }
                if (count > 1)
                {
                    float v1 = SampleMask(_MaskTex1, u, _Opacity1);
                    g = Blend(g, v1, (int)_BlendMode1);
                }
                if (count > 2)
                {
                    float v2 = SampleMask(_MaskTex2, u, _Opacity2);
                    b = Blend(b, v2, (int)_BlendMode2);
                }
                if (count > 3)
                {
                    float v3 = SampleMask(_MaskTex3, u, _Opacity3);
                    a = Blend(a, v3, (int)_BlendMode3);
                }

                if (_Normalize > 0.5)
                {
                    float sum = r + g + b + a;
                    if (sum > 1e-6)
                    {
                        float inv = 1.0 / sum;
                        r *= inv; g *= inv; b *= inv; a *= inv;
                    }
                }

                return fixed4(r, g, b, a);
            }
            ENDCG
        }
    }
}