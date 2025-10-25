using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Preview
{
    /// <summary>Handles sampling, mesh generation and rendering for path previews.</summary>
    public sealed class PathPreviewManager : IDisposable
    {

        private const float MaxRenderDistance = 1000f;
        private const float LodThreshold = 100f;
        private static readonly int PreviewAlpha = Shader.PropertyToID("_PreviewAlpha");
        private readonly float _alpha;
        private readonly PreviewLineRenderer _line = new PreviewLineRenderer();

        private readonly List<Material> _materials = new List<Material>();
        private readonly PreviewMaterialManager _matMgr;

        private readonly PreviewRenderingOptimizer _optimizer = new PreviewRenderingOptimizer();
        private readonly Material _template;

        private Bounds _bounds;
        private int _lastProfileHash = -1;
        private bool _materialsDirty = true;
        private Mesh _mesh;
        private bool _meshDirty = true;
        private Camera _sceneCam;
        private int _sceneCamId;
        private MaterialPropertyBlock _singleMpb; // 缓存单材质渲染时的属性块，避免重复分配
        private bool _spineDirty = true; // replaced previous _dirty

        public PathPreviewManager(IPreviewGenerator gen, PreviewMaterialManager matMgr, Material template, float alpha)
        {
            Generator = gen;
            _matMgr = matMgr;
            _template = template;
            _alpha = alpha;
        }

        public PathSpine? LatestSpine { get; private set; }
        public IPreviewGenerator Generator { get; }
        public bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            Generator.Dispose();
            _matMgr.Dispose();
            _optimizer.Dispose();
            _line.Dispose();
            _materials.Clear();
        }
        public PreviewLineRenderer GetSharedLineRenderer() => _line;

        public void SetActive(bool value) => IsActive = value;
        public void MarkSpineDirty()
        {
            _spineDirty = true;
        }
        // 网格只有在曲线(spine)发生变动时才重建，其他参数变化（如材质）无需重绘网格。
        public void MarkMeshDirty()
        {
            _meshDirty = true;
        }
        public void MarkMaterialsDirty() => _materialsDirty = true;
        // Backwards compatibility
        public void MarkDirty() => MarkSpineDirty();

        /// <summary>Main update entry called from editor each frame.</summary>
        public void Update(PathCreator creator, IHeightProvider heightProvider)
        {
            if (!IsActive || creator?.profile == null) return;

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
                    Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                }
                _spineDirty = false;
                _meshDirty = false; // spine change implies mesh change
            }
            else if (_meshDirty)
            {
                if (LatestSpine.HasValue)
                {
                    Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                }
                _meshDirty = false;
            }
            // 移除仅因非曲线变化而触发的网格重建逻辑，避免频繁重绘。

            if (Generator.TryFinalizeMesh() || (Generator.PreviewMesh?.vertexCount ?? 0) > 0 && Generator.ForceFinalizeMesh())
            {
                if (_mesh != Generator.PreviewMesh)
                {
                    _mesh = Generator.PreviewMesh;
                    _bounds = _mesh ? _mesh.bounds : default;
                }
            }

            // 计算路径长度并推送到材质管理器
            var pathLen = ComputeSpineLength(LatestSpine);
            _matMgr.SetPathLength(pathLen > 0f ? pathLen : 100f);

            // 统一UV语义：网格UV已归一化到0..1，材质重复设为1
            _matMgr.SetMeshRepeats(1f, 1f);

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
                var currentMatCount = _matMgr.GetRenderMaterials()?.Count ?? 0;
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

        private Camera SceneCamera()
        {
            var cam = SceneView.lastActiveSceneView?.camera;
            if (cam == null || cam.GetInstanceID() != _sceneCamId)
            {
                _sceneCam = cam;
                _sceneCamId = cam ? cam.GetInstanceID() : 0;
            }
            return _sceneCam;
        }

        private void Render()
        {
            var cam = SceneCamera();
            if (cam == null) return;

            var dist = Vector3.Distance(cam.transform.position, _bounds.center);
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), _bounds) || dist > MaxRenderDistance) return;

            var count = Mathf.Min(GetLodMaterialCount(dist), _materials.Count);
            var matrix = Matrix4x4.identity;

            if (count > 1)
            {
                _optimizer.ClearBatches();
                _optimizer.SetGlobalProperty("_PreviewAlpha", _alpha);
                for (var i = 0; i < count; i++) _optimizer.AddRenderItem(_mesh, _materials[i], matrix);
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

        private int GetLodMaterialCount(float dist) => dist > LodThreshold ? Mathf.Max(1, _materials.Count / 2) : _materials.Count;

        private void RefreshMaterialCache()
        {
            _materials.Clear();
            var list = _matMgr.GetRenderMaterials();
            if (list != null) _materials.AddRange(list);
        }

        private static int CalcProfileHash(PathProfile profile)
        {
            unchecked
            {
                var h = profile?.GetHashCode() ?? 0;
                h = h * 31 + (profile?.roadRecipe?.GetHashCode() ?? 0);
                return h;
            }
        }

        // 统一计算脊线长度
        private static float ComputeSpineLength(PathSpine? spine)
        {
            if (!spine.HasValue || spine.Value.VertexCount < 2) return 0f;
            var len = 0f;
            var pts = spine.Value.Points;
            for (var i = 1; i < pts.Length; i++)
            {
                len += Vector3.Distance(pts[i - 1], pts[i]);
            }
            return len;
        }
    }
}
