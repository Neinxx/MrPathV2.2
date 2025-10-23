using System.Collections.Generic;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Preview
{
    /// <summary>Handles sampling, mesh generation and rendering for path previews.</summary>
    public sealed class PathPreviewManager : System.IDisposable
    {
        private static readonly int PreviewAlpha = Shader.PropertyToID("_PreviewAlpha");
        readonly IPreviewGenerator _generator;
        readonly PreviewMaterialManager _matMgr;
        readonly Material _template;
        readonly float _alpha;

        bool _active = true;
        bool _spineDirty = true; // replaced previous _dirty
        bool _meshDirty = true;
        bool _materialsDirty = true;

        readonly List<Material> _materials = new();
        Mesh _mesh;
        Bounds _bounds;
        int _lastProfileHash = -1;
        Camera _sceneCam;
        int _sceneCamId;

        readonly PreviewRenderingOptimizer _optimizer = new();
        readonly PreviewLineRenderer _line = new();
        private MaterialPropertyBlock _singleMpb; // 缓存单材质渲染时的属性块，避免重复分配

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
        public void MarkSpineDirty() { _spineDirty = true; }
        // 网格只有在曲线(spine)发生变动时才重建，其他参数变化（如材质）无需重绘网格。
        public void MarkMeshDirty() { _meshDirty = true; }
        public void MarkMaterialsDirty() => _materialsDirty = true;
        // Backwards compatibility
        public void MarkDirty() => MarkSpineDirty();

        /// <summary>Main update entry called from editor each frame.</summary>
        public void Update(PathCreator creator, IHeightProvider heightProvider)
        {
            if (!_active || creator?.profile == null) return;

            // 始终尝试更新材质管理器：内部 CalculateHash 会确保仅在参数变化时才重建材质，性能开销可忽略。
#if UNITY_EDITOR
            // 设置目标 Terrain 供 GPU 预览使用。此处简单选取与 PathCreator 最接近的活跃 Terrain，后续可根据包围盒精确匹配。
            var activeTerrains = UnityEngine.Terrain.activeTerrains;
            UnityEngine.Terrain targetTerrain = null;
            if (activeTerrains != null && activeTerrains.Length > 0)
            {
                // 取离路径起点最近的 Terrain
                var pos0 = creator.transform.position;
                var minDist = float.MaxValue;
                foreach (var t in activeTerrains)
                {
                    if (t == null) continue;
                    var d = Vector3.Distance(pos0, t.GetPosition());
                    if (d < minDist)
                    {
                        minDist = d;
                        targetTerrain = t;
                    }
                }
            }
            _matMgr.SetTargetTerrain(targetTerrain);
#endif


            if (_spineDirty)
            {
                LatestSpine = PathSampler.SamplePath(creator, heightProvider);
                if (LatestSpine.HasValue)
                {
                    _generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                }
                _spineDirty = false;
                _meshDirty = false; // spine change implies mesh change
            }
            else if (_meshDirty)
            {
                if (LatestSpine.HasValue)
                {
                    _generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                }
                _meshDirty = false;
            }
            // 移除仅因非曲线变化而触发的网格重建逻辑，避免频繁重绘。

            if (_generator.TryFinalizeMesh() || (_generator.PreviewMesh?.vertexCount ?? 0) > 0 && _generator.ForceFinalizeMesh())
            {
                if (_mesh != _generator.PreviewMesh)
                {
                    _mesh = _generator.PreviewMesh;
                    _bounds = _mesh ? _mesh.bounds : default;
                }
            }

            // 计算路径长度并推送到材质管理器
            float pathLen = ComputeSpineLength(LatestSpine);
            _matMgr.SetPathLength(pathLen > 0f ? pathLen : 100f);

            // 计算 Mesh UV 重复（Across/Along），用于着色器自适应遮罩采样
            float tileX = 1f, tileY = 1f;
            var layers = creator.profile.roadRecipe?.GetLayers();
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var tl = layers[i]?.contentLayer;
                    if (tl != null && tl.diffuseTexture != null)
                    {
                        var sz = tl.tileSize;
                        tileX = Mathf.Approximately(sz.x, 0f) ? 1f : sz.x;
                        tileY = Mathf.Approximately(sz.y, 0f) ? 1f : sz.y;
                        break;
                    }
                }
            }
            var acrossRepeat = Mathf.Max(1e-4f, creator.profile.roadWidth / tileX);
            var alongRepeat = Mathf.Max(1e-4f, (pathLen > 0f ? pathLen : 1f) / tileY);
            _matMgr.SetMeshRepeats(acrossRepeat, alongRepeat);

            // 更新材质并刷新缓存
            _matMgr.Update(creator.profile, _template, _alpha);
            if (_materialsDirty)
            {
                RefreshMaterialCache();
                _materialsDirty = false;
            }
            else
            {
                // 如果内嵌 Mask 等资源变更导致材质实例被替换，也需要刷新缓存；通过检查引用变化实现。
                int currentMatCount = _matMgr.GetRenderMaterials()?.Count ?? 0;
                if (currentMatCount != _materials.Count)
                {
                    RefreshMaterialCache();
                }
            }

            // 更新用于判断 Profile 引用变化的哈希（不再决定是否调用 Update，仅用于脏标记优化）
            _lastProfileHash = CalcProfileHash(creator.profile);

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
                // 使用 MaterialPropertyBlock 而非全局 Shader 属性，避免因其他编辑器 UI 绘制修改全局状态导致闪烁。
                if (_singleMpb == null)
                    _singleMpb = new MaterialPropertyBlock();
                _singleMpb.SetFloat(PreviewAlpha, _alpha);
                Graphics.DrawMesh(_mesh, matrix, _materials[0], 0, cam, 0, _singleMpb);
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

        // 统一计算脊线长度
        private static float ComputeSpineLength(PathSpine? spine)
        {
            if (!spine.HasValue || spine.Value.VertexCount < 2) return 0f;
            float len = 0f;
            var pts = spine.Value.points;
            for (int i = 1; i < pts.Length; i++)
            {
                len += Vector3.Distance(pts[i - 1], pts[i]);
            }
            return len;
        }
    }
}