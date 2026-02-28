using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 适配器：将 TerrainHeightProvider 封装为 IHeightProvider。
    /// </summary>
    public class TerrainHeightProviderAdapter : IHeightProvider
    {
        private readonly TerrainHeightProvider _impl;

        public TerrainHeightProviderAdapter(TerrainHeightProvider impl)
        {
            _impl = impl ?? new TerrainHeightProvider();
        }

        public float GetHeight(Vector3 worldPos) => _impl.GetHeight(worldPos);
        public Vector3 GetNormal(Vector3 worldPos) => _impl.GetNormal(worldPos);
        public void MarkAsDirty() => _impl.MarkAsDirty();
        public void Dispose() => _impl.Dispose();
    }
}