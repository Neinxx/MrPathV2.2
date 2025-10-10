using UnityEngine;
using UnityEditor;

namespace MrPathV2
{
    /// <summary>
    /// 数据驱动的地形操作定义。遵循开闭：新增操作只需新增资产。
    /// </summary>
    public abstract class PathTerrainOperation : ScriptableObject
    {
        [Header("显示与排序")]
        public string displayName = "操作";
        public Texture2D icon;
        public Color buttonColor = Color.white;
        public int order = 0;

        /// <summary>
        /// 校验是否可执行。默认要求路径有效。
        /// </summary>
        public virtual bool CanExecute(PathCreator creator)
        {
            return creator != null && creator.profile != null && creator.pathData != null && creator.pathData.KnotCount >= 2;
        }

        /// <summary>
        /// 通过上下文创建具体命令。保持数据与行为分离：资产仅描述，不直接执行。
        /// </summary>
        public abstract TerrainCommandBase CreateCommand(PathCreator creator, IHeightProvider heightProvider);
    }
}