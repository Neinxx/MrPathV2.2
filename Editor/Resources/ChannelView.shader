Shader "Hidden/MrPath/ChannelView"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Channel("Channel", Float) = 0 // 0:RGB,1:R,2:G,3:B,4:A
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend One Zero
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Channel;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (_Channel < 0.5) return c; // RGB
                fixed v = 0;
                if (_Channel < 1.5) v = c.r; // R
                else if (_Channel < 2.5) v = c.g; // G
                else if (_Channel < 3.5) v = c.b; // B
                else v = c.a; // A
                return fixed4(v, v, v, 1);
            }
            ENDHLSL
        }
    }
}