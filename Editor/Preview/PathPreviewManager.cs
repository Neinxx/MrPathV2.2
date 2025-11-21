using System;
using System.Collections.Generic;
using MrPathV2.Editor.Core;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Preview;
using UnityEditor;
using UnityEngine;
using MrPathV2.Editor.Settings; // 读取项目高级设置
// 统一地形绘制接口
// 命名空间别名，减少全限定名噪音

namespace MrPathV2.Editor.Preview
{
    /// <summary>Handles sampling, mesh generation and rendering for path previews.</summary>
    public sealed class PathPreviewManager : IDisposable
    {

        private const float MaxRenderDistance = 1000f;
        private const float LodThreshold = 100f;
        private static readonly int PreviewAlpha = PreviewShaderContracts.Properties.PreviewAlpha;
        private readonly float _mAlpha;
        private readonly PreviewLineRenderer _mLine = new PreviewLineRenderer();
        private readonly PreviewMaterialManager _mMatMgr;

        private readonly PreviewRenderingOptimizer _mOptimizer = new PreviewRenderingOptimizer();
        private readonly Material _mTemplate;

        private Bounds _mBounds;
        private int _mLastBoundsHash; // 上次道路包围盒哈希（量化）
        private int _mLastGpuTerrainId; // 上次运行 GPU 预览所使用的 Terrain ID
        private int _mLastProfileHash = -1;
        private int _mLastSpineHash; // 上次运行时的脊线哈希

        private List<Material> _mMaterials = new List<Material>();
        private bool _mMaterialsDirty = true;
        private Mesh _mMesh;
        private bool _mMeshDirty = true;
        private Camera _mSceneCam;
        private int _mSceneCamId;
        private MaterialPropertyBlock _mSingleMpb; // 缓存单材质渲染时的属性块，避免重复分配
        private bool _mSpineDirty = true; // replaced previous _dirty
        private UnityEngine.Terrain _mTargetTerrain; // 缓存选中的目标地形

        public PathPreviewManager(IPreviewGenerator gen, PreviewMaterialManager matMgr, Material template, float alpha)
        {
            Generator = gen ?? throw new ArgumentNullException(nameof(gen));
            _mMatMgr = matMgr ?? throw new ArgumentNullException(nameof(matMgr));
            _mTemplate = template;
            _mAlpha = alpha;

            // Ensure materials list is always initialized
            _mMaterials ??= new List<Material>();

            // 注入现代预览线风格（依赖注入，不直接耦合具体实现）
            _mLine.UseStyleProvider(ModernPreviewLineStyles.Provider);

            // 从项目高级设置读取预览线参数并注入到运行时渲染器
            ApplyPreviewLineConfigFromAdvancedSettings();
        }

        /// <summary>
        ///     从项目设置读取高级预览参数并应用到线渲染器（编辑器侧）。
        /// </summary>
        private void ApplyPreviewLineConfigFromAdvancedSettings()
        {
            var settings = MrPathProjectSettings.LoadExistingSettings();
            var adv = settings ? settings.advancedSettings : null;
            if (!adv) return; // 提前返回：未创建高级设置时使用默认值

            var cfg = new PreviewLineConfig(
                adv.previewAAWidthPixels,
                adv.previewCapAAWidthPixels,
                adv.previewSeamScale,
                (int)adv.previewCapType,
                adv.previewDefaultDashPixels,
                adv.previewFastDashedMode
            );
            _mLine.ApplyPreviewConfig(cfg);
        }

        public PathSpine? LatestSpine { get; private set; }
        public IPreviewGenerator Generator { get; }
        public bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            Generator.Dispose();
            _mMatMgr.Cleanup(); // 使用新的Cleanup方法释放CommandBuffer等资源
            _mMatMgr.Dispose();
            _mOptimizer.Dispose();
            _mLine.Dispose();
            _mMaterials.Clear();
        }
        public PreviewLineRenderer GetSharedLineRenderer() => _mLine;

        public void SetActive(bool value) => IsActive = value;
        public void MarkSpineDirty()
        {
            _mSpineDirty = true;
        }
        // 网格只有在曲线(spine)发生变动时才重建，其他参数变化（如材质）无需重绘网格。
        public void MarkMeshDirty()
        {
            _mMeshDirty = true;
        }
        public void MarkMaterialsDirty() => _mMaterialsDirty = true;
        // Backwards compatibility

        /// <summary>Main update entry called from editor each frame.</summary>
        /// <summary>Main update entry called from editor each frame.</summary>
        public void Update(PathCreator creator, IHeightProvider heightProvider)
        {
            if (!PreUpdateValidation(creator))
                return;

            try
            {
                _mLine.SetUseGpu(GpuPreview.IsEnabled);
                UpdateSpineAndMesh(creator, heightProvider);
                FinalizeMesh();
                SetupMaterialParameters(creator);
                UpdateTerrainTarget(creator);
                RunGpuPreviewIfNeeded(creator);
                UpdateMaterials(creator);
                RenderIfNeeded(creator);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Unexpected error during update: {ex.Message}");
            }
        }

        /// <summary>
        ///     Validates preconditions before update
        /// </summary>
        /// <param name="creator">Path creator</param>
        /// <returns>True if update can proceed</returns>
        private bool PreUpdateValidation(PathCreator creator)
        {
            if (!IsActive || creator?.profile == null)
                return false;

            // Additional null checks for safety
            if (creator.transform == null)
            {
                Debug.LogWarning("[PathPreviewManager] PathCreator transform is null, skipping update");
                return false;
            }

            if (Generator == null)
            {
                Debug.LogError("[PathPreviewManager] Generator is null, cannot update preview");
                return false;
            }

            if (_mMatMgr == null)
            {
                Debug.LogError("[PathPreviewManager] Material manager is null, cannot update preview");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Updates spine and mesh data
        /// </summary>
        /// <param name="creator">Path creator</param>
        /// <param name="heightProvider">Height provider</param>
        private void UpdateSpineAndMesh(PathCreator creator, IHeightProvider heightProvider)
        {
            if (_mSpineDirty)
            {
                try
                {
                    LatestSpine = PathSampler.SamplePath(creator, heightProvider);
                    if (LatestSpine.HasValue && Generator != null)
                    {
                        Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                    }
                    _mSpineDirty = false;
                    _mMeshDirty = false; // spine change implies mesh change
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PathPreviewManager] Error during spine sampling: {ex.Message}");
                    _mSpineDirty = false; // Prevent infinite retry
                }
            }
            else if (_mMeshDirty)
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
                _mMeshDirty = false;
            }
        }

        /// <summary>
        ///     Finalizes the mesh generation
        /// </summary>
        private void FinalizeMesh()
        {
            try
            {
                // 确保网格数据已完成并可用
                if (!Generator.TryFinalizeMesh() && ((Generator.PreviewMesh?.vertexCount ?? 0) <= 0 || !Generator.ForceFinalizeMesh()))
                    return;

                // 即使 Mesh 实例未变化，也需要刷新包围盒以避免剔除使用旧边界
                _mMesh = Generator.PreviewMesh;
                if (_mMesh)
                {
                    _mBounds = _mMesh.bounds;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error during mesh finalization: {ex.Message}");
            }
        }

        /// <summary>
        ///     Sets up material parameters
        /// </summary>
        private void SetupMaterialParameters(PathCreator creator)
        {
            try
            {
                // 计算路径长度并推送到材质管理器
                var pathLen = ComputeSpineLength(LatestSpine);
                _mMatMgr.SetPathLength(pathLen > 0f ? pathLen : 100f);

                // 统一UV语义：网格UV已归一化到0..1，材质重复设为1
                _mMatMgr.SetMeshRepeats(1f, 1f);

                // 推送统一 ROI：与 GPU 计算一致的 PathBounds（按脊线包围盒 + roadWidth 扩展）
                if (LatestSpine.HasValue && creator?.profile != null)
                {
                    var pts = LatestSpine.Value.Points;
                    if (pts != null && pts.Length > 0)
                    {
                        var bounds = new Bounds(pts[0], Vector3.zero);
                        for (var i = 1; i < pts.Length; i++) bounds.Encapsulate(pts[i]);
                        bounds.Expand(creator.profile.roadWidth); // 与 V3 计算一致（按脊线包围盒 + roadWidth 扩展）
                        var boundsXZ = new Vector4(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                        _mMatMgr.SetPreviewBounds(boundsXZ);
                        // 使用脊线派生包围盒作为渲染剔除的世界包围盒，避免仅用 Mesh.bounds 导致旧值或局部空间误差
                        _mBounds = bounds;
                    }
                }
                else if (_mMesh)
                {
                    // 兜底：仍然使用网格包围盒，保证预览在脊线未就绪时也能工作
                    var b = _mBounds;
                    var boundsXZ = new Vector4(b.min.x, b.min.z, b.max.x, b.max.z);
                    _mMatMgr.SetPreviewBounds(boundsXZ);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error during material setup: {ex.Message}");
            }
        }

        /// <summary>
        ///     Updates the target terrain based on the path bounds
        /// </summary>
        /// <param name="creator">Path creator</param>
        private void UpdateTerrainTarget(PathCreator creator)
        {
#if UNITY_EDITOR
            var boundsHashNow = 0;
            // 基于脊线包围盒选择目标 Terrain（优先相交，其次最近），并仅在变化时运行 GPU 预览
            var targetTerrain = _mTargetTerrain;
            var activeTerrains = UnityEngine.Terrain.activeTerrains;
            if (activeTerrains is { Length: > 0 })
            {
                if (LatestSpine.HasValue)
                {
                    targetTerrain = FindBestTerrainForSpine(targetTerrain, activeTerrains, creator, out boundsHashNow);
                }
            }

            // 回退：若未找到相交地形，则使用最近地形
            if (!targetTerrain)
            {
                targetTerrain = FindNearestTerrain(activeTerrains, creator);
            }

            _mMatMgr.SetTargetTerrain(targetTerrain);
            // 更新缓存以供后续帧复用
            _mTargetTerrain = targetTerrain;
            _mLastBoundsHash = boundsHashNow;
#endif
        }

        /// <summary>
        ///     Finds the best terrain for the current spine
        /// </summary>
        /// <param name="targetTerrain">Current target terrain</param>
        /// <param name="activeTerrains">Active terrains</param>
        /// <param name="creator">Path creator</param>
        /// <param name="boundsHashNow">Output bounds hash</param>
        /// <returns>Best terrain</returns>
        private UnityEngine.Terrain FindBestTerrainForSpine(UnityEngine.Terrain targetTerrain, UnityEngine.Terrain[] activeTerrains, PathCreator creator, out int boundsHashNow)
        {
            boundsHashNow = 0;
            var pts = LatestSpine.Value.Points;
            if (pts is not { Length: > 0 })
                return targetTerrain;

            var (minX, maxX, minZ, maxZ) = CalculateBounds(pts);
            var margin = Mathf.Max(0.25f, creator.profile.roadWidth * 0.5f + creator.profile.falloffWidth);
            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            var size = new Vector3(Mathf.Max(0.01f, maxX - minX + margin * 2f), 10000f, Mathf.Max(0.01f, maxZ - minZ + margin * 2f));
            var roadBounds = new Bounds(center, size);
            boundsHashNow = CalcBoundsHash(roadBounds);
            var needRetarget = targetTerrain == null || boundsHashNow != _mLastBoundsHash;

            if (!needRetarget)
                return targetTerrain;

            UnityEngine.Terrain bestTerrain = null;
            var bestOverlap = -1f;
            var bestDist = float.MaxValue;

            foreach (var t in activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var tb = new Bounds(t.GetPosition() + t.terrainData.size / 2f, t.terrainData.size);
                if (tb.Intersects(roadBounds))
                {
                    var (overlapArea, dist) = CalculateOverlapAndDistance(tb, roadBounds, center, t);
                    if (overlapArea > bestOverlap || Mathf.Approximately(overlapArea, bestOverlap) && dist < bestDist)
                    {
                        bestOverlap = overlapArea;
                        bestDist = dist;
                        bestTerrain = t;
                    }
                }
            }

            return bestTerrain;
        }

        /// <summary>
        ///     Calculates min/max bounds for points
        /// </summary>
        /// <param name="pts">Points array</param>
        /// <returns>Min/Max values</returns>
        private (float minX, float maxX, float minZ, float maxZ) CalculateBounds(Vector3[] pts)
        {
            var minX = pts[0].x;
            var maxX = pts[0].x;
            var minZ = pts[0].z;
            var maxZ = pts[0].z;

            for (var i = 1; i < pts.Length; i++)
            {
                var p = pts[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }

            return (minX, maxX, minZ, maxZ);
        }

        /// <summary>
        ///     Calculates overlap area and distance between terrain and road bounds
        /// </summary>
        /// <param name="tb">Terrain bounds</param>
        /// <param name="roadBounds">Road bounds</param>
        /// <param name="center">Center point</param>
        /// <param name="t">Terrain</param>
        /// <returns>Overlap area and distance</returns>
        private (float overlapArea, float dist) CalculateOverlapAndDistance(Bounds tb, Bounds roadBounds, Vector3 center, UnityEngine.Terrain t)
        {
            var ixMin = Mathf.Max(tb.min.x, roadBounds.min.x);
            var izMin = Mathf.Max(tb.min.z, roadBounds.min.z);
            var ixMax = Mathf.Min(tb.max.x, roadBounds.max.x);
            var izMax = Mathf.Min(tb.max.z, roadBounds.max.z);
            var overlapArea = Mathf.Max(0f, (ixMax - ixMin) * (izMax - izMin));
            var dist = (center - t.GetPosition()).sqrMagnitude;
            return (overlapArea, dist);
        }

        /// <summary>
        ///     Finds the nearest terrain to the path creator
        /// </summary>
        /// <param name="activeTerrains">Active terrains</param>
        /// <param name="creator">Path creator</param>
        /// <returns>Nearest terrain</returns>
        private UnityEngine.Terrain FindNearestTerrain(UnityEngine.Terrain[] activeTerrains, PathCreator creator)
        {
            var pos0 = creator.transform.position;
            var minDist = float.MaxValue;
            UnityEngine.Terrain nearestTerrain = null;

            foreach (var t in activeTerrains)
            {
                if (t == null) continue;
                var d = Vector3.Distance(pos0, t.GetPosition());
                if (d < minDist)
                {
                    minDist = d;
                    nearestTerrain = t;
                }
            }

            return nearestTerrain;
        }

        /// <summary>
        ///     Runs GPU preview if needed
        /// </summary>
        /// <param name="creator">Path creator</param>
        private void RunGpuPreviewIfNeeded(PathCreator creator)
        {
#if UNITY_EDITOR
            // CPU-only 模式：遵循“提前返回”原则，直接退出以禁用 GPU 预览路径。
            // 材质更新将自动走 CPU 贴图/参数推送逻辑（PreviewMaterialManager.SetupMaskTextures & PushCpuTerrainParameters）。
            return;
#endif
        }

        /// <summary>
        ///     Updates materials
        /// </summary>
        /// <param name="creator">Path creator</param>
        private void UpdateMaterials(PathCreator creator)
        {
            try
            {
                // 更新材质并刷新缓存
                if (_mMatMgr != null && creator?.profile != null)
                {
                    _mMatMgr.Update(creator.profile, _mTemplate, _mAlpha);
                    if (_mMaterialsDirty)
                    {
                        RefreshMaterialCache();
                        _mMaterialsDirty = false;
                    }
                    else
                    {
                        // 如果内嵌 Mask 等资源变更导致材质实例被替换，也需要刷新缓存；通过检查引用变化实现。
                        var renderMaterials = _mMatMgr.GetRenderMaterials();
                        var currentMatCount = renderMaterials?.Count ?? 0;
                        if (currentMatCount != _mMaterials.Count)
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
            _mLastProfileHash = CalcProfileHash(creator.profile);
        }

        /// <summary>
        ///     Renders the preview if needed
        /// </summary>
        /// <param name="creator">Path creator</param>
        private void RenderIfNeeded(PathCreator creator)
        {
            if (!creator.profile.showPreviewMesh || _mMesh == null || _mMaterials.Count == 0)
                return;

            try
            {
                Render();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error during rendering: {ex.Message}");
            }
        }

        private Camera SceneCamera()
        {
            var cam = SceneView.lastActiveSceneView?.camera;
            if (cam != null && cam.GetInstanceID() == _mSceneCamId) return _mSceneCam;
            _mSceneCam = cam;
            _mSceneCamId = cam ? cam.GetInstanceID() : 0;
            return _mSceneCam;
        }

        private void Render()
        {
            var cam = SceneCamera();
            if (cam == null) return;

            var dist = Vector3.Distance(cam.transform.position, _mBounds.center);
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), _mBounds) || dist > MaxRenderDistance) return;

            var count = Mathf.Min(GetLodMaterialCount(dist), _mMaterials.Count);
            var matrix = Matrix4x4.identity;

            if (count > 1)
            {
                _mOptimizer.ClearBatches();
                _mOptimizer.SetGlobalProperty("_PreviewAlpha", _mAlpha);
                for (var i = 0; i < count; i++) _mOptimizer.AddRenderItem(_mMesh, _mMaterials[i], matrix);
                _mOptimizer.ExecuteBatchedRender(cam);
            }
            else
            {
                // 使用 MaterialPropertyBlock 而非全局 Shader 属性，避免因其他编辑器 UI 绘制修改全局状态导致闪烁。
                _mSingleMpb ??= new MaterialPropertyBlock();
                _mSingleMpb.SetFloat(PreviewAlpha, _mAlpha);
                Graphics.DrawMesh(_mMesh, matrix, _mMaterials[0], 0, cam, 0, _mSingleMpb);
            }
        }

        private int GetLodMaterialCount(float dist) => dist > LodThreshold ? Mathf.Max(1, _mMaterials.Count / 2) : _mMaterials.Count;

        private void RefreshMaterialCache()
        {
            try
            {
                _mMaterials.Clear();
                var list = _mMatMgr?.GetRenderMaterials();
                if (list != null)
                {
                    _mMaterials.AddRange(list);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error in RefreshMaterialCache: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Ensure materials list is in a valid state even if refresh fails
                _mMaterials ??= new List<Material>();
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

        // 新增：稳定的包围盒哈希，用于判断道路边界是否变化
        private static int CalcBoundsHash(Bounds b)
        {
            unchecked
            {
                // 量化到0.5米精度，避免浮点微抖导致频繁变化
                var q = 2; // 1/q 米分辨率 -> 0.5m
                var minX = Mathf.RoundToInt(b.min.x * q);
                var minZ = Mathf.RoundToInt(b.min.z * q);
                var maxX = Mathf.RoundToInt(b.max.x * q);
                var maxZ = Mathf.RoundToInt(b.max.z * q);
                var h = 17;
                h = h * 31 + minX;
                h = h * 31 + minZ;
                h = h * 31 + maxX;
                h = h * 31 + maxZ;
                return h;
            }
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
