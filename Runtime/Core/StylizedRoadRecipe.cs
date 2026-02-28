using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core
{
    /// <summary>
    /// 美术定义的风格化道路配方：可自由组合项目中的 TerrainLayer，并通过 BlendMask 控制横向分布。

    /// </summary>
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        [Title("混合图层 (Passes)")]
        [InfoBox("✨ 支持无限层数：系统会自动处理多Control贴图分配和内存优化。")]

        // 列表显示设置
        [ListDrawerSettings(
            DraggableItems = true,          // 允许拖拽排序
            ShowFoldout = true,             // 显示每个列表项的折叠箭头
            ShowItemCount = true,           // 显示列表项数量
            DefaultExpandedState = true,    // 默认展开整个列表
            CustomAddFunction = "AddNewLayer", // 自定义添加按钮处理函数
            NumberOfItemsPerPage = 10       // 分页显示，避免大量层时UI卡顿
        )]

        // 整体控制属性
        // UI/UX：使主 Opacity 滑块与数值输入框右对齐
        [HorizontalGroup("Header")]
        [LabelText("Master Opacity")]
        [LabelWidth(90)]
        [Range(0f, 1f)]
        [Tooltip("整体配方透明度，可统一控制所有图层的可见度")]
        public float masterOpacity = 1f;

        // 图层列表
        [LabelText("Layers")]
        public List<RoadLayer> layers = new List<RoadLayer>();



#if UNITY_EDITOR
        // [HideInInspector]
        // [HorizontalGroup("Header", Width = 150), LabelText("Preview Width"), MinValue(0.1f)]
        // [Tooltip("仅供编辑器预览使用的参考道路宽度，运行时将使用 PathProfile.roadWidth")]
        [HideInInspector]
        public float width = 5f;

        /// <summary>
        /// Odin Inspector 自定义添加按钮支持的方法。
        /// 在 Layers 列表中点击“Add”时会调用此方法，返回要添加的新元素。
        /// </summary>
        private RoadLayer AddNewLayer()
        {
            var newLayer = RoadLayer.CreateDefault(layers.Count + 1);
            layers.Add(newLayer);
            // 触发变更事件，通知相关系统刷新
            RecipeChanged?.Invoke();
            return newLayer;
        }
#endif

        // 所有旧版 BlendLayer 数据已迁移，已完全移除过时字段。

        /// <summary>
        /// 当配方内部数据在编辑器或运行时被修改时触发，用于通知依赖此配方的系统（如 PathCreator、预览管理器等）刷新。
        /// </summary>
        public event System.Action RecipeChanged;

        /// <summary>
        /// Call this method to notify listeners that the recipe has changed.
        /// This is required because the RecipeChanged event can only be invoked
        /// from within the declaring class.
        /// </summary>
        public void RaiseRecipeChanged()
        {
            RecipeChanged?.Invoke();
        }

        private void OnValidate()
        {
            // 保证列表非空
            layers ??= new List<RoadLayer>();
            // 通知变化
            RecipeChanged?.Invoke();
        }

        /// <summary>
        /// 获取当前图层列表。
        /// </summary>
        public IReadOnlyList<RoadLayer> GetLayers() => layers;

        /// <summary>
        /// 当前有效图层数量（忽略 disabled 条目）。
        /// </summary>
        public int ActiveLayerCount
        {
            get
            {
                var list = GetLayers();
                return list.Count(l => l is { enabled: true });
            }
        }
    }
}