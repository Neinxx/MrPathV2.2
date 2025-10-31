using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Operations
{
    /// <summary>
    ///     压平地形操作定义
    /// </summary>
    [CreateAssetMenu(fileName = "Flatten Terrain Operation", menuName = "MrPath/Operations/Flatten Terrain")]
    public class FlattenTerrainOperation : PathTerrainOperation
    {
        private void OnEnable()
        {
            if (string.IsNullOrEmpty(displayName)) displayName = "压平地形";
            if (buttonColor == default) buttonColor = new Color(0.6f, 0.85f, 1f);
        }

        public override TerrainCommandBase CreateCommand(PathCreator creator, IHeightProvider heightProvider) => new FlattenTerrainCommand(creator, heightProvider);
    }
}
