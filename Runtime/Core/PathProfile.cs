using System;
using MrPathV2.Runtime.Components;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    [CreateAssetMenu(fileName = "NewPathProfile", menuName = "MrPath/Path Profile")]
    public class PathProfile : ScriptableObject
    {

        private const int MinCrossSectionSegments = 3;
        private const int MaxCrossSectionSegments = 64;
        private const int MinLongitudinalSegments = 1;
        private const int MaxLongitudinalSegments = 300;
        [Header("核心设置")]
        public CurveType curveType = CurveType.CatmullRom;
        [Tooltip("预览网格在长度上的分段数")]
        [Range(MinLongitudinalSegments, MaxLongitudinalSegments)] public int longitudinalSegments = 32;
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
        [Range(MinCrossSectionSegments, MaxCrossSectionSegments)] public int crossSectionSegments = 16;

        [Header("渲染预览")]
        [Tooltip("是否在场景中显示预览网格")]
        public bool showPreviewMesh = true;
        [Tooltip("预览是否进行深度测试：开启时预览遵循场景深度(LEqual)；关闭时始终在最上层(Always)。")]
        public bool enableDepthTest;
        [Tooltip("不透明预览：开启后预览为完全不透明，不与地形颜色混合。")]
        public bool opaquePreview;

        [Header("风格化边缘噪声")]
        [Tooltip("是否启用预览网格两侧边缘的噪声扰动（仅影响预览网格外形）")]
        public bool enableEdgeNoise = true;
        [Tooltip("边缘噪声的最大横向扰动幅度（米）")]
        [Range(0f, 2f)] public float edgeNoiseAmplitude = 0.15f;
        [Tooltip("沿路径的噪声周期（每单位进度的周期数，数值越大波纹越密")]
        [Range(0.1f, 64f)] public float edgeNoisePeriod = 8f;
        [Tooltip("噪声抖动强度（0..1）")]
        [Range(0f, 1f)] public float edgeNoiseJitter = 0.2f;
        [Tooltip("边缘噪声的种子值")]
        public float edgeNoiseSeed = 123f;

        [Header("遮罩采样")]
        [Tooltip("沿路径方向的遮罩采样密度（每多少米采一行）。数值越小，沿途越平滑但Atlas越高。")]
        [Min(0.1f)] public float maskMetersPerSample = 0.75f;

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
            // 注意：不在OnDisable中清理实例化Recipe，因为这可能在编辑器中频繁触发
            // 实例化Recipe的清理由OnValidate和ClearInstancedRecipe方法负责
        }

        private void OnValidate()
        {
            // 保证生成参数与曲线端点处于安全范围
            crossSectionSegments = Mathf.Clamp(crossSectionSegments, MinCrossSectionSegments, MaxCrossSectionSegments);
            longitudinalSegments = Mathf.Clamp(longitudinalSegments, MinLongitudinalSegments, MaxLongitudinalSegments);
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

            // 直接使用roadRecipe
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
            // 优化：不再强制修改端点数值，避免编辑器抖动与自动加点。
            // 仅保证有且仅有一个端点位于指定时间；如果不存在，则将最靠近的端点移动到该时间。
            if (curve == null) return;

            var keys = curve.keys;
            if (keys.Length == 0)
            {
                // 空曲线的容错：只添加一个关键帧以避免 NRE；数值沿用传入值
                curve.AddKey(new Keyframe(time, value));
                return;
            }

            // 如果已存在精确的端点，直接清理重复端点并返回（避免重复操作）
            var existingIdx = Array.FindIndex(keys, k => Mathf.Approximately(k.time, time));
            if (existingIdx >= 0)
            {
                RemoveDuplicateKeys(curve, time, existingIdx);
                return;
            }

            // 根据时间判断是起始点还是结束点
            MoveNearestKeyToPoint(curve, time);
        }

        private static void RemoveDuplicateKeys(AnimationCurve curve, float time, int excludeIndex)
        {
            // 清理除指定索引外的其它重复端点
            for (var i = curve.keys.Length - 1; i >= 0; i--)
            {
                if (i == excludeIndex) continue;
                if (Mathf.Approximately(curve.keys[i].time, time))
                {
                    curve.RemoveKey(i);
                }
            }
        }

        private static void MoveNearestKeyToPoint(AnimationCurve curve, float targetTime)
        {
            var keys = curve.keys;
            const int firstIdx = 0;
            var lastIdx = keys.Length - 1;

            // 判断目标时间更靠近起点还是终点
            var targetIsStart = targetTime <= (keys[firstIdx].time + keys[lastIdx].time) * 0.5f;

            if (targetIsStart)
            {
                // 移动第一个关键帧到目标时间
                var key = curve.keys[firstIdx];
                key.time = targetTime;
                curve.MoveKey(firstIdx, key);

                // 清理可能产生的重复关键帧（保留新的首端点）
                RemoveDuplicateKeysAfterFirst(curve, targetTime);
            }
            else
            {
                // 移动最后一个关键帧到目标时间
                var key = curve.keys[lastIdx];
                key.time = targetTime;
                curve.MoveKey(lastIdx, key);

                // 清理可能产生的重复关键帧（保留新的末端点）
                RemoveDuplicateKeysBeforeLast(curve, targetTime);
            }
        }

        private static void RemoveDuplicateKeysAfterFirst(AnimationCurve curve, float time)
        {
            for (var i = curve.keys.Length - 1; i >= 1; i--)
            {
                if (Mathf.Approximately(curve.keys[i].time, time))
                    curve.RemoveKey(i);
            }
        }

        private static void RemoveDuplicateKeysBeforeLast(AnimationCurve curve, float time)
        {
            var lastIndex = curve.keys.Length - 1;
            for (var i = lastIndex - 1; i >= 0; i--)
            {
                if (Mathf.Approximately(curve.keys[i].time, time))
                    curve.RemoveKey(i);
            }
        }
    }
}
