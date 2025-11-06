using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using UnityEngine;

namespace MrPathV2.Editor.Operations
{
    /// <summary>
    ///     绘制纹理操作定义
    /// </summary>
    [CreateAssetMenu(fileName = "Paint Terrain Operation", menuName = "MrPath/Operations/Paint Terrain")]
    public class PaintTerrainOperation : PathTerrainOperation
    {
        private void OnEnable()
        {
            if (string.IsNullOrEmpty(displayName)) displayName = "绘制纹理";
            if (buttonColor == default) buttonColor = new Color(1f, 0.75f, 1f);
        }

        public override TerrainCommandBase CreateCommand(PathCreator creator, IHeightProvider heightProvider) => new PaintTerrainCommand(creator, heightProvider);
    }
}
