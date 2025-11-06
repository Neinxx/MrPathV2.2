Shader "MrPathV2/RoadMaskRender"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "Queue" = "Geometry"
        }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Mesh 顶点输入 + 区域映射参数
            float2 _TerrainPosition;
            float2 _TerrainSize;
            int2   _AlphamapResolution;
            int2   _CoverageMin;
            int2   _CoverageMax;

            struct VSIn {
                float3 pos : POSITION;
            };

            struct VSOut {
                float4 pos : SV_Position;
            };

            VSOut Vert(VSIn v)
            {
                VSOut o;

                // 将世界坐标转为地形UV（0..1）
                float2 uvTerrain = (v.pos.xz - _TerrainPosition) / _TerrainSize;

                // 转为全地形像素坐标，再映射到本次覆盖区域（CoverageMin..Max）
                float2 pix = uvTerrain * float2(_AlphamapResolution);
                float2 covMin = float2(_CoverageMin);
                float2 covMax = float2(_CoverageMax);
                float2 regionSize = max(float2(1.0, 1.0), (covMax - covMin) + float2(1.0, 1.0));
                float2 uvRegion = (pix - covMin) / regionSize;

                // 映射到裁剪空间
                float2 clip = uvRegion * 2.0 - 1.0;
                o.pos = float4(clip, 0, 1);
                return o;
            }

            float4 Frag(VSOut i) : SV_Target
            {
                // 白色写入，作为遮罩
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}