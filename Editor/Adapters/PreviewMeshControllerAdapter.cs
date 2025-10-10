using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 适配器：将 PreviewMeshController 封装为 IPreviewGenerator。
    /// </summary>
    public class PreviewMeshControllerAdapter : IPreviewGenerator
    {
        private readonly PreviewMeshController _impl;

        public PreviewMeshControllerAdapter(PreviewMeshController impl)
        {
            _impl = impl ?? new PreviewMeshController();
        }

        public Mesh PreviewMesh => _impl.PreviewMesh;
        public void StartMeshGeneration(PathSpine spine, PathProfile profile) => _impl.StartMeshGeneration(spine, profile);
        public bool TryFinalizeMesh() => _impl.TryFinalizeMesh();
        public void Dispose() => _impl.Dispose();
    }
}