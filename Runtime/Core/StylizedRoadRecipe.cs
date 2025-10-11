using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 美术师定义的风格化道路配方：可自由组合项目中的 TerrainLayer，并通过 BlendMask 控制横向分布。
    /// 数据纯净，行为由命令与Job驱动，符合组合与开闭原则。
    /// </summary>
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        [Tooltip("按顺序叠加的纹理图层列表。越靠后将覆盖前者（规范化前）。")]
        public List<BlendLayer> blendLayers = new();
    }
}