using UnityEngine;

namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    /// 统一管理预览相关着色器属性的契约（名称与ID）。
    /// </summary>
    public static class PreviewShaderContracts
    {
        public static class Properties
        {
            public static readonly int LayerCount = Shader.PropertyToID("_LayerCount");
            public static readonly int PreviewAlpha = Shader.PropertyToID("_PreviewAlpha");
            public static readonly int OpaquePreview = Shader.PropertyToID("_OpaquePreview");
            public static readonly int MaskAtlas = Shader.PropertyToID("_MaskAtlas");
            public static readonly int AtlasInvHeight = Shader.PropertyToID("_AtlasInvHeight");
            public static readonly int LayerTex = Shader.PropertyToID("_LayerTex");
            public static readonly int LayerTiling = Shader.PropertyToID("_LayerTiling");
            public static readonly int LayerTint = Shader.PropertyToID("_LayerTint");
            public static readonly int LayerOpacity = Shader.PropertyToID("_LayerOpacity");
            public static readonly int BlendMode = Shader.PropertyToID("_BlendMode");
            public static readonly int LayerTilingsArr = Shader.PropertyToID("_LayerTilings");
            public static readonly int LayerOpacitiesArr = Shader.PropertyToID("_LayerOpacities");
            public static readonly int LayerBlendModesArr = Shader.PropertyToID("_LayerBlendModes");
            public static readonly int PathSamples = Shader.PropertyToID("_PathSamples");
            public static readonly int LayerIndex = Shader.PropertyToID("_LayerIndex");
            public static readonly int MaskStrength = Shader.PropertyToID("_MaskStrength");
            public static readonly int ZTest = Shader.PropertyToID("_ZTest");
            public static readonly int MaskThreshold = Shader.PropertyToID("_MaskThreshold");
            public static readonly int MeshRepeatAcross = Shader.PropertyToID("_MeshRepeatAcross");
            public static readonly int MeshRepeatAlong = Shader.PropertyToID("_MeshRepeatAlong");
            public static readonly int AcrossScale = Shader.PropertyToID("_AcrossScale");
            public static readonly int LayerTextures = Shader.PropertyToID("_LayerTextures");
            public static readonly int UseLayerTexArray = Shader.PropertyToID("_UseLayerTexArray");
            public static readonly int SplatWeights = Shader.PropertyToID("_SplatWeights");
            public static readonly int UseSplatWeights = Shader.PropertyToID("_UseSplatWeights");
            public static readonly int TerrainPosition = Shader.PropertyToID("_TerrainPosition");
            public static readonly int TerrainSize = Shader.PropertyToID("_TerrainSize");
            public static readonly int AlphamapResolution = Shader.PropertyToID("_AlphamapResolution");
            public static readonly int LayerSplatIndicesArr = Shader.PropertyToID("_LayerSplatIndices");
            // Stylized 叠加使用的上一帧结果纹理
            public static readonly int PrevResultTex = Shader.PropertyToID("_PrevResultTex");
        }
    }
}