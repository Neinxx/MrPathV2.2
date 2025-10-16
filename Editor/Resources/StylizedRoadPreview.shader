Shader "MrPathV2/StylizedRoadPreview"
{
    Properties
    {
        _PreviousResultTex ("Previous Result", 2D) = "black" {}
        _LayerTex ("Layer Texture", 2D) = "white" {}
        _MaskLUT ("Mask LUT", 2D) = "white" {}
        _LayerTiling ("Layer Tiling", Vector) = (1,1,0,0)
        _LayerTint ("Layer Tint", Color) = (1,1,1,1)
        _LayerOpacity ("Layer Opacity", Float) = 1
        _BlendMode ("Blend Mode", Float) = 0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
        }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 引入URP核心库
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 声明纹理和采样器
            TEXTURE2D(_PreviousResultTex);
            SAMPLER(sampler_PreviousResultTex);
            
            TEXTURE2D(_LayerTex);
            SAMPLER(sampler_LayerTex);
            
            TEXTURE2D(_MaskLUT);
            SAMPLER(sampler_MaskLUT);

            // 平铺参数
            CBUFFER_START(UnityPerMaterial)
                float4 _LayerTiling;
                float4 _LayerTint; // added
                float4 _PreviousResultTex_ST;
                float4 _LayerTex_ST;
                float4 _MaskLUT_ST;
                float _LayerOpacity;
                float _BlendMode;
            CBUFFER_END

            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            // 片元输入结构
            struct Varyings
            {
                float2 uv           : TEXCOORD0;
                float4 positionHCS  : SV_POSITION;
            };

            // 顶点着色器
            Varyings vert(Attributes input)
            {
                Varyings output;
                // 转换到齐次裁剪空间
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                // 传递UV
                output.uv = input.uv;
                return output;
            }

            // 片元着色器
            half4 frag(Varyings input) : SV_Target
            {
                // 采样掩码 LUT：沿着 u 轴取样以保持与场景预览一致（0..1 映射到道路横向宽度）
                float maskInfluence = SAMPLE_TEXTURE2D(_MaskLUT, sampler_MaskLUT, float2(input.uv.x, 0.5)).r;

                // 采样此前累积结果
                half4 prevColor = SAMPLE_TEXTURE2D(_PreviousResultTex, sampler_PreviousResultTex, input.uv);

                // 计算平铺 UV 并采样当前图层纹理
                float2 layerUV = input.uv * _LayerTiling.xy + _LayerTiling.zw;
                half4 layerColor = SAMPLE_TEXTURE2D(_LayerTex, sampler_LayerTex, layerUV) * _LayerTint;

                // 应用图层透明度，并与掩码影响相乘，确保与 TerrainJobs 权重一致
                layerColor *= (_LayerOpacity * maskInfluence);

                // 根据 BlendMode 进行混合
                half4 result;
                if (_BlendMode < 0.5)       // Normal
                {
                    result = lerp(prevColor, layerColor, layerColor.a); // 使用 layerColor alpha 作为插值
                }
                else if (_BlendMode < 1.5)  // Add
                {
                    result = prevColor + layerColor;
                }
                else                         // Multiply
                {
                    result = lerp(prevColor, prevColor * layerColor, layerColor.a);
                }

                return saturate(result);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}