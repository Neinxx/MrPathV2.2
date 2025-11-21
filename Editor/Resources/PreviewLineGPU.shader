Shader "MrPath/PreviewLineGPU"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+200" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
            };

            struct SegmentData
            {
                float3 start;
                float3 end;
                float4 color;
                float  thickness;
                float  dashSize;
                uint   flags;
            };

            StructuredBuffer<SegmentData> _Segments;

            Varyings vert(Attributes input)
            {
                SegmentData seg = _Segments[input.instanceID];
                float3 s = seg.start;
                float3 e = seg.end;
                float3 dir = normalize(e - s);
                float3 right = float3(-dir.z, 0.0, dir.x);
                float t = saturate(input.positionOS.x);
                float w = input.positionOS.y;
                float worldThickness = seg.thickness * 0.02;
                float3 worldPos = lerp(s, e, t) + right * w * worldThickness;
                Varyings o;
                o.positionHCS = TransformWorldToHClip(worldPos);
                o.color = seg.color;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.color);
            }
            ENDHLSL
        }
    }
}

