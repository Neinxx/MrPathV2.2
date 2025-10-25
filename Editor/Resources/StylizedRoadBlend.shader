// shaderlab
Shader "MrPathV2/StylizedRoadBlend"
{
    Properties
    {
        _PrevResultTex ("Previous Result", 2D) = "black" {}
        _LayerTex ("Layer Texture", 2D) = "white" {}
        _MaskAtlas ("Mask Atlas", 2D) = "white" {}
        _AtlasInvHeight ("Atlas Inv Height", Float) = 1
        _MaskThreshold ("Mask Threshold", Range(0, 1)) = 0
        _MaskRowCenter ("Mask Row Center", Float) = 0.5
        // 新增属性：Layer 索引和 PathSamples 用于 2D MaskAtlas 采样
        _LayerIndex ("Layer Index", Float) = 0
        _PathSamples ("Path Samples", Float) = 64
        _AcrossScale ("Across Scale", Float) = 1
        _MeshRepeatAcross ("Mesh Repeat Across", Float) = 1
        _MeshRepeatAlong ("Mesh Repeat Along", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
        [Toggle] _OpaquePreview ("Opaque Preview", Float) = 0

        _LayerTiling ("Layer Tiling", Vector) = (1, 1, 0, 0)
        _LayerTint ("Layer Tint", Color) = (1, 1, 1, 1)
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            ZTest [_ZTest]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BlendLayer.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0; // original mesh UV
                float2 worldUV : TEXCOORD1; // world - space UV (XZ) for texture sampling
            };

            sampler2D    _PrevResultTex;
            sampler2D    _LayerTex;
            Texture2D    _MaskAtlas;
            SamplerState sampler_LinearClamp;

            float _AtlasInvHeight;
            float _MaskThreshold;
            float _MaskRowCenter; // legacy single - row center (仍保留作兼容，但 2D 版本不使用)
            float _LayerIndex; // 当前图层索引 (0 - based)
            float _PathSamples; // 每层纵向采样行数
            float _AcrossScale;
            float _MeshRepeatAcross;
            float _MeshRepeatAlong;
            float _OpaquePreview;

            float4 _LayerTiling;
            float4 _LayerTint;
            float  _LayerOpacity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3   worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                OUT.worldUV = worldPos.xz;

                // 新增：计算道路本地UV，基于道路宽度和长度归一化
                float2 localUV;
                localUV.x = IN.uv.x / max(_MeshRepeatAcross, 1e-5); // 道路宽度归一化
                localUV.y = IN.uv.y / max(_MeshRepeatAlong, 1e-5); // 道路长度归一化
                OUT.uv = localUV;

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // 道路本地UV直接使用归一化后的值
                float across01 = saturate(IN.uv.x);
                // 统一为左->右 0..1 的 across，去除中心镜像
                float across = saturate(across01 * _AcrossScale);
                float pathProgress = saturate(IN.uv.y);

                // 使用新的 2D 采样函数
                float mask = SampleMaskAtlas2D(
                    _MaskAtlas,
                    sampler_LinearClamp,
                    across,
                    pathProgress,
                    _LayerIndex,
                    _PathSamples,
                    _AtlasInvHeight,
                    _MaskThreshold);

                // 移除硬裁剪早退，改为软透明混合，依赖 mask 透明度进行平滑过渡
                float4 prevResult = tex2D(_PrevResultTex, IN.uv);

                // 当 mask 非零时逐渐混合；mask 已通过阈值归一化，值接近 0 时基本透明
                float2 layerUV = IN.worldUV * _LayerTiling.xy + _LayerTiling.zw;
                float4 layerColor = tex2D(_LayerTex, layerUV) * _LayerTint;

                // 使用纹理自身的 Alpha 通道作为透明度乘子
                float srcAlpha = layerColor.a * _LayerOpacity * mask;

                // Alpha 混合：src over dst
                float4 outColor;
                outColor.rgb = lerp(prevResult.rgb, layerColor.rgb, srcAlpha);
                outColor.a = (_OpaquePreview > 0.5) ? 1.0 : saturate(srcAlpha + prevResult.a * (1.0 - srcAlpha));
                return outColor;
            }
            ENDHLSL
        }
    }
}