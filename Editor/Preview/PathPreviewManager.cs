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

        private List<Material> _materials = new List<Material>();
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
        private int _lastGpuTerrainId; // 上次运行 GPU 预览所使用的 Terrain ID
        private int _lastSpineHash;     // 上次运行时的脊线哈希

        public PathPreviewManager(IPreviewGenerator gen, PreviewMaterialManager matMgr, Material template, float alpha)
        {
            Generator = gen ?? throw new ArgumentNullException(nameof(gen));
            _matMgr = matMgr ?? throw new ArgumentNullException(nameof(matMgr));
            _template = template;
            _alpha = alpha;
            
            // Ensure materials list is always initialized
            if (_materials == null)
            {
                _materials = new List<Material>();
            }
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
            try
            {
                if (!IsActive || creator?.profile == null) return;

                // Additional null checks for safety
                if (creator.transform == null)
                {
                    Debug.LogWarning("[PathPreviewManager] PathCreator transform is null, skipping update");
                    return;
                }

                if (Generator == null)
                {
                    Debug.LogError("[PathPreviewManager] Generator is null, cannot update preview");
                    return;
                }

                if (_matMgr == null)
                {
                    Debug.LogError("[PathPreviewManager] Material manager is null, cannot update preview");
                    return;
                }

                // 始终尝试更新材质管理器：内部 CalculateHash 会确保仅在参数变化时才重建材质，性能开销可忽略。
#if UNITY_EDITOR
                // 移除初始近邻选择，改为在下方基于道路包围盒选择目标 Terrain
                // 这里暂不设置 _matMgr 的目标 Terrain，稍后在 GPU 预览阶段统一处理。
#endif

                if (_spineDirty)
                {
                    try
                    {
                        LatestSpine = PathSampler.SamplePath(creator, heightProvider);
                        if (LatestSpine.HasValue && Generator != null)
                        {
                            Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                        }
                        _spineDirty = false;
                        _meshDirty = false; // spine change implies mesh change
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PathPreviewManager] Error during spine sampling: {ex.Message}");
                        _spineDirty = false; // Prevent infinite retry
                    }
                }
                else if (_meshDirty)
                {
                    try
                    {
                        if (LatestSpine.HasValue && Generator != null)
                        {
                            Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PathPreviewManager] Error during mesh generation: {ex.Message}");
                    }
                    _meshDirty = false;
                }
                // 移除仅因非曲线变化而触发的网格重建逻辑，避免频繁重绘。

                try
                {
                    if (Generator.TryFinalizeMesh() || (Generator.PreviewMesh?.vertexCount ?? 0) > 0 && Generator.ForceFinalizeMesh())
                    {
                        if (_mesh != Generator.PreviewMesh)
                        {
                            _mesh = Generator.PreviewMesh;
                            _bounds = _mesh ? _mesh.bounds : default;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PathPreviewManager] Error during mesh finalization: {ex.Message}");
                }

                try
                {
                    // 计算路径长度并推送到材质管理器
                    var pathLen = ComputeSpineLength(LatestSpine);
                    _matMgr.SetPathLength(pathLen > 0f ? pathLen : 100f);

                    // 统一UV语义：网格UV已归一化到0..1，材质重复设为1
                    _matMgr.SetMeshRepeats(1f, 1f);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PathPreviewManager] Error during material setup: {ex.Message}");
                }

#if UNITY_EDITOR
                // 基于脊线包围盒选择目标 Terrain（优先相交，其次最近），并仅在变化时运行 GPU 预览
                UnityEngine.Terrain targetTerrain = null;
                var activeTerrains = UnityEngine.Terrain.activeTerrains;
                if (activeTerrains != null && activeTerrains.Length > 0)
                {
                    if (LatestSpine.HasValue)
                    {
                        var pts = LatestSpine.Value.Points;
                        if (pts != null && pts.Length > 0)
                        {
                            var minX = pts[0].x; var maxX = pts[0].x;
                            var minZ = pts[0].z; var maxZ = pts[0].z;
                            for (var i = 1; i < pts.Length; i++)
                            {
                                var p = pts[i];
                                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                            }
                            var margin = Mathf.Max(0.25f, creator.profile.roadWidth * 0.5f + creator.profile.falloffWidth);
                            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
                            var size = new Vector3(Mathf.Max(0.01f, (maxX - minX) + margin * 2f), 10000f, Mathf.Max(0.01f, (maxZ - minZ) + margin * 2f));
                            var roadBounds = new Bounds(center, size);

                            UnityEngine.Terrain bestTerrain = null;
                            var bestOverlap = -1f;
                            var bestDist = float.MaxValue;
                            foreach (var t in activeTerrains)
                            {
                                if (t == null || t.terrainData == null) continue;
                                var tb = new Bounds(t.GetPosition() + t.terrainData.size / 2f, t.terrainData.size);
                                if (tb.Intersects(roadBounds))
                                {
                                    var ixMin = Mathf.Max(tb.min.x, roadBounds.min.x);
                                    var izMin = Mathf.Max(tb.min.z, roadBounds.min.z);
                                    var ixMax = Mathf.Min(tb.max.x, roadBounds.max.x);
                                    var izMax = Mathf.Min(tb.max.z, roadBounds.max.z);
                                    var overlapArea = Mathf.Max(0f, (ixMax - ixMin) * (izMax - izMin));
                                    var dist = (center - t.GetPosition()).sqrMagnitude;
                                    if (overlapArea > bestOverlap || (Mathf.Approximately(overlapArea, bestOverlap) && dist < bestDist))
                                    {
                                        bestOverlap = overlapArea;
                                        bestDist = dist;
                                        bestTerrain = t;
                                    }
                                }
                            }
                            targetTerrain = bestTerrain;
                        }
                    }

                    // 回退：若未找到相交地形，则使用最近地形
                    if (!targetTerrain)
                    {
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
                }

                _matMgr.SetTargetTerrain(targetTerrain);

                // —— 仅在发生变化时运行 GPU 预览 ——
                var profileHashNow = CalcProfileHash(creator.profile);
                var terrainIdNow = targetTerrain ? targetTerrain.GetInstanceID() : 0;
                var spineHashNow = LatestSpine.HasValue ? CalcSpineHash(LatestSpine.Value) : 0;
                var cacheHasRt = targetTerrain && MrPathV2.Editor.Terrain.GpuPreviewCache.TryGet(targetTerrain, out var cachedRt) && cachedRt;
                var shouldRunGpu = PreviewMaterialManager.EnableGpuPreview && targetTerrain && LatestSpine.HasValue && (
                    !cacheHasRt || terrainIdNow != _lastGpuTerrainId || spineHashNow != _lastSpineHash || profileHashNow != _lastProfileHash);
                if (shouldRunGpu)
                {
                    if (GpuPreviewRunner.TryRun(targetTerrain, LatestSpine.Value, creator.profile))
                    {
                        _lastGpuTerrainId = terrainIdNow;
                        _lastSpineHash = spineHashNow;
                        _lastProfileHash = profileHashNow;
                    }
                }
#endif

                try
            {
                // 更新材质并刷新缓存
                if (_matMgr != null && creator?.profile != null)
                {
                    _matMgr.Update(creator.profile, _template, _alpha);
                    if (_materialsDirty)
                    {
                        RefreshMaterialCache();
                        _materialsDirty = false;
                    }
                    else
                    {
                        // 如果内嵌 Mask 等资源变更导致材质实例被替换，也需要刷新缓存；通过检查引用变化实现。
                        var renderMaterials = _matMgr.GetRenderMaterials();
                        var currentMatCount = renderMaterials?.Count ?? 0;
                        if (currentMatCount != _materials.Count)
                        {
                            RefreshMaterialCache();
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[PathPreviewManager] Skipping material update - missing material manager or profile");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error during material update: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }

                // 更新用于判断 Profile 引用变化的哈希（不再决定是否调用 Update，仅用于脏标记优化）
                _lastProfileHash = CalcProfileHash(creator.profile);

                if (!creator.profile.showPreviewMesh || _mesh == null || _materials.Count == 0) return;

                try
                {
                    Render();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PathPreviewManager] Error during rendering: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Unexpected error during update: {ex.Message}");
            }
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
            try
            {
                _materials.Clear();
                var list = _matMgr?.GetRenderMaterials();
                if (list != null) 
                {
                    _materials.AddRange(list);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error in RefreshMaterialCache: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Ensure materials list is in a valid state even if refresh fails
                if (_materials == null)
                {
                    _materials = new List<Material>();
                }
            }
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

        // 新增：稳定的脊线哈希，用于仅在变化时触发 GPU 预览
        private static int CalcSpineHash(PathSpine spine)
        {
            unchecked
            {
                var h = spine.VertexCount;
                var pts = spine.Points;
                if (pts == null || pts.Length == 0) return h;
                // 对坐标进行量化以稳定哈希，采样最多64个点
                var step = Mathf.Max(1, pts.Length / 64);
                for (var i = 0; i < pts.Length; i += step)
                {
                    var p = pts[i];
                    var x = Mathf.RoundToInt(p.x * 1000f);
                    var z = Mathf.RoundToInt(p.z * 1000f);
                    h = h * 31 + x;
                    h = h * 31 + z;
                }
                return h;
            }
        }
    }
}
