using System;
using System.Collections.Generic;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;
using __temp.MrPathV2.Runtime.Preview;
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
        private static readonly int PreviewAlpha = PreviewShaderContracts.Properties.PreviewAlpha;
        private readonly float m_Alpha;
        private readonly PreviewLineRenderer m_Line = new PreviewLineRenderer();

        private List<Material> m_Materials = new List<Material>();
        private readonly PreviewMaterialManager m_MatMgr;

        private readonly PreviewRenderingOptimizer m_Optimizer = new PreviewRenderingOptimizer();
        private readonly Material m_Template;

        private Bounds m_Bounds;
        private int m_LastProfileHash = -1;
        private bool m_MaterialsDirty = true;
        private Mesh m_Mesh;
        private bool m_MeshDirty = true;
        private Camera m_SceneCam;
        private int m_SceneCamId;
        private MaterialPropertyBlock m_SingleMpb; // 缓存单材质渲染时的属性块，避免重复分配
        private bool m_SpineDirty = true; // replaced previous _dirty
        private int m_LastGpuTerrainId; // 上次运行 GPU 预览所使用的 Terrain ID
        private int m_LastSpineHash;     // 上次运行时的脊线哈希
        private int m_LastBoundsHash;    // 上次道路包围盒哈希（量化）
        private UnityEngine.Terrain m_TargetTerrain; // 缓存选中的目标地形

        public PathPreviewManager(IPreviewGenerator gen, PreviewMaterialManager matMgr, Material template, float alpha)
        {
            Generator = gen ?? throw new ArgumentNullException(nameof(gen));
            m_MatMgr = matMgr ?? throw new ArgumentNullException(nameof(matMgr));
            m_Template = template;
            m_Alpha = alpha;

            // Ensure materials list is always initialized
            if (m_Materials == null)
            {
                m_Materials = new List<Material>();
            }
        }

        public PathSpine? LatestSpine { get; private set; }
        public IPreviewGenerator Generator { get; }
        public bool IsActive { get; private set; } = true;

        public void Dispose()
        {
            Generator.Dispose();
            m_MatMgr.Cleanup(); // 使用新的Cleanup方法释放CommandBuffer等资源
            m_MatMgr.Dispose();
            m_Optimizer.Dispose();
            m_Line.Dispose();
            m_Materials.Clear();
        }
        public PreviewLineRenderer GetSharedLineRenderer() => m_Line;

        public void SetActive(bool value) => IsActive = value;
        public void MarkSpineDirty()
        {
            m_SpineDirty = true;
        }
        // 网格只有在曲线(spine)发生变动时才重建，其他参数变化（如材质）无需重绘网格。
        public void MarkMeshDirty()
        {
            m_MeshDirty = true;
        }
        public void MarkMaterialsDirty() => m_MaterialsDirty = true;
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

                if (m_MatMgr == null)
                {
                    Debug.LogError("[PathPreviewManager] Material manager is null, cannot update preview");
                    return;
                }

                // 始终尝试更新材质管理器：内部 CalculateHash 会确保仅在参数变化时才重建材质，性能开销可忽略。
#if UNITY_EDITOR
                // 移除初始近邻选择，改为在下方基于道路包围盒选择目标 Terrain
                // 这里暂不设置 _matMgr 的目标 Terrain，稍后在 GPU 预览阶段统一处理。
#endif

                if (m_SpineDirty)
                {
                    try
                    {
                        LatestSpine = PathSampler.SamplePath(creator, heightProvider);
                        if (LatestSpine.HasValue && Generator != null)
                        {
                            Generator.StartMeshGeneration(LatestSpine.Value, creator.profile);
                        }
                        m_SpineDirty = false;
                        m_MeshDirty = false; // spine change implies mesh change
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PathPreviewManager] Error during spine sampling: {ex.Message}");
                        m_SpineDirty = false; // Prevent infinite retry
                    }
                }
                else if (m_MeshDirty)
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
                    m_MeshDirty = false;
                }
                // 移除仅因非曲线变化而触发的网格重建逻辑，避免频繁重绘。

                try
                {
                    if (Generator.TryFinalizeMesh() || (Generator.PreviewMesh?.vertexCount ?? 0) > 0 && Generator.ForceFinalizeMesh())
                    {
                        if (m_Mesh != Generator.PreviewMesh)
                        {
                            m_Mesh = Generator.PreviewMesh;
                            m_Bounds = m_Mesh ? m_Mesh.bounds : default;
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
                    m_MatMgr.SetPathLength(pathLen > 0f ? pathLen : 100f);

                    // 统一UV语义：网格UV已归一化到0..1，材质重复设为1
                    m_MatMgr.SetMeshRepeats(1f, 1f);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PathPreviewManager] Error during material setup: {ex.Message}");
                }

#if UNITY_EDITOR
                int boundsHashNow = 0;
                // 基于脊线包围盒选择目标 Terrain（优先相交，其次最近），并仅在变化时运行 GPU 预览
                UnityEngine.Terrain targetTerrain = m_TargetTerrain;
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
                            boundsHashNow = CalcBoundsHash(roadBounds);
                            var needRetarget = (targetTerrain == null) || (boundsHashNow != m_LastBoundsHash);
                            if (needRetarget)
                            {

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

                m_MatMgr.SetTargetTerrain(targetTerrain);
                // 更新缓存以供后续帧复用
                m_TargetTerrain = targetTerrain;
                m_LastBoundsHash = boundsHashNow;

                // —— 仅在发生变化时运行 GPU 预览 ——
                var profileHashNow = CalcProfileHash(creator.profile);
                var terrainIdNow = targetTerrain ? targetTerrain.GetInstanceID() : 0;
                var spineHashNow = LatestSpine.HasValue ? CalcSpineHash(LatestSpine.Value) : 0;
                var cacheHasRt = targetTerrain && MrPathV2.Editor.Terrain.GpuPreviewCache.TryGet(targetTerrain, out var cachedRt) && cachedRt;
                var shouldRunGpu = PreviewMaterialManager.EnableGpuPreview && targetTerrain && LatestSpine.HasValue && (
                    !cacheHasRt || terrainIdNow != m_LastGpuTerrainId || spineHashNow != m_LastSpineHash || profileHashNow != m_LastProfileHash);

#endif

#if UNITY_EDITOR
                // 在材质更新之前执行 GPU 预览，以便本帧材质能绑定到最新的权重 RT
                if (shouldRunGpu)
                {
                    try
                    {
                        var pts = LatestSpine.Value.Points;
                        if (pts == null || pts.Length < 2)
                        {
                            // 脊线无效，提前返回
                            goto SkipGpuRun;
                        }

                        // 构造层配置（根据 RoadRecipe + Terrain 实际层索引）
                        var layerConfigs = BuildLayerConfigs(targetTerrain, creator.profile.roadRecipe);
                        if (layerConfigs == null || layerConfigs.Length == 0)
                        {
                            // 没有有效图层，提前返回
                            goto SkipGpuRun;
                        }

                        var width = Mathf.Max(0.1f, creator.profile.roadWidth);
                        var result = __temp.MrPathV2.Editor.GPU.GpuTerrainPainterV2.Instance
                            .PaintPathAsync(targetTerrain, pts, width, layerConfigs, true)
                            .GetAwaiter().GetResult();

                        if (result != null && result.Success && result.RenderTexture)
                        {
                            MrPathV2.Editor.Terrain.GpuPreviewCache.Register(targetTerrain, result.RenderTexture);
                            m_LastGpuTerrainId = terrainIdNow;
                            m_LastSpineHash = spineHashNow;
                            m_LastProfileHash = profileHashNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PathPreviewManager] GPU 预览执行失败: {ex.Message}");
                    }
                }
            SkipGpuRun:;
#endif

                try
                {
                    // 更新材质并刷新缓存
                    if (m_MatMgr != null && creator?.profile != null)
                    {
                        m_MatMgr.Update(creator.profile, m_Template, m_Alpha);
                        if (m_MaterialsDirty)
                        {
                            RefreshMaterialCache();
                            m_MaterialsDirty = false;
                        }
                        else
                        {
                            // 如果内嵌 Mask 等资源变更导致材质实例被替换，也需要刷新缓存；通过检查引用变化实现。
                            var renderMaterials = m_MatMgr.GetRenderMaterials();
                            var currentMatCount = renderMaterials?.Count ?? 0;
                            if (currentMatCount != m_Materials.Count)
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
                m_LastProfileHash = CalcProfileHash(creator.profile);

                if (!creator.profile.showPreviewMesh || m_Mesh == null || m_Materials.Count == 0) return;

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
            if (cam == null || cam.GetInstanceID() != m_SceneCamId)
            {
                m_SceneCam = cam;
                m_SceneCamId = cam ? cam.GetInstanceID() : 0;
            }
            return m_SceneCam;
        }

        private void Render()
        {
            var cam = SceneCamera();
            if (cam == null) return;

            var dist = Vector3.Distance(cam.transform.position, m_Bounds.center);
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), m_Bounds) || dist > MaxRenderDistance) return;

            var count = Mathf.Min(GetLodMaterialCount(dist), m_Materials.Count);
            var matrix = Matrix4x4.identity;

            if (count > 1)
            {
                m_Optimizer.ClearBatches();
                m_Optimizer.SetGlobalProperty("_PreviewAlpha", m_Alpha);
                for (var i = 0; i < count; i++) m_Optimizer.AddRenderItem(m_Mesh, m_Materials[i], matrix);
                m_Optimizer.ExecuteBatchedRender(cam);
            }
            else
            {
                // 使用 MaterialPropertyBlock 而非全局 Shader 属性，避免因其他编辑器 UI 绘制修改全局状态导致闪烁。
                if (m_SingleMpb == null)
                    m_SingleMpb = new MaterialPropertyBlock();
                m_SingleMpb.SetFloat(PreviewAlpha, m_Alpha);
                Graphics.DrawMesh(m_Mesh, matrix, m_Materials[0], 0, cam, 0, m_SingleMpb);
            }
        }

        private int GetLodMaterialCount(float dist) => dist > LodThreshold ? Mathf.Max(1, m_Materials.Count / 2) : m_Materials.Count;

        private void RefreshMaterialCache()
        {
            try
            {
                m_Materials.Clear();
                var list = m_MatMgr?.GetRenderMaterials();
                if (list != null)
                {
                    m_Materials.AddRange(list);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PathPreviewManager] Error in RefreshMaterialCache: {ex.Message}\nStackTrace: {ex.StackTrace}");
                // Ensure materials list is in a valid state even if refresh fails
                if (m_Materials == null)
                {
                    m_Materials = new List<Material>();
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

        // 新增：稳定的包围盒哈希，用于判断道路边界是否变化
        private static int CalcBoundsHash(Bounds b)
        {
            unchecked
            {
                // 量化到0.5米精度，避免浮点微抖导致频繁变化
                int q = 2; // 1/q 米分辨率 -> 0.5m
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

#if UNITY_EDITOR
        // 将 RoadRecipe 映射为 GPU 渲染所需的 LayerConfig 数组（包含目标 Terrain 的 layer 索引）
        private static __temp.MrPathV2.Editor.GPU.LayerConfig[] BuildLayerConfigs(UnityEngine.Terrain terrain, StylizedRoadRecipe recipe)
        {
            if (!terrain || recipe == null) return Array.Empty<__temp.MrPathV2.Editor.GPU.LayerConfig>();
            var layers = recipe.GetLayers();
            if (layers == null || layers.Count == 0)
            {
                return Array.Empty<__temp.MrPathV2.Editor.GPU.LayerConfig>();
            }

            var resolved = __temp.MrPathV2.Editor.Terrain.LayerResolver.ResolveEnsurePresent(terrain, recipe);
            var list = new List<__temp.MrPathV2.Editor.GPU.LayerConfig>(layers.Count);
            var master = Mathf.Clamp01(recipe.masterOpacity);
            for (int i = 0; i < layers.Count; i++)
            {
                var rl = layers[i];
                if (rl == null || !rl.enabled) continue;
                var tl = rl.contentLayer;
                if (!tl) continue;
                if (!resolved.TryGetValue(tl, out var layerIndex)) continue;
                var strength = Mathf.Clamp01(rl.opacity * master);
                var blend = MapBlendMode(rl.blendMode);
                list.Add(new __temp.MrPathV2.Editor.GPU.LayerConfig(layerIndex, strength, blend));
            }

            return list.Count > 0 ? list.ToArray() : Array.Empty<__temp.MrPathV2.Editor.GPU.LayerConfig>();
        }

        private static __temp.MrPathV2.Editor.GPU.BlendMode MapBlendMode(__temp.MrPathV2.Runtime.Core.BlendMode mode)
        {
            switch (mode)
            {
                case __temp.MrPathV2.Runtime.Core.BlendMode.Add:
                case __temp.MrPathV2.Runtime.Core.BlendMode.Additive:
                    return __temp.MrPathV2.Editor.GPU.BlendMode.Add;
                case __temp.MrPathV2.Runtime.Core.BlendMode.Multiply:
                    return __temp.MrPathV2.Editor.GPU.BlendMode.Multiply;
                case __temp.MrPathV2.Runtime.Core.BlendMode.Overlay:
                    return __temp.MrPathV2.Editor.GPU.BlendMode.Overlay;
                case __temp.MrPathV2.Runtime.Core.BlendMode.Screen:
                    return __temp.MrPathV2.Editor.GPU.BlendMode.Add; // 近似替代
                case __temp.MrPathV2.Runtime.Core.BlendMode.Lerp:
                case __temp.MrPathV2.Runtime.Core.BlendMode.Normal:
                default:
                    return __temp.MrPathV2.Editor.GPU.BlendMode.Replace;
            }
        }
#endif
    }
}
