// 文件路径: Runtime/Core/PathProfile.cs (专业剖面版)
using System.Collections.Generic;
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

        // --- 【核心重构：引入动态剖面】 ---
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

        // 【废弃】图层列表不再用于定义几何形状，仅用于纹理绘制
        [HideInInspector]
        public List<PathLayer> layers = new List<PathLayer>();

        private void OnEnable()
        {
            // 初始化默认值
            if (crossSection == null || crossSection.keys.Length == 0)
                crossSection = new AnimationCurve(new Keyframe(-1, 0, 0, 0), new Keyframe(1, 0, 0, 0));
            if (falloffShape == null || falloffShape.keys.Length == 0)
                falloffShape = AnimationCurve.EaseInOut(0, 1, 1, 0);
        }
    }
}