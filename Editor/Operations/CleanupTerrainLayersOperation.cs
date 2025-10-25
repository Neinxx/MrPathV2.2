using MrPathV2._2.Editor.Terrain;
using MrPathV2._2.Runtime.Core;
using MrPathV2._2.Runtime.Interfaces;
using UnityEngine;

namespace MrPathV2._2.Editor.Operations
{
    /// <summary>
    ///     "清理未使用图层" 的外部操作资产。用于在 Overlay 中生成按钮并创建命令执行。
    ///     遵循开闭：新增/修改操作只需新增/调整资产，不改 Overlay。
    /// </summary>
    [CreateAssetMenu(fileName = "CleanupTerrainLayersOperation", menuName = "MrPath/Terrain Operation/Cleanup Unused Layers")]
    public class CleanupTerrainLayersOperation : PathTerrainOperation
    {
        private void OnEnable()
        {

            if (string.IsNullOrEmpty(displayName))
                displayName = "清理未使用图层";

            if (buttonColor == default)
                buttonColor = new Color(0.75f, 0.75f, 0.85f);

            if (order == 0)
                order = 999;

            if (string.IsNullOrEmpty(operationId))
                operationId = "cleanup_unused_layers";
        }

        public override bool CanExecute(PathCreator creator)
        {
            if (!base.CanExecute(creator)) return false;
            return UnityEngine.Terrain.activeTerrains != null && UnityEngine.Terrain.activeTerrains.Length > 0;
        }

        public override TerrainCommandBase CreateCommand(PathCreator creator, IHeightProvider heightProvider) => new CleanupTerrainLayersCommand(creator, heightProvider);
    }
}
