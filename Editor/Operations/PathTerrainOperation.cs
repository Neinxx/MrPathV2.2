using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Operations
{
    /// <summary>
    ///     数据驱动的地形操作定义。遵循开闭：新增操作只需新增资产。
    /// </summary>
    public abstract class PathTerrainOperation : ScriptableObject
    {
        [Header("显示与排序")]
        public string displayName = "操作";
        public Texture2D icon;
        public Color buttonColor = Color.white;
        public int order;
        [Tooltip("稳定的操作标识（可选）。若为空，将使用 displayName 或资产名称作为标识。")]
        public string operationId = string.Empty;

        /// <summary>
        ///     校验是否可执行。默认要求路径有效。
        /// </summary>
        public virtual bool CanExecute(PathCreator creator) => creator != null && creator.profile != null && creator.pathData is { KnotCount: >= 2 };

        /// <summary>
        ///     通过上下文创建具体命令。保持数据与行为分离：资产仅描述，不直接执行。
        /// </summary>
        public abstract TerrainCommandBase CreateCommand(PathCreator creator, IHeightProvider heightProvider);
    }
}
