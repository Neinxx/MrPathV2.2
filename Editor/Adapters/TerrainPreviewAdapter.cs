#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.TerrainUtils;

namespace MrPathV2.EditorAdapters
{
    /// <summary>
    /// 在编辑器中实时检测当前选择的 Terrain，并为 <see cref="StylizedRoadRecipe"/> 与 <see cref="PathProfile"/> 生成匹配地形控制图分辨率的预览 RenderTexture。
    /// 
    /// 该适配器为 <see cref="RoadPreviewRenderPipeline"/> 的薄包装：
    /// 1. 监听 <see cref="UnityEditor.Selection"/> 变化以及地形数据（alphamap 分辨率 / 图层数量）变化；
    /// 2. 当检测到变化或显式请求时，调用渲染管线生成新的 PreviewRT；
    /// 3. 通过 <see cref="PreviewUpdated"/> 事件对外通知更新；
    /// 4. 统一管理 RT 生命周期，进入 PlayMode / 脚本刷新时自动释放资源。
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainPreviewAdapter
    {
        public static event Action<RenderTexture> PreviewUpdated;

        public static Terrain ActiveTerrain { get; private set; }
        public static RenderTexture PreviewRT => _previewRT;

        private static RenderTexture _previewRT;

        private static StylizedRoadRecipe _currentRecipe;
        private static PathProfile _currentProfile;

        // 缓存地形状态用于检测变化
        private static int _cachedAlphamapResolution;
        private static int _cachedLayerCount;
        private static double _lastUpdateTime;
        private const double MinUpdateInterval = 0.5; // 最多每 0.5 秒刷新一次

        static TerrainPreviewAdapter()
        {
            // 注册编辑器回调
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        /// <summary>
        /// 手动设置上下文，通常在 Inspector 绘制时调用。
        /// </summary>
        public static void SetContext(Terrain terrain, PathProfile profile, StylizedRoadRecipe recipe)
        {
            ActiveTerrain = terrain;
            _currentProfile = profile;
            _currentRecipe = recipe;
            ForceUpdate();
        }

        /// <summary>
        /// 主动触发一次立即更新。
        /// </summary>
        public static void ForceUpdate()
        {
            UpdatePreviewInternal(true);
        }

        private static void OnSelectionChanged()
        {
            // 若选中对象中存在 Terrain，则切换到该 Terrain
            var terrain = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Terrain>() : null;
            if (terrain != null)
            {
                ActiveTerrain = terrain;
                // 不立即刷新，等待下一帧 update
            }
        }

        private static void OnEditorUpdate()
        {
            UpdatePreviewInternal(false);
        }

        private static void UpdatePreviewInternal(bool force)
        {
            if (ActiveTerrain == null || _currentRecipe == null || _currentProfile == null)
                return;

            var td = ActiveTerrain.terrainData;
            if (td == null) return;

            int res = td.alphamapResolution;
            int layerCount = td.terrainLayers != null ? td.terrainLayers.Length : 0;

            bool needUpdate = force ||
                              res != _cachedAlphamapResolution ||
                              layerCount != _cachedLayerCount ||
                              (EditorApplication.timeSinceStartup - _lastUpdateTime) > MinUpdateInterval;

            if (!needUpdate) return;

            _cachedAlphamapResolution = res;
            _cachedLayerCount = layerCount;
            _lastUpdateTime = EditorApplication.timeSinceStartup;

            _previewRT = MrPathV2.RoadPreviewRenderPipeline.GeneratePreviewRT(
                _previewRT,
                td,
                _currentProfile,
                _currentRecipe);

            PreviewUpdated?.Invoke(_previewRT);
        }

        private static void Cleanup()
        {
            if (_previewRT != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(_previewRT);
                else
                    UnityEngine.Object.DestroyImmediate(_previewRT);
                _previewRT = null;
            }
        }
    }
}
#endif