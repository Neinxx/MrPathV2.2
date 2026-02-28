// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathTerrainOperations.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 管理数据驱动的地形操作按钮列表。
    /// </summary>
    public class MrPathTerrainOperations : ScriptableObject
    {
        [Header("数据驱动操作")]
        [Tooltip("在场景工具窗口中显示的操作列表（将按 'order' 字段排序）")]
        public PathTerrainOperation[] operations;
    }
}