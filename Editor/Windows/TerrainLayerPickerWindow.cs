#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Editor.Stores;
using MrPathV2.Editor.Terrain;
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// 高性能、简洁的 TerrainLayer 选择器窗口。
    /// Tabs: 当前地形 / 全场景 / 最近收藏。
    /// 单一职责：仅负责选择和写入。采集逻辑在 TerrainLayerCollector，持久化在 TerrainLayerPickerStore。
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

        private Tab _activeTab = Tab.CurrentTerrain;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private TerrainLayerPickerStore _store;

        private List<TerrainLayer> _cacheCurrentTerrain = new List<TerrainLayer>();
        private List<TerrainLayer> _cacheAllScene = new List<TerrainLayer>();

        public static void Open(RoadLayer targetLayer, TerrainLayer currentValue = null)
        {
            s_TargetLayer = targetLayer;
            s_CurrentValue = currentValue;
            var win = GetWindow<TerrainLayerPickerWindow>(true, "选择 TerrainLayer", true);
            win.minSize = new Vector2(420, 320);
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
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSearch();
            EditorGUILayout.Space(6);
            DrawList();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                var tabs = new[] {"当前地形", "全场景", "最近/收藏"};
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
            switch (_activeTab)
            {
                case Tab.CurrentTerrain:
                    return _cacheCurrentTerrain;
                case Tab.AllScene:
                    return _cacheAllScene;
                case Tab.RecentAndFavorites:
                    var merged = new List<TerrainLayer>();
                    merged.AddRange(_store.Favorites);
                    foreach (var r in _store.Recent)
                    {
                        if (!merged.Contains(r)) merged.Add(r);
                    }
                    return merged;
                default:
                    return _cacheCurrentTerrain;
            }
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
                return p as Texture2D;
            }
            var p2 = AssetPreview.GetAssetPreview(layer) ?? AssetPreview.GetMiniThumbnail(layer);
            return p2 as Texture2D;
        }

        private void DrawItem(TerrainLayer layer)
        {
            if (!layer) return;
            var isFavorite = _store.IsFavorite(layer);
            var previewTex = GetLayerPreview(layer);

            EditorGUILayout.BeginHorizontal(GUILayout.Height(72));
            {
                if (previewTex)
                {
                    GUILayout.Label(new GUIContent(previewTex), GUILayout.Width(64), GUILayout.Height(64));
                }
                else
                {
                    var icon = AssetPreview.GetMiniThumbnail(layer);
                    GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                }

                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.LabelField(layer.name, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("定位", GUILayout.Width(48)))
                        {
                            EditorGUIUtility.PingObject(layer);
                        }

                        // 收藏切换
                        var starLabel = isFavorite ? "★" : "☆";
                        if (GUILayout.Button(starLabel, GUILayout.Width(28)))
                        {
                            _store.ToggleFavorite(layer);
                        }

                        // 选择
                        var isCurrent = s_CurrentValue && layer == s_CurrentValue;
                        var chooseLabel = isCurrent ? "已选择" : "选择";
                        EditorGUI.BeginDisabledGroup(isCurrent);
                        if (GUILayout.Button(chooseLabel, GUILayout.Width(60)))
                        {
                            ApplySelection(layer);
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
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
            // 在已加载的 StylizedRoadRecipe 中查找引用该实例的对象
            var recipes = Resources.FindObjectsOfTypeAll<StylizedRoadRecipe>();
            foreach (var r in recipes)
            {
                var list = r.layers;
                if (list == null) continue;
                // 引用比较：RoadLayer 是引用类型，可使用 ReferenceEquals
                if (list.Any(l => ReferenceEquals(l, layer))) return r;
            }
            return null;
        }
    }
}
#endif