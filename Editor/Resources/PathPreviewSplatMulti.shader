Shader "MrPath/PathPreviewSplatMulti"
{
    Properties
    {
        _MaskAtlas ("Mask Atlas", 2D) = "white" {}
        _AtlasInvHeight ("Atlas Inv Height", Float) = 1.0
        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8 // Default to Always (8). Use LEqual (4) for normal depth.
        [Space]
        _PreviewAlpha("Preview Alpha", Range(0, 1)) = 0.6
        _MaskStrength("Mask Strength", Range(0, 4)) = 1
        [Toggle] _OpaquePreview ("Opaque Preview", Float) = 0

        [Header(Control Textures)]
        // NOTE : These control textures are NOT used by the fixed shader logic,
        // which now correctly uses the Mask LUT or Vertex Colors. They are kept for property compatibility.
        _Control0("Control 0 (RGBA)", 2D) = "red" {}
        _Control1("Control 1 (RGBA)", 2D) = "black" {}
        _Control2("Control 2 (RGBA)", 2D) = "black" {}
        _Control3("Control 3 (RGBA)", 2D) = "black" {}

        [Header(Layer Count)]
        _LayerCount("Layer Count", Int) = 4


        [Header(Layers 0_3)]
        _Layer0_Texture("Layer 0", 2D) = "white" {}
        _Layer0_Tiling("Layer 0 Tiling", Vector) = (1, 1, 0, 0)
        _Layer0_Color("Layer 0 Color", Color) = (1, 1, 1, 1)
        _Layer1_Texture("Layer 1", 2D) = "white" {}
        _Layer1_Tiling("Layer 1 Tiling", Vector) = (1, 1, 0, 0)
        _Layer1_Color("Layer 1 Color", Color) = (1, 1, 1, 1)
        _Layer2_Texture("Layer 2", 2D) = "white" {}
        _Layer2_Tiling("Layer 2 Tiling", Vector) = (1, 1, 0, 0)
        _Layer2_Color("Layer 2 Color", Color) = (1, 1, 1, 1)
        _Layer3_Texture("Layer 3", 2D) = "white" {}
        _Layer3_Tiling("Layer 3 Tiling", Vector) = (1, 1, 0, 0)
        _Layer3_Color("Layer 3 Color", Color) = (1, 1, 1, 1)

        [Header(Layers 4_15 Are Unused)]
        _Layer4_Texture("Layer 4", 2D) = "white" {}
        _Layer4_Tiling("Layer 4 Tiling", Vector) = (1, 1, 0, 0)
        _Layer4_Color("Layer 4 Color", Color) = (1, 1, 1, 1)
        _Layer5_Texture("Layer 5", 2D) = "white" {}
        _Layer5_Tiling("Layer 5 Tiling", Vector) = (1, 1, 0, 0)
        _Layer5_Color("Layer 5 Color", Color) = (1, 1, 1, 1)
        _Layer6_Texture("Layer 6", 2D) = "white" {}
        _Layer6_Tiling("Layer 6 Tiling", Vector) = (1, 1, 0, 0)
        _Layer6_Color("Layer 6 Color", Color) = (1, 1, 1, 1)
        _Layer7_Texture("Layer 7", 2D) = "white" {}
        _Layer7_Tiling("Layer 7 Tiling", Vector) = (1, 1, 0, 0)
        _Layer7_Color("Layer 7 Color", Color) = (1, 1, 1, 1)
        _Layer8_Texture("Layer 8", 2D) = "white" {}
        _Layer8_Tiling("Layer 8 Tiling", Vector) = (1, 1, 0, 0)
        _Layer8_Color("Layer 8 Color", Color) = (1, 1, 1, 1)
        _Layer9_Texture("Layer 9", 2D) = "white" {}
        _Layer9_Tiling("Layer 9 Tiling", Vector) = (1, 1, 0, 0)
        _Layer9_Color("Layer 9 Color", Color) = (1, 1, 1, 1)
        _Layer10_Texture("Layer 10", 2D) = "white" {}
        _Layer10_Tiling("Layer 10 Tiling", Vector) = (1, 1, 0, 0)
        _Layer10_Color("Layer 10 Color", Color) = (1, 1, 1, 1)
        _Layer11_Texture("Layer 11", 2D) = "white" {}
        _Layer11_Tiling("Layer 11 Tiling", Vector) = (1, 1, 0, 0)
        _Layer11_Color("Layer 11 Color", Color) = (1, 1, 1, 1)
        _Layer12_Texture("Layer 12", 2D) = "white" {}
        _Layer12_Tiling("Layer 12 Tiling", Vector) = (1, 1, 0, 0)
        _Layer12_Color("Layer 12 Color", Color) = (1, 1, 1, 1)
        _Layer13_Texture("Layer 13", 2D) = "white" {}
        _Layer13_Tiling("Layer 13 Tiling", Vector) = (1, 1, 0, 0)
        _Layer13_Color("Layer 13 Color", Color) = (1, 1, 1, 1)
        _Layer14_Texture("Layer 14", 2D) = "white" {}
        _Layer14_Tiling("Layer 14 Tiling", Vector) = (1, 1, 0, 0)
        _Layer14_Color("Layer 14 Color", Color) = (1, 1, 1, 1)
        _Layer15_Texture("Layer 15", 2D) = "white" {}
        _Layer15_Tiling("Layer 15 Tiling", Vector) = (1, 1, 0, 0)
        _Layer15_Color("Layer 15 Color", Color) = (1, 1, 1, 1)

        [Header(Path Masking)]
        _AcrossScale ("Across Scale", Float) = 1
        _MaskThreshold ("Mask Threshold", Range(0, 1)) = 0
        _PathSamples("Path Samples", Float) = 64
        _MeshRepeatAcross ("Mesh Repeat Across", Float) = 1
        _MeshRepeatAlong ("Mesh Repeat Along", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay+100"
        }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // REMOVED : Unused multi_compile directive
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BlendLayer.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float2 worldUV : TEXCOORD1;
                half4 color : COLOR;
                float4 positionHCS : SV_POSITION;
            };

            // Shared samplers to reduce register usage
            SAMPLER(sampler_LinearRepeat);
            SAMPLER(sampler_LinearClamp);

            // Control textures (Unused in fixed logic, but declared to match properties)
            TEXTURE2D(_Control0);
            TEXTURE2D(_Control1);
            TEXTURE2D(_Control2);
            TEXTURE2D(_Control3);

            // Layer textures 0 - 15 with shared samplers
            TEXTURE2D(_Layer0_Texture); half4 _Layer0_Color;
            TEXTURE2D(_Layer1_Texture); half4 _Layer1_Color;
            TEXTURE2D(_Layer2_Texture); half4 _Layer2_Color;
            TEXTURE2D(_Layer3_Texture); half4 _Layer3_Color;
            TEXTURE2D(_Layer4_Texture); half4 _Layer4_Color;
            TEXTURE2D(_Layer5_Texture); half4 _Layer5_Color;
            TEXTURE2D(_Layer6_Texture); half4 _Layer6_Color;
            TEXTURE2D(_Layer7_Texture); half4 _Layer7_Color;
            TEXTURE2D(_Layer8_Texture); half4 _Layer8_Color;
            TEXTURE2D(_Layer9_Texture); half4 _Layer9_Color;
            TEXTURE2D(_Layer10_Texture); half4 _Layer10_Color;
            TEXTURE2D(_Layer11_Texture); half4 _Layer11_Color;
            TEXTURE2D(_Layer12_Texture); half4 _Layer12_Color;
            TEXTURE2D(_Layer13_Texture); half4 _Layer13_Color;
            TEXTURE2D(_Layer14_Texture); half4 _Layer14_Color;
            TEXTURE2D(_Layer15_Texture); half4 _Layer15_Color;

            // Mask atlas and other properties
            TEXTURE2D(_MaskAtlas);
            // 新增：GPU 计算的地形权重数组
            TEXTURE2D_ARRAY(_SplatWeights);
            float _AtlasInvHeight;
            float _MaskThreshold;
            float _PreviewAlpha;
            float _OpaquePreview;
            float _MaskStrength;
            float _AcrossScale;
            float _MeshRepeatAcross;
            float _MeshRepeatAlong;
            int _LayerCount;
            float _PathSamples;

            // New unified layer parameter arrays (populated from PreviewMaterialManager)
            CBUFFER_START(UnityPerMaterial)
            float4 _LayerTilings[16];
            float _LayerOpacities[16];
            float _LayerBlendModes[16];
            // 新增：GPU 权重采样所需的地形与映射参数
            float2 _TerrainPosition;
            float2 _TerrainSize;
            float2 _AlphamapResolution;
            float _LayerSplatIndices[16];
            float _UseSplatWeights;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(worldPos);
                output.uv = input.uv;
                output.worldUV = worldPos.xz;
                output.color = input.color;
                return output;
            }

            // NOTE : These switch - based helper functions are inefficient but necessary
            // due to the verbose property structure. Refactoring would require
            // changing to uniform arrays and a C# controller script.
            half4 SampleLayerTexture(int layerIndex, float2 uv)
            {

                switch(layerIndex)
                {
                    case 0 : return SAMPLE_TEXTURE2D_LOD(_Layer0_Texture, sampler_LinearRepeat, uv, 0) * _Layer0_Color;
                    case 1 : return SAMPLE_TEXTURE2D_LOD(_Layer1_Texture, sampler_LinearRepeat, uv, 0) * _Layer1_Color;
                    case 2 : return SAMPLE_TEXTURE2D_LOD(_Layer2_Texture, sampler_LinearRepeat, uv, 0) * _Layer2_Color;
                    case 3 : return SAMPLE_TEXTURE2D_LOD(_Layer3_Texture, sampler_LinearRepeat, uv, 0) * _Layer3_Color;
                    case 4 : return SAMPLE_TEXTURE2D_LOD(_Layer4_Texture, sampler_LinearRepeat, uv, 0) * _Layer4_Color;
                    case 5 : return SAMPLE_TEXTURE2D_LOD(_Layer5_Texture, sampler_LinearRepeat, uv, 0) * _Layer5_Color;
                    case 6 : return SAMPLE_TEXTURE2D_LOD(_Layer6_Texture, sampler_LinearRepeat, uv, 0) * _Layer6_Color;
                    case 7 : return SAMPLE_TEXTURE2D_LOD(_Layer7_Texture, sampler_LinearRepeat, uv, 0) * _Layer7_Color;
                    case 8 : return SAMPLE_TEXTURE2D_LOD(_Layer8_Texture, sampler_LinearRepeat, uv, 0) * _Layer8_Color;
                    case 9 : return SAMPLE_TEXTURE2D_LOD(_Layer9_Texture, sampler_LinearRepeat, uv, 0) * _Layer9_Color;
                    case 10 : return SAMPLE_TEXTURE2D_LOD(_Layer10_Texture, sampler_LinearRepeat, uv, 0) * _Layer10_Color;
                    case 11 : return SAMPLE_TEXTURE2D_LOD(_Layer11_Texture, sampler_LinearRepeat, uv, 0) * _Layer11_Color;
                    case 12 : return SAMPLE_TEXTURE2D_LOD(_Layer12_Texture, sampler_LinearRepeat, uv, 0) * _Layer12_Color;
                    case 13 : return SAMPLE_TEXTURE2D_LOD(_Layer13_Texture, sampler_LinearRepeat, uv, 0) * _Layer13_Color;
                    case 14 : return SAMPLE_TEXTURE2D_LOD(_Layer14_Texture, sampler_LinearRepeat, uv, 0) * _Layer14_Color;
                    case 15 : return SAMPLE_TEXTURE2D_LOD(_Layer15_Texture, sampler_LinearRepeat, uv, 0) * _Layer15_Color;
                    default : return half4(1, 1, 1, 1);
                }
            }

            float2 GetLayerTiling(int layerIndex)
            {
                return max(_LayerTilings[layerIndex].xy, float2(0.0001, 0.0001));
            }

            float GetLayerOpacity(int layerIndex)
            {
                return _LayerOpacities[layerIndex];
            }

            float GetLayerBlendMode(int layerIndex)
            {
                return _LayerBlendModes[layerIndex];
            }
            // Legacy BlendLayer replaced by shared ApplyBlend in BlendLayer.hlsl
            #define BlendLayer(baseColor, layerColor, mode, opacity) ApplyBlend(baseColor, layerColor, mode, opacity)

            // 根据可用性从 GPU _SplatWeights 或 2D MaskAtlas 采样权重
            float SampleWeightForLayer(float2 worldUV, float across, float progress, int layerIndex)
            {
                // 优先使用 GPU 权重
                if (_UseSplatWeights > 0.5)
                {
                    int splatIndex = (int)round(_LayerSplatIndices[layerIndex]);
                    if (splatIndex >= 0)
                    {
                        // (我们顺便也修复一下上次的整数除法和梯度警告)
                        uint slice = (uint)splatIndex / 4u;
                        uint channel = (uint)splatIndex % 4u;
                        float2 terrainUV = saturate((worldUV - _TerrainPosition) / _TerrainSize);

                        // 使用 _LOD 避免梯度警告
                        half4 rgba = SAMPLE_TEXTURE2D_ARRAY_LOD(_SplatWeights, sampler_LinearClamp, terrainUV, slice, 0);

                        if (channel == 0) return rgba.r;
                        else if (channel == 1) return rgba.g;
                        else if (channel == 2) return rgba.b;
                        else return rgba.a;
                    }
                    else
                    {
                        // !! 这是修复 uninitialized variable 错误的关键 !!
                        return 0.0;
                    }
                }

                // 回退到 2D MaskAtlas
                return SampleMaskAtlas2D(
                _MaskAtlas,
                sampler_LinearClamp,
                across,
                progress,
                layerIndex,
                _PathSamples,
                _AtlasInvHeight,
                _MaskThreshold) * _MaskStrength;
            }
            half4 frag(Varyings input) : SV_Target
            {
                // 拉伸到道路宽度：Across 不再重复，直接使用 0..1
                float acrossPos01 = saturate(input.uv.x);
                float across = saturate(abs(acrossPos01 * 2.0 - 1.0) * _AcrossScale);
                // 沿路径方向仍允许重复控制
                float pathProgress = saturate(input.uv.y / max(_MeshRepeatAlong, 0.0001));

                // 初始完全透明
                half4 finalColor = half4(0, 0, 0, 0);

                int maxLayers = min(_LayerCount, 16);
                for (int i = 0; i < maxLayers; i ++)
                {
                    // 从 GPU 权重或 MaskAtlas 采样
                    float weight = SampleWeightForLayer(input.worldUV, across, pathProgress, i);
                    if (weight < 0.0004)
                    continue;

                    float2 layerTiling = GetLayerTiling(i);
                    float2 layerUV = input.worldUV * layerTiling;
                    half4 layerColor = SampleLayerTexture(i, layerUV);

                    // 仅透明度受 mask 影响，颜色保持原值
                    layerColor.a *= weight;

                    float layerOpacity = GetLayerOpacity(i);
                    float blendMode = GetLayerBlendMode(i);
                    float blendOpacity = layerOpacity;

                    finalColor = BlendLayer(finalColor, layerColor, blendMode, blendOpacity);
                }

                // 使用颜色强度驱动透明度；全黑像素输出全透明
                float alphaFactor = saturate(max(max(finalColor.r, finalColor.g), finalColor.b));
                finalColor.a = (_OpaquePreview > 0.5) ? 1.0 : alphaFactor * saturate(_PreviewAlpha);
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}