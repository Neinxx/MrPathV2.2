Shader "MrPath/PathPreviewSplatMulti"
{
    Properties
    {
        [Header(Render State)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8 // Default to Always (8). Use LEqual (4) for normal depth.
        [Space]
        _PreviewAlpha("Preview Alpha", Range(0, 1)) = 0.5
        
        [Header(Control Textures)]
        // NOTE: These control textures are NOT used by the fixed shader logic, 
        // which now correctly uses the Mask LUT or Vertex Colors. They are kept for property compatibility.
        _Control0("Control 0 (RGBA)", 2D) = "red" {}
        _Control1("Control 1 (RGBA)", 2D) = "black" {}
        _Control2("Control 2 (RGBA)", 2D) = "black" {}
        _Control3("Control 3 (RGBA)", 2D) = "black" {}
        
        [Header(Layer Count)]
        _LayerCount("Layer Count", Int) = 4
        
        [Header(Layer Opacity and Blend Modes)]
        _Layer0_Opacity("Layer 0 Opacity", Range(0, 1)) = 1
        _Layer0_BlendMode("Layer 0 Blend Mode", Float) = 0
        _Layer1_Opacity("Layer 1 Opacity", Range(0, 1)) = 1
        _Layer1_BlendMode("Layer 1 Blend Mode", Float) = 0
        _Layer2_Opacity("Layer 2 Opacity", Range(0, 1)) = 1
        _Layer2_BlendMode("Layer 2 Blend Mode", Float) = 0
        _Layer3_Opacity("Layer 3 Opacity", Range(0, 1)) = 1
        _Layer3_BlendMode("Layer 3 Blend Mode", Float) = 0
        
        // Properties for layers 4-15 are unused by this shader's 4-layer mask logic
        // but are kept to prevent material errors if they were previously set.
        [Header(Layers 4_15 Are Unused)]
        _Layer4_Opacity("Layer 4 Opacity", Range(0, 1)) = 1
        _Layer4_BlendMode("Layer 4 Blend Mode", Float) = 0
        _Layer5_Opacity("Layer 5 Opacity", Range(0, 1)) = 1
        _Layer5_BlendMode("Layer 5 Blend Mode", Float) = 0
        _Layer6_Opacity("Layer 6 Opacity", Range(0, 1)) = 1
        _Layer6_BlendMode("Layer 6 Blend Mode", Float) = 0
        _Layer7_Opacity("Layer 7 Opacity", Range(0, 1)) = 1
        _Layer7_BlendMode("Layer 7 Blend Mode", Float) = 0
        _Layer8_Opacity("Layer 8 Opacity", Range(0, 1)) = 1
        _Layer8_BlendMode("Layer 8 Blend Mode", Float) = 0
        _Layer9_Opacity("Layer 9 Opacity", Range(0, 1)) = 1
        _Layer9_BlendMode("Layer 9 Blend Mode", Float) = 0
        _Layer10_Opacity("Layer 10 Opacity", Range(0, 1)) = 1
        _Layer10_BlendMode("Layer 10 Blend Mode", Float) = 0
        _Layer11_Opacity("Layer 11 Opacity", Range(0, 1)) = 1
        _Layer11_BlendMode("Layer 11 Blend Mode", Float) = 0
        _Layer12_Opacity("Layer 12 Opacity", Range(0, 1)) = 1
        _Layer12_BlendMode("Layer 12 Blend Mode", Float) = 0
        _Layer13_Opacity("Layer 13 Opacity", Range(0, 1)) = 1
        _Layer13_BlendMode("Layer 13 Blend Mode", Float) = 0
        _Layer14_Opacity("Layer 14 Opacity", Range(0, 1)) = 1
        _Layer14_BlendMode("Layer 14 Blend Mode", Float) = 0
        _Layer15_Opacity("Layer 15 Opacity", Range(0, 1)) = 1
        _Layer15_BlendMode("Layer 15 Blend Mode", Float) = 0

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
        _MaskLUT ("Mask LUT (RGBA weights)", 2D) = "white" {}
        _AcrossScale ("Across Scale", Float) = 1
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
            // REMOVED: Unused multi_compile directive
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
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
            
            // Layer textures 0-15 with shared samplers
            TEXTURE2D(_Layer0_Texture); float4 _Layer0_Tiling; half4 _Layer0_Color; float _Layer0_Opacity; float _Layer0_BlendMode;
            TEXTURE2D(_Layer1_Texture); float4 _Layer1_Tiling; half4 _Layer1_Color; float _Layer1_Opacity; float _Layer1_BlendMode;
            TEXTURE2D(_Layer2_Texture); float4 _Layer2_Tiling; half4 _Layer2_Color; float _Layer2_Opacity; float _Layer2_BlendMode;
            TEXTURE2D(_Layer3_Texture); float4 _Layer3_Tiling; half4 _Layer3_Color; float _Layer3_Opacity; float _Layer3_BlendMode;
            TEXTURE2D(_Layer4_Texture); float4 _Layer4_Tiling; half4 _Layer4_Color; float _Layer4_Opacity; float _Layer4_BlendMode;
            TEXTURE2D(_Layer5_Texture); float4 _Layer5_Tiling; half4 _Layer5_Color; float _Layer5_Opacity; float _Layer5_BlendMode;
            TEXTURE2D(_Layer6_Texture); float4 _Layer6_Tiling; half4 _Layer6_Color; float _Layer6_Opacity; float _Layer6_BlendMode;
            TEXTURE2D(_Layer7_Texture); float4 _Layer7_Tiling; half4 _Layer7_Color; float _Layer7_Opacity; float _Layer7_BlendMode;
            TEXTURE2D(_Layer8_Texture); float4 _Layer8_Tiling; half4 _Layer8_Color; float _Layer8_Opacity; float _Layer8_BlendMode;
            TEXTURE2D(_Layer9_Texture); float4 _Layer9_Tiling; half4 _Layer9_Color; float _Layer9_Opacity; float _Layer9_BlendMode;
            TEXTURE2D(_Layer10_Texture); float4 _Layer10_Tiling; half4 _Layer10_Color; float _Layer10_Opacity; float _Layer10_BlendMode;
            TEXTURE2D(_Layer11_Texture); float4 _Layer11_Tiling; half4 _Layer11_Color; float _Layer11_Opacity; float _Layer11_BlendMode;
            TEXTURE2D(_Layer12_Texture); float4 _Layer12_Tiling; half4 _Layer12_Color; float _Layer12_Opacity; float _Layer12_BlendMode;
            TEXTURE2D(_Layer13_Texture); float4 _Layer13_Tiling; half4 _Layer13_Color; float _Layer13_Opacity; float _Layer13_BlendMode;
            TEXTURE2D(_Layer14_Texture); float4 _Layer14_Tiling; half4 _Layer14_Color; float _Layer14_Opacity; float _Layer14_BlendMode;
            TEXTURE2D(_Layer15_Texture); float4 _Layer15_Tiling; half4 _Layer15_Color; float _Layer15_Opacity; float _Layer15_BlendMode;
            
            // LUT and other properties
            TEXTURE2D(_MaskLUT);
            float _PreviewAlpha;
            float _AcrossScale;
            int _LayerCount; 

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // NOTE: These switch-based helper functions are inefficient but necessary
            // due to the verbose property structure. Refactoring would require
            // changing to uniform arrays and a C# controller script.
            half4 SampleLayerTexture(int layerIndex, float2 uv)
            {
                switch(layerIndex)
                {
                    case 0: return SAMPLE_TEXTURE2D(_Layer0_Texture, sampler_LinearRepeat, uv) * _Layer0_Color;
                case 1: return SAMPLE_TEXTURE2D(_Layer1_Texture, sampler_LinearRepeat, uv) * _Layer1_Color;
                case 2: return SAMPLE_TEXTURE2D(_Layer2_Texture, sampler_LinearRepeat, uv) * _Layer2_Color;
                case 3: return SAMPLE_TEXTURE2D(_Layer3_Texture, sampler_LinearRepeat, uv) * _Layer3_Color;
                case 4: return SAMPLE_TEXTURE2D(_Layer4_Texture, sampler_LinearRepeat, uv) * _Layer4_Color;
                case 5: return SAMPLE_TEXTURE2D(_Layer5_Texture, sampler_LinearRepeat, uv) * _Layer5_Color;
                case 6: return SAMPLE_TEXTURE2D(_Layer6_Texture, sampler_LinearRepeat, uv) * _Layer6_Color;
                case 7: return SAMPLE_TEXTURE2D(_Layer7_Texture, sampler_LinearRepeat, uv) * _Layer7_Color;
                case 8: return SAMPLE_TEXTURE2D(_Layer8_Texture, sampler_LinearRepeat, uv) * _Layer8_Color;
                case 9: return SAMPLE_TEXTURE2D(_Layer9_Texture, sampler_LinearRepeat, uv) * _Layer9_Color;
                case 10: return SAMPLE_TEXTURE2D(_Layer10_Texture, sampler_LinearRepeat, uv) * _Layer10_Color;
                case 11: return SAMPLE_TEXTURE2D(_Layer11_Texture, sampler_LinearRepeat, uv) * _Layer11_Color;
                case 12: return SAMPLE_TEXTURE2D(_Layer12_Texture, sampler_LinearRepeat, uv) * _Layer12_Color;
                case 13: return SAMPLE_TEXTURE2D(_Layer13_Texture, sampler_LinearRepeat, uv) * _Layer13_Color;
                case 14: return SAMPLE_TEXTURE2D(_Layer14_Texture, sampler_LinearRepeat, uv) * _Layer14_Color;
                case 15: return SAMPLE_TEXTURE2D(_Layer15_Texture, sampler_LinearRepeat, uv) * _Layer15_Color;
                    default: return half4(1, 1, 1, 1);
                }
            }

            float2 GetLayerTiling(int layerIndex)
            {
                switch(layerIndex)
                {
                    case 0: return max(_Layer0_Tiling.xy, float2(0.0001, 0.0001));
                    case 1: return max(_Layer1_Tiling.xy, float2(0.0001, 0.0001));
                    case 2: return max(_Layer2_Tiling.xy, float2(0.0001, 0.0001));
                    case 3: return max(_Layer3_Tiling.xy, float2(0.0001, 0.0001));
                    case 4: return max(_Layer4_Tiling.xy, float2(0.0001, 0.0001));
                    case 5: return max(_Layer5_Tiling.xy, float2(0.0001, 0.0001));
                    case 6: return max(_Layer6_Tiling.xy, float2(0.0001, 0.0001));
                    case 7: return max(_Layer7_Tiling.xy, float2(0.0001, 0.0001));
                    case 8: return max(_Layer8_Tiling.xy, float2(0.0001, 0.0001));
                    case 9: return max(_Layer9_Tiling.xy, float2(0.0001, 0.0001));
                    case 10: return max(_Layer10_Tiling.xy, float2(0.0001, 0.0001));
                    case 11: return max(_Layer11_Tiling.xy, float2(0.0001, 0.0001));
                    case 12: return max(_Layer12_Tiling.xy, float2(0.0001, 0.0001));
                    case 13: return max(_Layer13_Tiling.xy, float2(0.0001, 0.0001));
                    case 14: return max(_Layer14_Tiling.xy, float2(0.0001, 0.0001));
                    case 15: return max(_Layer15_Tiling.xy, float2(0.0001, 0.0001));
                    default: return float2(1, 1);
                }
            }

            float GetLayerOpacity(int layerIndex)
            {
                switch(layerIndex)
                {
                    case 0: return _Layer0_Opacity;
                    case 1: return _Layer1_Opacity;
                    case 2: return _Layer2_Opacity;
                    case 3: return _Layer3_Opacity;
                    case 4: return _Layer4_Opacity;
                    case 5: return _Layer5_Opacity;
                    case 6: return _Layer6_Opacity;
                    case 7: return _Layer7_Opacity;
                    case 8: return _Layer8_Opacity;
                    case 9: return _Layer9_Opacity;
                    case 10: return _Layer10_Opacity;
                    case 11: return _Layer11_Opacity;
                    case 12: return _Layer12_Opacity;
                    case 13: return _Layer13_Opacity;
                    case 14: return _Layer14_Opacity;
                    case 15: return _Layer15_Opacity;
                    default: return 1;
                }
            }

            float GetLayerBlendMode(int layerIndex)
            {
                switch(layerIndex)
                {
                    case 0: return _Layer0_BlendMode;
                    case 1: return _Layer1_BlendMode;
                    case 2: return _Layer2_BlendMode;
                    case 3: return _Layer3_BlendMode;
                    case 4: return _Layer4_BlendMode;
                    case 5: return _Layer5_BlendMode;
                    case 6: return _Layer6_BlendMode;
                    case 7: return _Layer7_BlendMode;
                    case 8: return _Layer8_BlendMode;
                    case 9: return _Layer9_BlendMode;
                    case 10: return _Layer10_BlendMode;
                    case 11: return _Layer11_BlendMode;
                    case 12: return _Layer12_BlendMode;
                    case 13: return _Layer13_BlendMode;
                    case 14: return _Layer14_BlendMode;
                    case 15: return _Layer15_BlendMode;
                    default: return 0;
                }
            }

            half4 BlendLayer(half4 base, half4 layer, float blendMode, float opacity)
            {
                // BlendMode: Normal=0, Multiply=1, Add=2, Overlay=3, Screen=4, Lerp=5, Additive=6
                half3 blendedColor;
                switch ((int)blendMode)
                {
                    case 1: // Multiply
                        blendedColor = base.rgb * layer.rgb;
                        break;
                    case 2: // Add (Same as Additive)
                    case 6: // Additive
                        blendedColor = base.rgb + layer.rgb;
                        break;
                    case 3: // Overlay
                        blendedColor = lerp(
                            2.0 * base.rgb * layer.rgb, 
                            1.0 - 2.0 * (1.0 - base.rgb) * (1.0 - layer.rgb), 
                            step(0.5, base.rgb)
                        );
                        break;
                    case 4: // Screen
                        blendedColor = 1.0 - (1.0 - base.rgb) * (1.0 - layer.rgb);
                        break;
                    default: // Normal / Lerp
                        blendedColor = layer.rgb;
                        break;
                }
                
                return half4(lerp(base.rgb, blendedColor, opacity), base.a);
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Sample the stripe LUT based on the cross-path UV coordinate
                // This provides up to 4 weights for blending layers.
                float scaledU = frac(input.uv.x * _AcrossScale);
                float across = saturate(abs(scaledU * 2.0 - 1.0));
                half4 mask = SAMPLE_TEXTURE2D(_MaskLUT, sampler_LinearClamp, float2(across, 0.5));
                
                // If the LUT is not providing weights (e.g., it's black), 
                // fall back to using the mesh's vertex colors as weights.
                half weightSum = dot(mask, half4(1,1,1,1));
                if (weightSum < 1e-4)
                {
                    mask = input.color;
                    weightSum = dot(mask, half4(1,1,1,1));
                }

                // If there are no weights at all, discard the pixel completely.
                if (weightSum < 1e-4)
                {
                    clip(-1);
                }

                half4 finalColor = half4(0, 0, 0, 1); // Start with an opaque black base
                
                // Create an array for easy access to mask weights.
                half weights[4] = { mask.r, mask.g, mask.b, mask.a };
                
                // This shader uses the LUT/VertexColor mask, which provides 4 weights.
                // Therefore, we only process up to the first 4 layers.
                int maxLayers = min(_LayerCount, 4);
                for (int i = 0; i < maxLayers; i++)
                {
                    // BUG FIX: The weight now correctly comes from the 'mask' calculated above,
                    // not from an unrelated splatmap function.
                    float weight = weights[i];

                    if (weight > 1e-4)
                    {
                        float2 layerTiling = GetLayerTiling(i);
                        float2 layerUV = input.uv * layerTiling;
                        half4 layerColor = SampleLayerTexture(i, layerUV);
                        
                        // 修正：黑色遮罩剔除地形layer，遮罩值越小剔除越多
                        // 当遮罩为黑色(0)时完全剔除，遮罩为白色(1)时完全保留
                        layerColor.rgb *= weight; // 直接使用遮罩值作为剔除系数
                        
                        float layerOpacity = GetLayerOpacity(i);
                        float blendMode = GetLayerBlendMode(i);
                        
                        // The opacity passed to the blend function combines the layer's
                        // base opacity with its weight for the current pixel.
                        float blendOpacity = layerOpacity * weight;
                        
                        // Sequentially blend this layer on top of the previous result.
                        finalColor = BlendLayer(finalColor, layerColor, blendMode, blendOpacity);
                    }
                }

                finalColor.a = saturate(_PreviewAlpha);
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}