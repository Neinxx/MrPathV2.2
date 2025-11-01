#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.Stores;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Windows
{
    /// <summary>
    /// 高性能、简洁的 TerrainLayer 选择器窗口。
    /// 扩展功能：标记道路覆盖地形持有的layer，智能分配按钮
    /// </summary>
    public class TerrainLayerPickerWindow : EditorWindow
    {
        private enum Tab
        {
            CurrentTerrain,
            AllScene,
            RecentAndFavorites
        }

        private static RoadLayer s_TargetLayer;
        private static TerrainLayer s_CurrentValue;
        private static PathCreator s_ContextPathCreator; // 新增：上下文PathCreator

        private Tab _activeTab = Tab.CurrentTerrain;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private TerrainLayerPickerStore _store;

        private List<TerrainLayer> _cacheCurrentTerrain = new List<TerrainLayer>();
        private List<TerrainLayer> _cacheAllScene = new List<TerrainLayer>();
        private readonly HashSet<TerrainLayer> _roadCoveredLayers = new HashSet<TerrainLayer>(); // 新增：道路覆盖地形持有的layers
        private TerrainLayer _selectedLayerForAssign; // 新增：选中的待分配layer

        public static void Open(RoadLayer targetLayer, TerrainLayer currentValue = null, PathCreator contextPathCreator = null)
        {
            s_TargetLayer = targetLayer;
            s_CurrentValue = currentValue;
            s_ContextPathCreator = contextPathCreator;
            var win = GetWindow<TerrainLayerPickerWindow>(true, "选择 TerrainLayer", true);
            win.minSize = new Vector2(420, 380); // 增加高度以容纳新按钮
            win.Show();
        }

        private void OnEnable()
        {
            _store = TerrainLayerPickerStore.GetOrCreate();
            RefreshCaches();
        }

        private void RefreshCaches()
        {
            _cacheCurrentTerrain = TerrainLayerCollector.GetLayersFromCurrentTerrain();
            _cacheAllScene = TerrainLayerCollector.GetLayersFromAllActiveTerrains();
            RefreshRoadCoveredLayers();
        }

        /// <summary>
        /// 刷新道路覆盖地形持有的layers
        /// </summary>
        private void RefreshRoadCoveredLayers()
        {
            _roadCoveredLayers.Clear();

            if (!s_ContextPathCreator) return;

            try
            {
                AddLayersFromCoveredTerrains();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RefreshRoadCoveredLayers] {ex.Message}");
            }
        }

        /// <summary>
        /// 从覆盖的地形中提取图层并添加到集合中
        /// </summary>
        private void AddLayersFromCoveredTerrains()
        {
            var coveredTerrains = GetCoveredTerrains(s_ContextPathCreator);
            foreach (var terrain in coveredTerrains)
            {
                AddTerrainLayers(terrain);
            }
        }

        /// <summary>
        /// 从单个地形中添加图层
        /// </summary>
        private void AddTerrainLayers(UnityEngine.Terrain terrain)
        {
            if (!HasValidTerrainLayers(terrain)) return;

            foreach (var layer in terrain.terrainData.terrainLayers)
            {
                if (layer) _roadCoveredLayers.Add(layer);
            }
        }

        /// <summary>
        /// 检查地形是否有有效的图层数据
        /// </summary>
        private static bool HasValidTerrainLayers(UnityEngine.Terrain terrain)
        {
            return terrain?.terrainData?.terrainLayers is { Length: > 0 };
        }

        /// <summary>
        /// 获取PathCreator覆盖的地形
        /// </summary>
        private static List<UnityEngine.Terrain> GetCoveredTerrains(PathCreator creator)
        {
            var result = new List<UnityEngine.Terrain>();

            if (!creator || !creator.profile || creator.pathData == null || creator.pathData.KnotCount < 2)
                return result;

            try
            {
                // 采样路径脊线
                var heightProvider = new Runtime.Providers.TerrainHeightProvider();

                var spine = PathSampler.SamplePath(creator, heightProvider);


                // 计算扩展边界
                var bounds = GetExpandedXZBounds(spine, creator.profile);

                // 查找相交地形
                var terrains = UnityEngine.Terrain.activeTerrains;
                foreach (var terrain in terrains)
                {
                    if (!terrain?.terrainData) continue;

                    var terrainBounds = GetTerrainBounds(terrain);
                    if (BoundsOverlap(bounds, terrainBounds))
                    {
                        result.Add(terrain);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetCoveredTerrains] {ex.Message}");
            }

            return result;
        }

        private static Vector4 GetExpandedXZBounds(PathSpine spine, PathProfile profile)
        {
            if (spine.VertexCount == 0)
                return new Vector4(0, 0, 0, 0);

            var halfWidth = (profile.roadWidth * 0.5f) + profile.falloffWidth;
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < spine.VertexCount; i++)
            {
                var point = spine.Points[i];
                minX = Mathf.Min(minX, point.x - halfWidth);
                minZ = Mathf.Min(minZ, point.z - halfWidth);
                maxX = Mathf.Max(maxX, point.x + halfWidth);
                maxZ = Mathf.Max(maxZ, point.z + halfWidth);
            }

            return new Vector4(minX, minZ, maxX, maxZ);
        }

        private static Vector4 GetTerrainBounds(UnityEngine.Terrain terrain)
        {
            var pos = terrain.transform.position;
            var size = terrain.terrainData.size;
            return new Vector4(pos.x, pos.z, pos.x + size.x, pos.z + size.z);
        }

        private static bool BoundsOverlap(Vector4 a, Vector4 b)
        {
            return !(a.z <= b.x || a.x >= b.z || a.w <= b.y || a.y >= b.w);
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSearch();
            EditorGUILayout.Space(6);
            DrawSmartAssignSection(); // 新增：智能分配区域
            DrawList();
        }

        /// <summary>
        /// 绘制智能分配区域
        /// </summary>
        private void DrawSmartAssignSection()
        {
            if (!s_ContextPathCreator) return;

            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("智能分配", EditorStyles.boldLabel);

                // 显示道路覆盖地形数量
                var coveredCount = GetCoveredTerrains(s_ContextPathCreator).Count;
                EditorGUILayout.LabelField($"道路覆盖地形: {coveredCount} 个", EditorStyles.miniLabel);

                // 智能分配按钮 - 仅当选中非地形持有layer时显示
                if (_selectedLayerForAssign && !_roadCoveredLayers.Contains(_selectedLayerForAssign))
                {
                    var buttonText = $"智能分配 '{_selectedLayerForAssign.name}' 到覆盖地形";
                    if (GUILayout.Button(buttonText, GUILayout.Height(24)))
                    {
                        ExecuteSmartAssign(_selectedLayerForAssign);
                    }
                }
                else if (_selectedLayerForAssign && _roadCoveredLayers.Contains(_selectedLayerForAssign))
                {
                    EditorGUILayout.HelpBox($"'{_selectedLayerForAssign.name}' 已存在于道路覆盖地形中", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// 执行智能分配
        /// </summary>
        private void ExecuteSmartAssign(TerrainLayer layer)
        {
            if (!layer || !s_ContextPathCreator?.profile?.roadRecipe) return;

            try
            {
                var coveredTerrains = GetCoveredTerrains(s_ContextPathCreator);
                if (coveredTerrains.Count == 0)
                {
                    EditorUtility.DisplayDialog("智能分配", "未检测到道路覆盖的地形", "确定");
                    return;
                }

                var assignedCount = 0;
                foreach (var terrain in coveredTerrains)
                {
                    if (!s_ContextPathCreator) continue;
                    var result = LayerResolver.ResolveEnsurePresentSmart(terrain, s_ContextPathCreator.profile.roadRecipe);
                    if (result.Count > 0) assignedCount++;
                }

                // 刷新缓存
                RefreshCaches();

                var message = $"智能分配完成！\n处理了 {coveredTerrains.Count} 个地形\n成功分配 {assignedCount} 个地形的图层映射";
                EditorUtility.DisplayDialog("智能分配", message, "确定");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("智能分配失败", ex.Message, "确定");
                Debug.LogError($"[SmartAssign] {ex}");
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                var tabs = new[]
                {
                    "当前地形", "全场景", "最近/收藏"
                };
                var newTab = GUILayout.Toolbar((int)_activeTab, tabs, EditorStyles.toolbarButton);
                if (newTab != (int)_activeTab)
                {
                    _activeTab = (Tab)newTab;
                    _scroll = Vector2.zero; // 切换时重置滚动
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    RefreshCaches();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearch()
        {
            EditorGUI.BeginChangeCheck();
            _search = EditorGUILayout.TextField("搜索", _search);
            if (EditorGUI.EndChangeCheck())
            {
                // 无需额外处理，列表渲染时按需过滤
            }
        }

        private void DrawList()
        {
            var list = GetCurrentList();
            if (list == null || list.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何 TerrainLayer。", MessageType.Info);
                return; // 早退
            }

            var filtered = Filter(list, _search);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var layer in filtered)
            {
                DrawItem(layer);
            }
            EditorGUILayout.EndScrollView();
        }

        private List<TerrainLayer> GetCurrentList()
        {
            List<TerrainLayer> baseList;
            switch (_activeTab)
            {
                case Tab.CurrentTerrain:
                    baseList = _cacheCurrentTerrain;
                    break;
                case Tab.AllScene:
                    baseList = _cacheAllScene;
                    break;
                case Tab.RecentAndFavorites:
                    var merged = new List<TerrainLayer>();
                    merged.AddRange(_store.Favorites);
                    foreach (var r in _store.Recent)
                    {
                        if (!merged.Contains(r)) merged.Add(r);
                    }
                    baseList = merged;
                    break;
                default:
                    baseList = _cacheCurrentTerrain;
                    break;
            }

            // 优先显示道路覆盖地形持有的layers
            if (_roadCoveredLayers.Count <= 0) return baseList;
            var priorityLayers = baseList.Where(l => _roadCoveredLayers.Contains(l)).ToList();
            var otherLayers = baseList.Where(l => !_roadCoveredLayers.Contains(l)).ToList();

            var result = new List<TerrainLayer>();
            result.AddRange(priorityLayers);
            result.AddRange(otherLayers);
            return result;

        }

        private static List<TerrainLayer> Filter(List<TerrainLayer> source, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return source;
            search = search.Trim();
            return source.Where(l => l && l.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private static Texture2D GetLayerPreview(TerrainLayer layer)
        {
            if (!layer) return null;
            var tex = layer.diffuseTexture;
            if (tex)
            {
                var p = AssetPreview.GetAssetPreview(tex) ?? AssetPreview.GetMiniThumbnail(tex);
                return p;
            }
            var p2 = AssetPreview.GetAssetPreview(layer) ?? AssetPreview.GetMiniThumbnail(layer);
            return p2;
        }

        private void DrawItem(TerrainLayer layer)
        {
            if (!layer) return;

            var isFavorite = _store.IsFavorite(layer);
            var isRoadCovered = _roadCoveredLayers.Contains(layer);
            var previewTex = GetLayerPreview(layer);

            ApplyRoadCoveredBackground(isRoadCovered);

            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(72));
            {
                DrawPreviewImage(layer, previewTex);
                DrawItemContent(layer, isFavorite, isRoadCovered);
            }
            EditorGUILayout.EndHorizontal();

            ResetBackgroundColor();
        }

        private static void ApplyRoadCoveredBackground(bool isRoadCovered)
        {
            if (isRoadCovered)
            {
                GUI.backgroundColor = new Color(0.8f, 1f, 0.8f, 1f); // 淡绿色背景
            }
        }

        private static void ResetBackgroundColor()
        {
            GUI.backgroundColor = Color.white; // 恢复背景色
        }

        private static void DrawPreviewImage(TerrainLayer layer, Texture2D previewTex)
        {
            if (previewTex)
            {
                GUILayout.Label(new GUIContent(previewTex), GUILayout.Width(64), GUILayout.Height(64));
                return;
            }

            var icon = AssetPreview.GetMiniThumbnail(layer);
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
        }

        private void DrawItemContent(TerrainLayer layer, bool isFavorite, bool isRoadCovered)
        {
            EditorGUILayout.BeginVertical();
            {
                DrawLayerName(layer, isRoadCovered);
                DrawActionButtons(layer, isFavorite, isRoadCovered);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawLayerName(TerrainLayer layer, bool isRoadCovered)
        {
            var displayName = isRoadCovered ? $"🛣️ {layer.name}" : layer.name;
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        }

        private void DrawActionButtons(TerrainLayer layer, bool isFavorite, bool isRoadCovered)
        {
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("定位", GUILayout.Width(48)))
                {
                    EditorGUIUtility.PingObject(layer);
                }

                DrawFavoriteToggle(layer, isFavorite);
                DrawSelectButton(layer);
                DrawSmartAssignButton(layer, isRoadCovered);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFavoriteToggle(TerrainLayer layer, bool isFavorite)
        {
            var starLabel = isFavorite ? "★" : "☆";
            if (GUILayout.Button(starLabel, GUILayout.Width(28)))
            {
                _store.ToggleFavorite(layer);
            }
        }

        private void DrawSelectButton(TerrainLayer layer)
        {
            var isCurrent = s_CurrentValue && layer == s_CurrentValue;
            var chooseLabel = isCurrent ? "已选择" : "选择";

            // 允许点击当前项的“已选择”按钮以重新确认选择（修复灰色不可点击问题）
            if (GUILayout.Button(chooseLabel, GUILayout.Width(60)))
            {
                _selectedLayerForAssign = layer;
                ApplySelection(layer);
            }
        }

        private void DrawSmartAssignButton(TerrainLayer layer, bool isRoadCovered)
        {
            if (isRoadCovered || !s_ContextPathCreator) return;

            if (GUILayout.Button("分配", GUILayout.Width(40)))
            {
                _selectedLayerForAssign = layer;
                ExecuteSmartAssign(layer);
            }
        }

        private void ApplySelection(TerrainLayer layer)
        {
            if (!layer) return; // 早退
            if (s_TargetLayer == null)
            {
                Close();
                return; // 无目标，直接退出
            }

            // 写入目标
            s_TargetLayer.contentLayer = layer;

            // 查找所属 Recipe 并标记脏 + 记录撤销
            var recipe = FindOwnerRecipe(s_TargetLayer);
            if (recipe)
            {
                Undo.RecordObject(recipe, "Set RoadLayer ContentLayer");
                EditorUtility.SetDirty(recipe);
            }
            else
            {
                // 回退：尝试标记当前选择对象
                if (Selection.activeObject)
                {
                    EditorUtility.SetDirty(Selection.activeObject);
                }
            }

            // 存储最近
            _store.AddRecent(layer);

            Close();
        }

        private static StylizedRoadRecipe FindOwnerRecipe(RoadLayer layer)
        {
            if (layer == null) return null;

            // 使用缓存字典优化查找性能
            return RecipeLayerCache.GetRecipeForLayer(layer);
        }

        /// <summary>
        /// 配方和图层的缓存类，用于优化查找性能
        /// </summary>
        private static class RecipeLayerCache
        {
            // 缓存配方与图层的映射关系
            private static readonly Dictionary<RoadLayer, StylizedRoadRecipe> SLayerToRecipeMap = new Dictionary<RoadLayer, StylizedRoadRecipe>();
            // 缓存所有已加载的配方
            private static StylizedRoadRecipe[] s_CachedRecipes = Array.Empty<StylizedRoadRecipe>();
            // 上次刷新缓存的时间
            private static float s_LastCacheRefreshTime;
            // 缓存有效期（秒）
            private const float CacheExpireTime = 5f;

            /// <summary>
            /// 根据图层获取对应的配方
            /// </summary>
            public static StylizedRoadRecipe GetRecipeForLayer(RoadLayer layer)
            {
                // 检查是否需要刷新缓存
                if (ShouldRefreshCache())
                {
                    RefreshCache();
                }

                // 尝试从缓存中获取
                if (SLayerToRecipeMap.TryGetValue(layer, out var recipe))
                {
                    return recipe;
                }

                // 如果缓存中没有找到，进行一次性的查找
                return FindRecipeForLayerUncached(layer);
            }

            /// <summary>
            /// 检查是否需要刷新缓存
            /// </summary>
            private static bool ShouldRefreshCache()
            {
                return Time.realtimeSinceStartup - s_LastCacheRefreshTime > CacheExpireTime ||
                       s_CachedRecipes.Length == 0;
            }

            /// <summary>
            /// 刷新缓存
            /// </summary>
            private static void RefreshCache()
            {
                // 获取所有配方
                s_CachedRecipes = Resources.FindObjectsOfTypeAll<StylizedRoadRecipe>();

                // 清空旧的映射关系
                SLayerToRecipeMap.Clear();

                // 重建映射关系
                foreach (var recipe in s_CachedRecipes)
                {
                    if (recipe.layers == null) continue;

                    foreach (var layer in recipe.layers.Where(layer => layer != null))
                    {
                        SLayerToRecipeMap.TryAdd(layer, recipe);
                    }
                }

                s_LastCacheRefreshTime = Time.realtimeSinceStartup;
            }

            /// <summary>
            /// 未缓存的查找方式（备用方案）
            /// </summary>
            private static StylizedRoadRecipe FindRecipeForLayerUncached(RoadLayer layer)
            {
                var recipes = Resources.FindObjectsOfTypeAll<StylizedRoadRecipe>();
                foreach (var recipe in recipes)
                {
                    if (recipe.layers == null) continue;

                    if (recipe.layers.Any(l => ReferenceEquals(l, layer)))
                    {
                        return recipe;
                    }
                }
                return null;
            }
        }
    }
}
#endif
