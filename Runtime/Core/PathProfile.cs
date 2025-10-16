
using System;
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
        [Range(3, 64)] public int crossSectionSegments = 16;

        [Header("渲染预览")]
        [Tooltip("是否在场景中显示预览网格")] public bool showPreviewMesh = true;
        [Tooltip("拖入 StylizedRoadRecipe 以定义道路的纹理分布与风格")]
        public StylizedRoadRecipe roadRecipe;

        private const int MIN_SEGMENTS = 3;
        private const int MAX_SEGMENTS = 64;

        private void OnValidate()
        {
            // Keep generated parameters within safe range
            crossSectionSegments = Mathf.Clamp(crossSectionSegments, MIN_SEGMENTS, MAX_SEGMENTS);
            roadWidth = Mathf.Max(0.01f, roadWidth);
            falloffWidth = Mathf.Max(0f, falloffWidth);

            // Ensure cross section curve has endpoints at -1 and 1
            EnsureKey(ref crossSection, -1f, 0f);
            EnsureKey(ref crossSection, 1f, 0f);

            // Ensure falloff curve starts at 0->1 and ends at 1->0
            EnsureKey(ref falloffShape, 0f, 1f);
            EnsureKey(ref falloffShape, 1f, 0f);
        }

        private static void EnsureKey(ref AnimationCurve curve, float time, float value)
        {
            int idx = Array.FindIndex(curve.keys, k => Mathf.Approximately(k.time, time));
            if (idx >= 0)
            {
                // Update value if needed
                if (!Mathf.Approximately(curve.keys[idx].value, value))
                {
                    var k = curve.keys[idx];
                    k.value = value;
                    curve.MoveKey(idx, k);
                }
            }
            else
            {
                curve.AddKey(new Keyframe(time, value));
            }
        }
    }
}