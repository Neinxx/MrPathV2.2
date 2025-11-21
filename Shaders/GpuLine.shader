Shader "Hidden/MrPath/GpuLine"
{
    Properties
    {

    }
    SubShader
    {
        Tags { "RenderType" = "Overlay" "Queue" = "Overlay+1000" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            struct SegmentData
            {
                float3 start;
                float3 end;
                float4 color;
                float thickness;
                float dashSize;
                uint flags;                // 改为uint类型，适合位运算
            };

            StructuredBuffer<SegmentData> _Segments;

            float4x4 unity_MatrixVP;
            float4 _ScreenParams;            // x=width, y=height
            float _AAWidthPx;            // 边缘抗锯齿像素宽度
            float _CapAAWidthPx;            // 端点融合像素宽度
            float _SeamScale;               // 融合宽度缩放
            int _CapType;                   // 端帽类型：0=None，1=Linear

            struct VSIn
            {
                float2 uv : TEXCOORD0;                //  (x: 0..1 along length, y: -0.5..0.5 across thickness)
                uint instanceID : SV_InstanceID;
            };

            struct VSOut
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR0;
                float dashSize : TEXCOORD1;
                uint flags : TEXCOORD2;                // 改为uint类型
                float lenPx : TEXCOORD3;                // 屏幕空间像素长度
            };

            VSOut Vert(VSIn v)
            {
                VSOut o;
                SegmentData seg = _Segments[v.instanceID];
                float3 a = seg.start;
                float3 b = seg.end;
                // 屏幕空间厚度：基于 NDC 偏移实现像素线宽
                float4 clipA = mul(unity_MatrixVP, float4(a, 1.0));
                float4 clipB = mul(unity_MatrixVP, float4(b, 1.0));
                float2 ndcA = clipA.xy / max(clipA.w, 1e-5);
                float2 ndcB = clipB.xy / max(clipB.w, 1e-5);
                float2 dirNdc = normalize(ndcB - ndcA);
                float2 perpNdc = float2(-dirNdc.y, dirNdc.x);

                // 沿长度的中心点（用于正确的 clip.w）
                float3 dirW = normalize(b - a);
                float lenW = max(length(b - a), 1e-5);
                float3 worldCenter = a + dirW * (v.uv.x * lenW);
                float4 clipCenter = mul(unity_MatrixVP, float4(worldCenter, 1.0));

                // 像素 -> NDC 转换；将横向偏移到 clip 空间
                float2 pxToNdc = float2(2.0 / max(_ScreenParams.x, 1.0), 2.0 / max(_ScreenParams.y, 1.0));
                float2 ndcOffset = perpNdc * (seg.thickness * v.uv.y) * pxToNdc * 2.0;
                // v.uv.y ∈ [-0.5,0.5]
                float4 clipPos = clipCenter;
                clipPos.xy += ndcOffset * clipCenter.w;

                // 屏幕空间像素长度（用于虚线与端点融合）
                float2 ndcDelta = ndcB - ndcA;
                float2 ndcToPx = float2(0.5 * _ScreenParams.x, 0.5 * _ScreenParams.y);
                o.lenPx = length(ndcDelta * ndcToPx);

                o.pos = clipPos;
                o.uv = v.uv;
                o.color = seg.color;
                o.dashSize = seg.dashSize;
                o.flags = seg.flags;
                return o;
            }

            float4 Frag(VSOut i) : SV_Target
            {
                // Dashing: clip fragments periodically along length
                if ((i.flags & 1) != 0)
                {
                    float d = fmod(i.uv.x * max(i.lenPx, 1e-3), max(i.dashSize, 1e-3));
                    // Visible half segment, simple dash pattern                    if (d > i.dashSize * 0.5)                discard
                }
                // 屏幕空间边缘AA（像素宽度）
                float edgeAlpha = saturate(((0.5 - abs(i.uv.y)) * i.lenPx) / max(_AAWidthPx, 1e-3));

                // 端点融合（未连接时对端部进行平滑）
                bool startJoined = ((i.flags & 4u) != 0u);
                bool endJoined = ((i.flags & 8u) != 0u);
                float capWidth = max(_CapAAWidthPx * max(_SeamScale, 1e-3), 1e-3);
                float startRaw = saturate((i.uv.x * i.lenPx) / capWidth);
                float endRaw = saturate(((1.0 - i.uv.x) * i.lenPx) / capWidth);
                if (_CapType == 0) { startRaw = 1.0; endRaw = 1.0; } // None
                // Linear 情况使用 startRaw/endRaw 原值
                float startAlpha = startJoined ? 1.0 : startRaw;
                float endAlpha = endJoined ? 1.0 : endRaw;

                float alpha = edgeAlpha * startAlpha * endAlpha * i.color.a;
                return float4(i.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
