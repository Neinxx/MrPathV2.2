
using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(fileName = "NewPathProfile", menuName = "MrPath/Path Profile")]
    public class PathProfile : ScriptableObject
    {
        [Header("核心设置")]
        public CurveType curveType = CurveType.Bezier;
        [Range(0.1f, 10f)] public float generationPrecision = 1f;
        [Tooltip("道路的总宽度")]
        public float roadWidth = 5f;

        [Header("道路剖面与边缘")]
        [Tooltip("定义道路横截面的形状。X轴[-1, 1]代表从左到右，Y轴代表相对高度。")]
        public AnimationCurve crossSection = new AnimationCurve(new Keyframe(-1, 0), new Keyframe(1, 0));

        [Tooltip("道路边缘与地形融合的过渡带宽度。")]
        public float falloffWidth = 2f;

        [Tooltip("定义边缘过渡的形状。X轴[0, 1]代表从道路边缘到过渡带末端，Y轴[0, 1]代表与地形的混合权重。")]
        public AnimationCurve falloffShape = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Header("地形吸附")]
        public bool snapToTerrain = true;
        public float heightOffset = 0.1f;
        [Range(0, 100)] public int smoothness = 10;

        [Header("网格生成")]
        public bool forceHorizontal = true;
        [Tooltip("预览网格在宽度上的分段数")]
        [Range(2, 64)] public int crossSectionSegments = 16;

        [Header("道路纹理配方")]
        [Tooltip("拖入 StylizedRoadRecipe 以定义道路的纹理分布与风格")]
        public StylizedRoadRecipe roadRecipe;
    }
}