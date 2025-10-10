using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 默认实现：使用 TerrainHeightProviderAdapter 包装 TerrainHeightProvider。
    /// </summary>
    [CreateAssetMenu(fileName = "Default Height Provider Factory", menuName = "MrPath/Factories/Height Provider Factory")]
    public class DefaultHeightProviderFactory : HeightProviderFactory
    {
        public override IHeightProvider Create()
        {
            return new TerrainHeightProviderAdapter(new TerrainHeightProvider());
        }
    }
}