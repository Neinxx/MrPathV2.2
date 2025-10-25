using System;
using MrPathV2.Runtime.Components;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    [CreateAssetMenu(fileName = "NewPathProfile", menuName = "MrPath/Path Profile")]
    public class PathProfile : ScriptableObject
    {

        private const int MIN_SEGMENTS = 3;
        private const int MAX_SEGMENTS = 64;
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
        [Tooltip("是否在场景中显示预览网格")]
        public bool showPreviewMesh = true;
        [Tooltip("预览是否进行深度测试：开启时预览遵循场景深度(LEqual)；关闭时始终在最上层(Always)。")]
        public bool enableDepthTest;
        [Tooltip("不透明预览：开启后预览为完全不透明，不与地形颜色混合。")]
        public bool opaquePreview;

        [Header("Mask Settings")]
        [Tooltip("Tiling for the mask, controlling how the mask texture repeats.")]
        public Vector2 maskTiling = new Vector2(1f, 1f);

        [Tooltip("拖入 StylizedRoadRecipe 以定义道路的纹理分布与风格")]
        [RequiredField(ErrorMessage = "请分配一个StylizedRoadRecipe以调配道路风格")]
        public StylizedRoadRecipe roadRecipe;

        private StylizedRoadRecipe _subscribedRecipe;

        private void OnEnable()
        {
            SubscribeToRecipe();
        }

        private void OnDisable()
        {
            UnsubscribeFromRecipe();
        }

        private void OnValidate()
        {
            // 保证生成参数与曲线端点处于安全范围
            crossSectionSegments = Mathf.Clamp(crossSectionSegments, MIN_SEGMENTS, MAX_SEGMENTS);
            roadWidth = Mathf.Max(0.01f, roadWidth);
            falloffWidth = Mathf.Max(0f, falloffWidth);

            EnsureKey(ref crossSection, -1f, 0f);
            EnsureKey(ref crossSection, 1f, 0f);
            EnsureKey(ref falloffShape, 0f, 1f);
            EnsureKey(ref falloffShape, 1f, 0f);

            // 重新订阅Recipe（可能在Inspector中更换了Recipe引用）
            SubscribeToRecipe();

            // 数据层仅负责发出立即事件；合并刷新交给上层
            ProfileModified?.Invoke();
        }

        public event Action ProfileModified;

        private void SubscribeToRecipe()
        {
            UnsubscribeFromRecipe(); // 确保不会重复订阅

            if (!roadRecipe) return;
            _subscribedRecipe = roadRecipe;
            _subscribedRecipe.RecipeChanged += OnRecipeChanged;
        }

        private void UnsubscribeFromRecipe()
        {
            if (!_subscribedRecipe) return;
            _subscribedRecipe.RecipeChanged -= OnRecipeChanged;
            _subscribedRecipe = null;
        }

        private void OnRecipeChanged()
        {
            // 数据变化：立即通知消费者层（由 EditorRefreshManager 合并刷新）
            ProfileModified?.Invoke();
        }

        private static void EnsureKey(ref AnimationCurve curve, float time, float value)
        {
            var idx = Array.FindIndex(curve.keys, k => Mathf.Approximately(k.time, time));
            if (idx >= 0)
            {
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
