using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 美术定义的风格化道路配方：可自由组合项目中的 TerrainLayer，并通过 BlendMask 控制横向分布。

    /// </summary>
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        [Header("混合图层 (Passes)")]
        [Tooltip("此配方通过层叠混合来定义道路的最终材质。每个图层由一个地形层(颜色)和一个遮罩(形状)组成，它们自下而上进行混合。\n\n支持无限层数：系统会自动处理多Control贴图分配和内存优化。")] 
        public List<BlendLayer> blendLayers = new List<BlendLayer>();

        /// <summary>
        /// 当点击 "+" 按钮时，Odin 会调用这个函数。
        /// 这允许我们为新图层设置合理的默认值。
        /// </summary>
        private void AddNewLayer()
        {
            blendLayers.Add(new BlendLayer
            {
                name = $"Layer {blendLayers.Count}",
                enabled = true,
                opacity = 0.85f,
                blendMode = BlendMode.Normal
            });
        }

        [Tooltip("道路的总宽度（米）。")]
        public float width = 5f;

        [Tooltip("道路边缘与地形混合的过渡距离（米）。")]
        public float falloff = 1.5f;

        [Tooltip("配方整体透明度 (0-1)，用于统一控制所有图层的不透明度。")]
        [Range(0f,1f)]
        public float masterOpacity = 1f;

        // 版本号：每次数据发生变化时自增，用于外部检测变更并作增量刷新
        [SerializeField, HideInInspector]
        private int _version = 0;
        public int Version => _version;

#if UNITY_EDITOR
        /// <summary>
        /// 当 Recipe 内容在编辑器中发生修改时触发。运行时自动剔除。
        /// 使用 <see cref="UnityEditor.EditorApplication.update"/> 订阅方需在编辑器模式下监听。
        /// </summary>
        public static event System.Action<StylizedRoadRecipe> OnRecipeChanged;
#endif

        private void OnValidate()
        {
            // Unity 会在 Inspector 变更或加载时调用此函数。
            // 我们在编辑器模式下递增版本号并广播变更事件。
            _version++;
#if UNITY_EDITOR
            OnRecipeChanged?.Invoke(this);
#endif
        }
    }
}