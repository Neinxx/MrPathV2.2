using UnityEngine;

namespace MrPathV2
{
    
    public class PreviewMeshControllerAdapter : IPreviewGenerator
    {
        private readonly GeneratorPreviewMeshController _impl;

        public PreviewMeshControllerAdapter(PreviewMaterialManager materialManager = null)
        {
            _impl = materialManager != null
                ? new GeneratorPreviewMeshController(materialManager)
                : new GeneratorPreviewMeshController(new PreviewMaterialManager());
        }

        public Mesh PreviewMesh => _impl.PreviewMesh;
        public void StartMeshGeneration(PathSpine spine, PathProfile profile) => _impl.StartMeshGeneration(spine, profile);
        public bool TryFinalizeMesh() => _impl.TryFinalizeMesh();
        public bool ForceFinalizeMesh() => _impl.ForceFinalizeMesh();
        public void Dispose() => _impl.Dispose();
    }
}