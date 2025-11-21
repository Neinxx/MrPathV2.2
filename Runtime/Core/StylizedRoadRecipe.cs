using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        // 透明度（使用 Unity 原生属性）
        [Range(0f, 1f)]
        [Tooltip("整体配方透明度，可统一控制所有图层的可见度")]
        public float masterOpacity = 1f;

        // 图层列表（纯数据，不依赖 Odin）
        public List<RoadLayer> layers = new List<RoadLayer>();

        public int ActiveLayerCount => layers.Count(l => l?.enabled == true);

        private void OnValidate()
        {
            layers ??= new List<RoadLayer>();
            RaiseRecipeChanged();
        }

        // =============== Events & Utilities ===============
        public event Action RecipeChanged;

        public void RaiseRecipeChanged()
        {
            RecipeChanged?.Invoke();
        }

        public IReadOnlyList<RoadLayer> GetLayers() => layers;

        // 已移除所有 Editor/Odin 相关绘制与功能，统一由 StylizedRoadRecipeEditor 负责 Inspector UI。
    }
}
