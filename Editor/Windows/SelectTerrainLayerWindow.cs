#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Providers;
using UnityEditor;
using UnityEngine;
using MrPathV2.Editor.Preview;
using __temp.MrPathV2.Editor.Inspectors; // 用于通知刷新

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// SelectTerrainLayer：简化为 ObjectPicker 风格的选择窗口。
    /// - 单一职责：选择并应用 TerrainLayer 到 RoadLayer。
    /// - 提前返回：所有空引用与异常情况快速退出。
    /// - 实时预览：选择变更时刷新 PathCreator 的预览材质。
    /// - 视图：统一 Assets 列表，优先展示覆盖地形已持有的图层并以绿色标识。
    /// </summary>
    public class SelectTerrainLayerWindow : EditorWindow
    {
        private RoadLayer _targetRoadLayer;
        private TerrainLayer _originalLayer;
        private PathCreator _contextPathCreator;

        private readonly List<TerrainLayer> _assetLayers = new();
        private readonly List<TerrainLayer> _coveredLayers = new();

        private string _search = string.Empty;
        private Vector2 _scroll;
        private TerrainLayer _selected;
        private bool _applied;
        private int _thumbSize = 72; // 缩略图尺寸（可调）
        private const int ThumbMin = 48;
        private const int ThumbMax = 128;
        private const int TilePadding = 8;

        // 覆盖统计与筛选模式
        private int _coveredTerrainTotal = 0;
        private readonly Dictionary<int, int> _coverageStats = new();
        private enum FilterMode { All, HeldOnly, MissingOnly }
        private FilterMode _filterMode = FilterMode.All;

        // 单一视图，不再使用 Covered 标签页

        public static void Open(RoadLayer roadLayer, TerrainLayer current, PathCreator contextPathCreator)
        {
            var win = GetWindow<SelectTerrainLayerWindow>(true, "Select Terrain Layer", true);
            win.minSize = new Vector2(420, 320);
            win.Initialize(roadLayer, current, contextPathCreator);
            win.Show();
        }

        private void Initialize(RoadLayer roadLayer, TerrainLayer current, PathCreator context)
        {
            _targetRoadLayer = roadLayer;
            _originalLayer = current;
            _contextPathCreator = context;

            // 收集图层列表（资产与覆盖地形）
            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());

            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers(_contextPathCreator));

            // 初始化覆盖统计
            RebuildCoverageStats();

            // 设置初始选中并进行一次预览赋值
            _selected = current; // 保持当前选择
            PreviewAssign(_selected);
        }

        private void OnGUI()
        {
            HandleShortcuts();
            DrawToolbar();
            DrawSearchBar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            // 使用栅格缩略图布局，点击缩略图直接预览（统一视图，优先展示持有图层）
            DrawGridPrioritized();
            EditorGUILayout.EndScrollView();

            // 底部详情面板：显示选中图层的详细信息
            DrawDetailsPanel();

            DrawFooterActions();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Assets（已持有优先，绿色标识）", EditorStyles.miniLabel);
            GUILayout.Space(8);
            // 筛选开关：全部/仅已持有/仅未持有
            var newFilterIndex = GUILayout.Toolbar((int)_filterMode, new[] { "全部", "仅已持有", "仅未持有" }, EditorStyles.toolbarButton, GUILayout.Width(220));
            var newFilter = (FilterMode)newFilterIndex;
            if (newFilter != _filterMode)
            {
                _filterMode = newFilter;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshLists();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearchBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("筛选:", GUILayout.Width(40));
                _search = EditorGUILayout.TextField(_search);
                GUILayout.FlexibleSpace();
            }
        }
        private void DrawGrid(List<TerrainLayer> list)
        {
            if (list == null || list.Count == 0)
            {
                var msg = "未检测到可展示的 TerrainLayer（项目或覆盖地形为空）。";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return; // 提前返回
            }

            var filtered = Filter(list, _search).ToList();

            // 预置一个“None”项
            DrawNoneTile();

            float viewWidth = position.width - 20f; // 预留滚动条/边距
            int tileWidth = _thumbSize + TilePadding;
            int cols = Mathf.Max(1, Mathf.FloorToInt(viewWidth / tileWidth));

            int i = 0;
            while (i < filtered.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < cols && i < filtered.Count; c++, i++)
                {
                    DrawTile(filtered[i]);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // 统一视图的优先栅格绘制：覆盖地形已持有的图层靠前
        private void DrawGridPrioritized()
        {
            // 若资产列表为空，提示并返回
            if (_assetLayers == null || _assetLayers.Count == 0)
            {
                EditorGUILayout.HelpBox("项目中未发现 TerrainLayer 资源。", MessageType.Info);
                return; // 提前返回
            }

            var prioritized = PrioritizeLayers(_assetLayers, _coveredLayers);
            var filtered = Filter(prioritized, _search).ToList();
            // 应用筛选模式
            if (_filterMode == FilterMode.HeldOnly)
                filtered = filtered.Where(IsLayerHeldByCoveredTerrains).ToList();
            else if (_filterMode == FilterMode.MissingOnly)
                filtered = filtered.Where(l => !IsLayerHeldByCoveredTerrains(l)).ToList();
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("筛选条件下无匹配图层。", MessageType.Info);
                return; // 提前返回
            }

            // 预置一个“None”项
            DrawNoneTile();

            float viewWidth = position.width - 20f; // 预留滚动条/边距
            int tileWidth = _thumbSize + TilePadding;
            int cols = Mathf.Max(1, Mathf.FloorToInt(viewWidth / tileWidth));

            int i = 0;
            while (i < filtered.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < cols && i < filtered.Count; c++, i++)
                {
                    DrawTile(filtered[i]);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private List<TerrainLayer> PrioritizeLayers(List<TerrainLayer> assets, List<TerrainLayer> covered)
        {
            var result = new List<TerrainLayer>();
            if (assets == null || assets.Count == 0) return result; // 提前返回

            // 覆盖排序：绿色(全持有) > 黄色(部分持有) > 默认(未持有)
            int total = _coveredTerrainTotal;
            if (total <= 0)
            {
                // 无覆盖地形上下文，按名称返回
                return assets.Where(a => a).OrderBy(a => a.name).ToList();
            }

            var full = new List<TerrainLayer>();
            var partial = new List<TerrainLayer>();
            var none = new List<TerrainLayer>();

            foreach (var a in assets)
            {
                if (!a) continue;
                int cov = GetCoverageCount(a);
                if (cov >= total)
                {
                    full.Add(a);
                }
                else if (cov > 0)
                {
                    partial.Add(a);
                }
                else
                {
                    none.Add(a);
                }
            }

            result.AddRange(full.OrderBy(x => x.name));
            result.AddRange(partial.OrderBy(x => x.name));
            result.AddRange(none.OrderBy(x => x.name));
            return result;
        }

        private void DrawNoneTile()
        {
            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.BeginVertical(GUILayout.Width(_thumbSize + TilePadding));
            var rect = GUILayoutUtility.GetRect(_thumbSize, _thumbSize, GUILayout.Width(_thumbSize), GUILayout.Height(_thumbSize));
            if (GUI.Button(rect, new GUIContent("None")))
            {
                if (_selected != null)
                {
                    _selected = null;
                    PreviewAssign(null);
                }
            }
            GUILayout.Label("None", nameStyle, GUILayout.Width(_thumbSize));
            EditorGUILayout.EndVertical();
        }

        private void DrawTile(TerrainLayer tl)
        {
            if (!tl) return; // 提前返回

            var preview = AssetPreview.GetAssetPreview(tl) as Texture2D ?? AssetPreview.GetMiniThumbnail(tl) as Texture2D;
            EditorGUILayout.BeginVertical(GUILayout.Width(_thumbSize + TilePadding));

            var rect = GUILayoutUtility.GetRect(_thumbSize, _thumbSize, GUILayout.Width(_thumbSize), GUILayout.Height(_thumbSize));
            // 现代卡片背景与悬停/选中高亮
            bool isHover = rect.Contains(Event.current.mousePosition);
            bool isSelected = _selected == tl;
            var bgColor = isSelected ? new Color(0.2f, 0.6f, 0.2f, 0.15f) : (isHover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.04f));
            var bgRect = new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f);
            EditorGUI.DrawRect(bgRect, bgColor);
            if (isSelected)
            {
                var border = new Color(0.2f, 0.8f, 0.2f, 0.8f);
                EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, bgRect.width, 1f), border);
                EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.yMax - 1f, bgRect.width, 1f), border);
                EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, 1f, bgRect.height), border);
                EditorGUI.DrawRect(new Rect(bgRect.xMax - 1f, bgRect.y, 1f, bgRect.height), border);
            }
            // 双击检测优先（避免与按钮冲突）
            var e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                if (e.clickCount == 2)
                {
                    HandleDoubleClickOnTile(tl);
                    e.Use();
                }
            }
            if (preview)
            {
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _selected = tl;
                PreviewAssign(tl);
            }

            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            // 名称加粗，并按持有比例着色：全持有=绿色，部分持有=黄色
            nameStyle.fontStyle = FontStyle.Bold;
            int cov = GetCoverageCount(tl);
            int total = _coveredTerrainTotal;
            if (total > 0)
            {
                if (cov >= total)
                {
                    // 全部持有
                    var col = new Color(0.2f, 0.8f, 0.2f);
                    nameStyle.normal.textColor = col;
                    nameStyle.hover.textColor = col;
                }
                else if (cov > 0)
                {
                    // 部分持有
                    var col = new Color(0.95f, 0.75f, 0.15f);
                    nameStyle.normal.textColor = col;
                    nameStyle.hover.textColor = col;
                }
                // 未持有保持默认颜色（更轻量、更优雅）
            }
            GUILayout.Label(tl.name, nameStyle, GUILayout.Width(_thumbSize));
            EditorGUILayout.EndVertical();
        }

        // 底部详情面板：显示选中图层信息（缩略图、名称、覆盖数、首个地形名可点击）
        private void DrawDetailsPanel()
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            var tl = _selected;
            if (!tl)
            {
                GUILayout.Label("未选择图层", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return; // 提前返回
            }

            var previewTex = AssetPreview.GetAssetPreview(tl) ?? AssetPreview.GetMiniThumbnail(tl);
            EditorGUILayout.BeginHorizontal();
            // 左侧预览
            var pRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
            if (previewTex) GUI.DrawTexture(pRect, previewTex, ScaleMode.ScaleToFit);

            // 右侧详情
            EditorGUILayout.BeginVertical();
            var nameStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            int cov = GetCoverageCount(tl);
            int total = _coveredTerrainTotal;
            if (total > 0)
            {
                if (cov >= total)
                {
                    var col = new Color(0.2f, 0.8f, 0.2f);
                    nameStyle.normal.textColor = col;
                }
                else if (cov > 0)
                {
                    var col = new Color(0.95f, 0.75f, 0.15f);
                    nameStyle.normal.textColor = col;
                }
            }
            GUILayout.Label(tl.name, nameStyle);
            GUILayout.Space(2);
            GUILayout.Label($"覆盖数：{cov}/{total}", EditorStyles.miniLabel);

            var holders = GetTerrainHoldersForLayer(tl);
            if (holders.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("持有它的地形：", EditorStyles.miniLabel, GUILayout.Width(96));
                var firstName = holders[0].name;
                var linkStyle = EditorStyles.linkLabel ?? EditorStyles.miniLabel;
                if (GUILayout.Button(firstName, linkStyle))
                {
                    if (holders.Count == 1)
                    {
                        LocateTerrain(holders[0]);
                    }
                    else
                    {
                        var linkRect = GUILayoutUtility.GetLastRect();
                        PopupWindow.Show(linkRect, new TerrainListPopup(holders, LocateTerrain));
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("持有它的地形：无", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private List<UnityEngine.Terrain> GetTerrainHoldersForLayer(TerrainLayer tl)
        {
            var result = new List<UnityEngine.Terrain>();
            if (!tl) return result; // 提前返回
            var terrains = GetCoveredTerrains(_contextPathCreator);
            if (terrains == null || terrains.Count == 0) return result; // 提前返回
            foreach (var t in terrains)
            {
                if (!t || !t.terrainData) continue;
                var layers = t.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
                if (layers.Any(l => l == tl)) result.Add(t);
            }
            return result;
        }

        private void LocateTerrain(UnityEngine.Terrain terrain)
        {
            if (!terrain) return; // 提前返回
            Selection.activeGameObject = terrain.gameObject;
            EditorGUIUtility.PingObject(terrain.gameObject);
            var sv = SceneView.lastActiveSceneView;
            if (sv) sv.FrameSelected();
        }

        private class TerrainListPopup : PopupWindowContent
        {
            private readonly List<UnityEngine.Terrain> _terrains;
            private readonly Action<UnityEngine.Terrain> _onChoose;
            private Vector2 _scroll;

            public TerrainListPopup(List<UnityEngine.Terrain> terrains, Action<UnityEngine.Terrain> onChoose)
            {
                _terrains = terrains ?? new List<UnityEngine.Terrain>();
                _onChoose = onChoose;
            }

            public override Vector2 GetWindowSize()
            {
                int rows = Mathf.Max(1, _terrains.Count);
                float height = Mathf.Min(300f, rows * 24f + 8f);
                return new Vector2(240f, height);
            }

            public override void OnGUI(Rect rect)
            {
                GUILayout.Label("持有它的地形", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (var t in _terrains)
                {
                    if (!t) continue;
                    var rowRect = GUILayoutUtility.GetRect(1f, 26f, GUILayout.ExpandWidth(true));
                    bool hover = rowRect.Contains(Event.current.mousePosition);
                    var bg = hover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.04f);
                    EditorGUI.DrawRect(rowRect, bg);

                    // 图标
                    Texture icon = AssetPreview.GetMiniThumbnail(t) ?? EditorGUIUtility.ObjectContent(t, typeof(UnityEngine.Terrain)).image;
                    var iconRect = new Rect(rowRect.x + 6f, rowRect.y + 3f, 20f, 20f);
                    if (icon) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

                    // 名称（链接风格）
                    var labelRect = new Rect(iconRect.xMax + 6f, rowRect.y + 4f, rowRect.width - (iconRect.width + 24f), 18f);
                    var linkStyle = EditorStyles.linkLabel ?? EditorStyles.miniLabel;
                    EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
                    GUI.Label(labelRect, t.name, linkStyle);

                    if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                    {
                        _onChoose?.Invoke(t);
                        editorWindow.Close();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void HandleDoubleClickOnTile(TerrainLayer tl)
        {
            if (!tl) return; // 提前返回
            // 智能双击：若覆盖地形全部已持有，直接选中并关闭；否则仅为缺失地形添加，先确认
            var terrains = GetCoveredTerrains(_contextPathCreator);
            int total = terrains?.Count ?? 0;
            int coveredCount = 0;
            if (total > 0)
            {
                foreach (var t in terrains)
                {
                    if (!t || !t.terrainData) continue;
                    var layers = t.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
                    if (layers.Any(l => l == tl)) coveredCount++;
                }
            }
            int missingCount = Mathf.Max(0, total - coveredCount);

            if (missingCount == 0)
            {
                // 全部已持有：直接应用选择
                ApplySelection(tl);
                return;
            }

            // 部分或全部缺失：确认是否补齐缺失地形
            var ok = EditorUtility.DisplayDialog(
                "添加地形图层",
                $"该图层在覆盖地形中的持有情况：{coveredCount}/{total}\n是否为缺失的 {missingCount} 个地形添加该图层？",
                "为缺失地形添加",
                "取消");

            if (!ok) return; // 取消则不处理

            AddLayerToCoveredTerrains(tl); // 仅为缺失地形添加（方法内已跳过已存在）
            RefreshLists();
            ApplySelection(tl);
        }

        private bool IsLayerHeldByCoveredTerrains(TerrainLayer tl)
        {
            if (!tl || _coveredLayers == null || _coveredLayers.Count == 0) return false; // 提前返回
            int id = tl.GetInstanceID();
            foreach (var l in _coveredLayers)
            {
                if (l && l.GetInstanceID() == id) return true;
            }
            return false;
        }

        private int GetCoverageCount(TerrainLayer tl)
        {
            if (!tl) return 0;
            var id = tl.GetInstanceID();
            return _coverageStats.TryGetValue(id, out var cnt) ? cnt : 0;
        }

        private void AddLayerToCoveredTerrains(TerrainLayer tl)
        {
            if (!tl || !_contextPathCreator) return; // 提前返回

            var terrains = GetCoveredTerrains(_contextPathCreator);
            if (terrains == null || terrains.Count == 0) return;

            foreach (var terrain in terrains)
            {
                if (!terrain || !terrain.terrainData) continue;

                var td = terrain.terrainData;
                var layers = td.terrainLayers ?? Array.Empty<TerrainLayer>();
                // 已存在则跳过
                var exists = layers.Any(l => l == tl);
                if (exists) continue;

                var newLayers = new TerrainLayer[layers.Length + 1];
                Array.Copy(layers, newLayers, layers.Length);
                newLayers[layers.Length] = tl;

                Undo.RecordObject(td, "Add TerrainLayer");
                td.terrainLayers = newLayers;
                EditorUtility.SetDirty(td);
            }

            // 通知刷新：窗口/Inspector 刷新，方便后续 Paint 识别到新列表
            EditorRefresh.Instance.RequestRefresh("terrain_layers_refresh", () =>
            {
                Repaint();
                EditorRefresh.Instance.RequestInspectorRefresh();
            }, forceImmediate: true);
        }

        private void ApplySelection(TerrainLayer tl)
        {
            if (_targetRoadLayer == null)
            {
                ShowNotification(new GUIContent("目标 RoadLayer 为空，无法应用选择"));
                return; // 提前返回
            }

            // 记录撤销并应用选择
            var owner = _contextPathCreator ? _contextPathCreator.profile : null;
            if (owner)
            {
                Undo.RecordObject(owner, "Select Terrain Layer");
            }

            _targetRoadLayer.contentLayer = tl;

            if (owner)
            {
                EditorUtility.SetDirty(owner);
            }
            _applied = true;
            MarkPreviewDirty();
            Close();
        }

        private void DrawFooterActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 仅保留应用按钮
                var applyLabel = _selected ? "应用" : "清空";
                if (GUILayout.Button(applyLabel, GUILayout.Height(24)))
                {
                    ApplySelection(_selected);
                }

                GUILayout.FlexibleSpace();
                // 右下角缩略图缩放滑条（类似Unity原生）
                GUILayout.Label("缩略图", GUILayout.Width(48));
                int newSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(_thumbSize, ThumbMin, ThumbMax, GUILayout.Width(160)));
                if (newSize != _thumbSize)
                {
                    _thumbSize = newSize;
                    Repaint();
                }
                GUILayout.Label(_thumbSize.ToString(), GUILayout.Width(32));
            }
        }

        // 工具方法：收集项目中的全部 TerrainLayer 资源
        private static List<TerrainLayer> CollectProjectLayers()
        {
            var guids = AssetDatabase.FindAssets("t:TerrainLayer");
            var result = new List<TerrainLayer>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tl = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (tl) result.Add(tl);
            }
            // 去重（按实例ID）
            var distinct = new List<TerrainLayer>();
            var seen = new HashSet<int>();
            foreach (var l in result)
            {
                var id = l.GetInstanceID();
                if (seen.Add(id)) distinct.Add(l);
            }
            return distinct.OrderBy(l => l.name).ToList();
        }

        private static IEnumerable<TerrainLayer> Filter(IEnumerable<TerrainLayer> src, string q)
        {
            if (string.IsNullOrEmpty(q)) return src;
            q = q.Trim();
            return src.Where(l => l && l.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // 收集道路覆盖地形所持有的全部图层
        private static List<TerrainLayer> CollectCoveredTerrainLayers(PathCreator creator)
        {
            var result = new List<TerrainLayer>();
            var terrains = GetCoveredTerrains(creator);
            if (terrains == null || terrains.Count == 0) return result; // 提前返回

            var set = new HashSet<int>();
            foreach (var t in terrains)
            {
                if (!t?.terrainData?.terrainLayers?.Any() ?? true) continue;
                foreach (var l in t.terrainData.terrainLayers)
                {
                    if (!l) continue;
                    var id = l.GetInstanceID();
                    if (set.Add(id)) result.Add(l);
                }
            }

            return result.OrderBy(l => l.name).ToList();
        }

        private void RefreshLists()
        {
            // 统一刷新资产与覆盖地形列表
            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());

            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers(_contextPathCreator));

            // 重建覆盖统计
            RebuildCoverageStats();
        }

        private void RebuildCoverageStats()
        {
            _coverageStats.Clear();
            _coveredTerrainTotal = 0;
            var terrains = GetCoveredTerrains(_contextPathCreator);
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            _coveredTerrainTotal = terrains.Count;
            foreach (var terrain in terrains)
            {
                if (!terrain || !terrain.terrainData) continue;
                var layers = terrain.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
                foreach (var l in layers)
                {
                    if (!l) continue;
                    var id = l.GetInstanceID();
                    _coverageStats[id] = _coverageStats.TryGetValue(id, out var cnt) ? (cnt + 1) : 1;
                }
            }
        }

        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ApplySelection(_selected);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.Use();
                }
            }
        }

        // ------- 道路覆盖地形计算 & 辅助方法 -------
        private static List<UnityEngine.Terrain> GetCoveredTerrains(PathCreator creator)
        {
            var result = new List<UnityEngine.Terrain>();
            if (!creator || !creator.profile || creator.pathData == null || creator.pathData.KnotCount < 2)
                return result; // 提前返回

            try
            {
                var heightProvider = new TerrainHeightProvider();
                var spine = PathSampler.SamplePath(creator, heightProvider);
                var bounds = GetExpandedXZBounds(spine, creator.profile);

                var terrains = UnityEngine.Terrain.activeTerrains;
                foreach (var terrain in terrains)
                {
                    if (!terrain?.terrainData) continue;
                    var tb = GetTerrainBounds(terrain);
                    if (BoundsOverlap(bounds, tb))
                    {
                        result.Add(terrain);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SelectTerrainLayerWindow] GetCoveredTerrains: {ex.Message}");
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
                var p = spine.Points[i];
                minX = Mathf.Min(minX, p.x - halfWidth);
                minZ = Mathf.Min(minZ, p.z - halfWidth);
                maxX = Mathf.Max(maxX, p.x + halfWidth);
                maxZ = Mathf.Max(maxZ, p.z + halfWidth);
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

        // ------- 实时预览赋值与回滚 -------
        private void PreviewAssign(TerrainLayer tl)
        {
            if (_targetRoadLayer == null) return; // 提前返回
            _targetRoadLayer.contentLayer = tl;
            MarkPreviewDirty();
        }

        private void MarkPreviewDirty()
        {
            if (_contextPathCreator)
            {
                MultiPathPreviewRenderer.MarkCreatorDirty(_contextPathCreator, spine: false, mesh: false, materials: true);
            }
        }

        private void OnDisable()
        {
            // 关闭时未应用则回滚
            if (!_applied && _targetRoadLayer != null)
            {
                _targetRoadLayer.contentLayer = _originalLayer;
                MarkPreviewDirty();
            }
        }
    }
}
#endif
