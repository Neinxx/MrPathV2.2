using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

namespace MrPathV2
{
    /// <summary>Handles sampling, mesh generation and rendering for path previews.</summary>
    public sealed class PathPreviewManager : System.IDisposable
    {
        readonly IPreviewGenerator _generator;
        readonly PreviewMaterialManager _matMgr;
        readonly Material _template;
        readonly float _alpha;

        bool _active = true;
        bool _dirty = true;
        bool _materialsDirty = true;

        readonly List<Material> _materials = new();
        Mesh _mesh;
        Bounds _bounds;
        int _lastProfileHash = -1;
        Camera _sceneCam;
        int _sceneCamId;

        readonly PreviewRenderingOptimizer _optimizer = new();
        readonly PreviewLineRenderer _line = new();

        const float MaxRenderDistance = 1000f;
        const float LodThreshold = 100f;

        public PathSpine? LatestSpine { get; private set; }
        public IPreviewGenerator Generator => _generator;
        public PreviewLineRenderer GetSharedLineRenderer() => _line;
        public bool IsActive => _active;

        public PathPreviewManager(IPreviewGenerator gen, PreviewMaterialManager matMgr, Material template, float alpha)
        {
            _generator = gen;
            _matMgr = matMgr;
            _template = template;
            _alpha = alpha;
        }

        public void SetActive(bool value) => _active = value;
        public void MarkDirty() => _dirty = true;
        public void MarkMaterialsDirty() => _materialsDirty = true;

        /// <summary>Main update entry called from editor each frame.</summary>
        public void Update(PathCreator creator, IHeightProvider heightProvider)
        {
            if (!_active || creator?.profile == null) return;

            var profileHash = CalcProfileHash(creator.profile);
            if (_materialsDirty || profileHash != _lastProfileHash)
            {
                _matMgr.Update(creator.profile, _template, _alpha);
                RefreshMaterialCache();
                _materialsDirty = false;
                _lastProfileHash = profileHash;
            }

            if (_dirty)
            {
                LatestSpine = PathSampler.SamplePath(creator, heightProvider);
                if (LatestSpine.HasValue)
                {
                    _generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                }
                _dirty = false;
            }

            if (_generator.TryFinalizeMesh() || (_generator.PreviewMesh?.vertexCount ?? 0) > 0 && _generator.ForceFinalizeMesh())
            {
                if (_mesh != _generator.PreviewMesh)
                {
                    _mesh = _generator.PreviewMesh;
                    _bounds = _mesh ? _mesh.bounds : default;
                }
            }

            if (!creator.profile.showPreviewMesh || _mesh == null || _materials.Count == 0) return;

            Render();
        }

        Camera SceneCamera()
        {
            var cam = SceneView.lastActiveSceneView?.camera;
            if (cam == null || cam.GetInstanceID() != _sceneCamId)
            {
                _sceneCam = cam;
                _sceneCamId = cam ? cam.GetInstanceID() : 0;
            }
            return _sceneCam;
        }

        void Render()
        {
            var cam = SceneCamera();
            if (cam == null) return;

            var dist = Vector3.Distance(cam.transform.position, _bounds.center);
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), _bounds) || dist > MaxRenderDistance) return;

            int count = Mathf.Min(GetLodMaterialCount(dist), _materials.Count);
            var matrix = Matrix4x4.identity;

            if (count > 1)
            {
                _optimizer.ClearBatches();
                _optimizer.SetGlobalProperty("_PreviewAlpha", _alpha);
                for (int i = 0; i < count; i++) _optimizer.AddRenderItem(_mesh, _materials[i], matrix);
                _optimizer.ExecuteBatchedRender(cam);
            }
            else
            {
                Shader.SetGlobalFloat("_PreviewAlpha", _alpha);
                Graphics.DrawMesh(_mesh, matrix, _materials[0], 0, cam);
            }
        }

        int GetLodMaterialCount(float dist) => dist > LodThreshold ? Mathf.Max(1, _materials.Count / 2) : _materials.Count;

        void RefreshMaterialCache()
        {
            _materials.Clear();
            var list = _matMgr.GetRenderMaterials();
            if (list != null) _materials.AddRange(list);
        }

        static int CalcProfileHash(PathProfile profile)
        {
            unchecked
            {
                var h = profile?.GetHashCode() ?? 0;
                h = h * 31 + (profile?.roadRecipe?.GetHashCode() ?? 0);
                return h;
            }
        }

        public void Dispose()
        {
            _generator.Dispose();
            _matMgr.Dispose();
            _optimizer.Dispose();
            _line.Dispose();
            _materials.Clear();
        }
    }
}