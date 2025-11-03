#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using MrPathV2.Editor.Preview;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// TerrainLayer 选择窗口（UITK）。
    /// - 单一职责：选择并应用 TerrainLayer 到 RoadLayer。
    /// - 提前返回：所有空引用与异常情况快速退出。
    /// - 高效简洁：基于 UITK，支持最小缩略图自动切换到列表样式。
    /// - 预览联动：选择变化立即刷新场景预览网格（材料）。
    /// </summary>
    public class SelectTerrainLayerWindow : EditorWindow
    {
        // 选择成功事件：让外部（Inspector）做行级更新，避免全量刷新
        public static event System.Action<__temp.MrPathV2.Runtime.Core.RoadLayer, UnityEngine.TerrainLayer> OnContentLayerApplied;
        // 上下文
        private RoadLayer _targetRoadLayer;
        private TerrainLayer _originalLayer;
        private PathCreator _contextPathCreator;

        // 数据
        private readonly List<TerrainLayer> _assetLayers = new();
        private readonly List<TerrainLayer> _coveredLayers = new();
        private readonly Dictionary<int, int> _coverageStats = new();
        private int _coveredTerrainTotal = 0;

        private TerrainLayer _selected;
        private bool _applied;

        // UI 与状态
        private const int ThumbMin = 48;
        private const int ThumbMax = 128;
        private const int DetailIconSize = 64; // 详情图标较小，提升紧凑度
        private int _thumbSize = 72;

        private enum FilterMode { All, HeldOnly, MissingOnly }
        private FilterMode _filterMode = FilterMode.All;
        private string _search = string.Empty;

        private ToolbarSearchField _searchField;
        private Label _nameLabel, _covLabel;
        private Label _sizeLabel, _terrainLabel, _pathLabel;
        private VisualElement _detailIcon;
        private SliderInt _thumbSlider;
        private VisualElement _contentRoot;
        private Button _tabAll, _tabHeld; // 顶部两个标签

        private readonly List<TerrainLayer> _visibleList = new();

        // 入口保持兼容
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

            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());

            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers(_contextPathCreator));

            RebuildCoverageStats();
            _selected = current;
            // 若未传入当前选中，则回退到 RoadLayer 的持有层，保证打开后保持选中状态
            if (_selected == null && _targetRoadLayer != null)
                _selected = _targetRoadLayer.contentLayer;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;
            // 引入统一样式表以提升观感
            try
            {
                var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/MrPathV2/Editor/Inspectors/PathProfileEditor.uss");
                if (ss) root.styleSheets.Add(ss);
                var selSs = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/MrPathV2/Editor/Styles/SelectTerrainLayerWindow.uss");
                if (selSs) root.styleSheets.Add(selSs);
                root.AddToClassList("root-container");
            }
            catch { /* ignore style load errors */ }

            // 顶部区域：第一行搜索，第二行两个标签（全部 / 仅已持有）
            _searchField = new ToolbarSearchField();
            _searchField.style.flexGrow = 1;
            _searchField.RegisterValueChangedCallback(ev =>
            {
                _search = ev.newValue ?? string.Empty;
                RebuildVisibleList();
            });

            var top = new VisualElement { name = "top" };
            top.style.flexDirection = FlexDirection.Column;
            top.style.marginLeft = 8; top.style.marginRight = 8; top.style.marginTop = 6; top.style.marginBottom = 0;

            var searchRow = new VisualElement { name = "search-row" };
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems = Align.Center;
            searchRow.Add(_searchField);
            top.Add(searchRow);

            var tabBar = new VisualElement { name = "tab-bar" };
            tabBar.style.flexDirection = FlexDirection.Row; tabBar.style.alignItems = Align.Center;

            _tabAll = new Button(() => SetFilterMode(FilterMode.All)) { text = "全部" };
            _tabHeld = new Button(() => SetFilterMode(FilterMode.HeldOnly)) { text = "仅已持有" };
            ApplyTabStyles(_tabAll, true); ApplyTabStyles(_tabHeld, false);
            tabBar.Add(_tabAll);
            tabBar.Add(_tabHeld);
            top.Add(tabBar);

            root.Add(top);

            // 内容区域（网格或列表）
            _contentRoot = new VisualElement { name = "content-root" };
            _contentRoot.style.flexGrow = 1;
            _contentRoot.style.marginLeft = 8; _contentRoot.style.marginRight = 8; _contentRoot.style.marginTop = 8; _contentRoot.style.marginBottom = 4;
            root.Add(_contentRoot);

            // 详情面板（固定高度，图标固定尺寸）
            var details = new VisualElement { name = "details" };
            details.style.flexDirection = FlexDirection.Column; // 外层采用 settings-group 的列式布局
            details.style.alignItems = Align.FlexStart;
            details.style.marginLeft = 8; details.style.marginRight = 8; details.style.marginTop = 4; details.style.marginBottom = 8;
            details.style.height = 80; // 更紧凑的详情高度
            details.style.flexShrink = 0;
            // 应用 PathProfileEditor.uss 中的 .settings-group 视觉风格
            details.AddToClassList("settings-group");
            // 内层行为行：左图标右信息
            var detailRow = new VisualElement { name = "detail-row" };
            detailRow.style.flexDirection = FlexDirection.Row;
            detailRow.style.alignItems = Align.Center;

            _detailIcon = new VisualElement { name = "icon" };
            _detailIcon.style.width = DetailIconSize; _detailIcon.style.height = DetailIconSize;
            _detailIcon.style.marginRight = 12; _detailIcon.style.marginLeft = 4;
            _detailIcon.style.borderBottomWidth = 1; _detailIcon.style.borderTopWidth = 1; _detailIcon.style.borderLeftWidth = 1; _detailIcon.style.borderRightWidth = 1;
            _detailIcon.style.borderBottomColor = Color.gray; _detailIcon.style.borderTopColor = Color.gray; _detailIcon.style.borderLeftColor = Color.gray; _detailIcon.style.borderRightColor = Color.gray;
            // 点击图标 Ping 到资产
            _detailIcon.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return; // 提前返回
                var tl = _selected;
                if (tl) EditorGUIUtility.PingObject(tl);
            });

            var info = new VisualElement { name = "info" };
            info.style.flexGrow = 1;
            info.style.flexDirection = FlexDirection.Column;

            _nameLabel = new Label("未选择图层") { name = "name" };
            _sizeLabel = new Label("尺寸：N/A") { name = "size" };
            _terrainLabel = new Label("地形覆盖：0/0") { name = "terrain" };
            _pathLabel = new Label("路径：-") { name = "path" };

            info.Add(_nameLabel);
            info.Add(_sizeLabel);
            info.Add(_terrainLabel);
            info.Add(_pathLabel);
            detailRow.Add(_detailIcon);
            detailRow.Add(info);
            details.Add(detailRow);
            root.Add(details);

            // 缩略图大小滑块独立放置在详情面板下方
            _thumbSlider = new SliderInt("缩略图大小", ThumbMin, ThumbMax);
            _thumbSlider.value = _thumbSize;
            _thumbSlider.style.marginTop = 6; _thumbSlider.style.marginBottom = 4;
            _thumbSlider.RegisterValueChangedCallback(ev =>
            {
                _thumbSize = Mathf.Clamp(ev.newValue, ThumbMin, ThumbMax);
                RebuildContent();
            });
            root.Add(_thumbSlider);
            // 初始构建
            // 打开窗口时主动刷新一遍数据与内容
            RefreshLists();
            RebuildVisibleList();
            RebuildContent();
            UpdateSelectionStyles();
            UpdateDetailsPanel();
        }

        // 顶部标签样式：对齐截图所示的暗色卡片风格
        private void ApplyTabStyles(Button btn, bool active)
        {
            if (btn == null) return; // 提前返回
            btn.style.height = 22;
            btn.style.marginTop = 6;
            btn.style.marginRight = 6;
            btn.style.paddingLeft = 10; btn.style.paddingRight = 10;
            btn.style.borderTopLeftRadius = 6; btn.style.borderTopRightRadius = 6; btn.style.borderBottomLeftRadius = 6; btn.style.borderBottomRightRadius = 6;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.color = Color.white;
            btn.style.borderBottomWidth = 1; btn.style.borderTopWidth = 1; btn.style.borderLeftWidth = 1; btn.style.borderRightWidth = 1;
            var bg = active ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.18f, 0.18f, 0.18f);
            var bd = active ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.25f, 0.25f, 0.25f);
            btn.style.backgroundColor = bg;
            btn.style.borderBottomColor = bd; btn.style.borderTopColor = bd; btn.style.borderLeftColor = bd; btn.style.borderRightColor = bd;
        }

        // 根据当前过滤模式更新标签的激活视觉
        private void UpdateTabActive()
        {
            ApplyTabStyles(_tabAll, _filterMode == FilterMode.All);
            ApplyTabStyles(_tabHeld, _filterMode == FilterMode.HeldOnly);
        }

        private bool IsListMode() => _thumbSize <= ThumbMin;

        private void RebuildContent()
        {
            // 保留滚动位置，避免刷新导致跳回顶部
            var prevScroll = _contentRoot.Q<ScrollView>("scroll");
            float prevOffset = 0f;
            if (prevScroll != null && prevScroll.verticalScroller != null)
                prevOffset = prevScroll.verticalScroller.value;

            _contentRoot.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "scroll" };
            scroll.style.flexGrow = 1;
            // 隐藏滚动条以匹配参考图（仍可滚轮滚动）
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _contentRoot.Add(scroll);

            // NullLayer 选项始终存在
            if (IsListMode())
            {
                scroll.Add(MakeListRow(null));
            }
            else
            {
                // grid 区域增加 settings-group 底板
                var group = new VisualElement { name = "grid-group" };
                group.AddToClassList("settings-group");
                group.style.marginLeft = 8; group.style.marginRight = 8; group.style.marginTop = 4; group.style.marginBottom = 4;
                group.style.paddingLeft = 12; group.style.paddingRight = 12; group.style.paddingTop = 12; group.style.paddingBottom = 12;
                group.style.flexDirection = FlexDirection.Column;
                // Grid 底板铺满对齐
                group.style.height = StyleKeyword.Auto;
                group.style.flexGrow = 1;
                scroll.Add(group);

                // 顶部标签“Assets”，与参考样式一致的简洁标签
                var tag = new Label("Assets");
                tag.style.unityFontStyleAndWeight = FontStyle.Bold;
                tag.style.marginBottom = 8;
                tag.style.paddingLeft = 8; tag.style.paddingRight = 8; tag.style.paddingTop = 2; tag.style.paddingBottom = 2;
                tag.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
                tag.style.borderTopLeftRadius = 4; tag.style.borderTopRightRadius = 4; tag.style.borderBottomLeftRadius = 4; tag.style.borderBottomRightRadius = 4;
                group.Add(tag);

                var grid = new VisualElement { name = "grid" };
                grid.style.flexDirection = FlexDirection.Row;
                grid.style.flexWrap = Wrap.Wrap;
                grid.style.alignContent = Align.FlexStart;
                grid.style.justifyContent = Justify.FlexStart;
                grid.AddToClassList("mrp-grid");
                group.Add(grid);
                grid.Add(MakeGridTile(null));
                foreach (var tl in _visibleList)
                {
                    grid.Add(MakeGridTile(tl));
                }
                // 让底板最小高度等于视口高度，实现“铺满”效果
                scroll.RegisterCallback<GeometryChangedEvent>(ev =>
                {
                    var g = scroll.Q<VisualElement>("grid-group");
                    if (g != null) g.style.minHeight = ev.newRect.height;
                });
                // 恢复滚动位置
                if (scroll.verticalScroller != null)
                    scroll.verticalScroller.value = prevOffset;
                return;
            }

            foreach (var tl in _visibleList)
            {
                scroll.Add(MakeListRow(tl));
            }

            // 恢复滚动位置
            if (scroll.verticalScroller != null)
                scroll.verticalScroller.value = prevOffset;
        }

        // 仅更新选中样式，避免因重建导致滚动跳跃
        private void UpdateSelectionStyles()
        {
            var scroll = _contentRoot.Q<ScrollView>("scroll");
            if (scroll == null) return; // 提前返回
            // 通用：直接更新当前 ScrollView 内所有 tile 与 row
            var tiles = scroll.Query<VisualElement>(name: "tile").ToList();
            foreach (var tile in tiles)
            {
                var tl = tile.userData as TerrainLayer;
                SetTileSelected(tile, tl == _selected);
            }

            var rows = scroll.Query<VisualElement>(name: "row").ToList();
            foreach (var row in rows)
            {
                var tl = row.userData as TerrainLayer;
                SetTileSelected(row, tl == _selected);
            }
        }

        private VisualElement MakeGridTile(TerrainLayer tl)
        {
            int tileSize = _thumbSize;
            var tile = new VisualElement { name = "tile" };
            tile.style.width = tileSize + 8; // 更紧凑的容器宽度
            tile.style.height = tileSize + 28;
            tile.style.marginLeft = 12; tile.style.marginRight = 12; tile.style.marginTop = 12; tile.style.marginBottom = 12; // 与截图一致的均匀间距
            tile.style.flexDirection = FlexDirection.Column;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.FlexStart;
            tile.style.borderBottomWidth = 0; tile.style.borderTopWidth = 0; tile.style.borderLeftWidth = 0; tile.style.borderRightWidth = 0; // tile 背景清爽
            tile.style.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明以露出 group 底板
            tile.userData = tl; // 记录所属图层，便于更新选中样式

            var icon = new VisualElement { name = "icon" };
            icon.style.width = tileSize; icon.style.height = tileSize;
            icon.style.marginTop = 4; icon.style.marginBottom = 8;
            icon.style.borderTopLeftRadius = 6; icon.style.borderTopRightRadius = 6; icon.style.borderBottomLeftRadius = 6; icon.style.borderBottomRightRadius = 6;
            icon.AddToClassList("tile-icon");

            var name = new Label { name = "name" };
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.fontSize = 12; // 与截图一致的小号标题
            name.style.color = Color.white;

            Texture2D tex = GetLayerThumbnail(tl);
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;

            // 名称过长使用省略号，并将 NullLayer 改为 Null
            var rawName = tl ? tl.name : "Null";
            var displayName = TruncateEnd(rawName, Mathf.Clamp(tileSize / 8, 6, 20));
            name.text = displayName;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            bool held = IsLayerHeldByCoveredTerrains(tl);
            name.style.color = held ? new Color(0.1f, 0.8f, 0.1f) : Color.white;

            // 悬停高亮改由 USS :hover 控制，避免内联样式覆盖 selected 视觉

            // 选择与应用
            tile.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return; // 提前返回
                SelectLayer(tl);
                if (evt.clickCount == 2)
                {
                    TryApplyByDoubleClick(tl);
                }
            });

            tile.Add(icon);
            tile.Add(name);
            if (_selected == tl) SetTileSelected(tile, true);
            return tile;
        }

        private VisualElement MakeListRow(TerrainLayer tl)
        {
            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = Mathf.Max(ThumbMin + 12, 64);
            row.style.marginLeft = 4; row.style.marginRight = 4; row.style.marginTop = 2; row.style.marginBottom = 2;
            row.style.borderBottomWidth = 1; row.style.borderTopWidth = 1; row.style.borderLeftWidth = 1; row.style.borderRightWidth = 1;
            row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f); row.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f); row.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f); row.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            row.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
            row.AddToClassList("mrp-list-row");
            row.userData = tl; // 记录所属图层，便于更新选中样式

            var icon = new VisualElement { name = "icon" };
            icon.style.width = ThumbMin; icon.style.height = ThumbMin; // 列表模式固定采用最小缩略图尺寸
            icon.style.marginLeft = 6; icon.style.marginRight = 8;
            icon.style.borderTopLeftRadius = 4; icon.style.borderTopRightRadius = 4; icon.style.borderBottomLeftRadius = 4; icon.style.borderBottomRightRadius = 4;
            icon.AddToClassList("mrp-icon");
            icon.AddToClassList("tile-icon");

            var content = new VisualElement { name = "content" };
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Column;
            var text = new Label { name = "name" };
            text.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.AddToClassList("mrp-name");

            Texture2D tex = GetLayerThumbnail(tl);
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;

            text.text = tl ? tl.name : "Null";
            bool held = IsLayerHeldByCoveredTerrains(tl);
            text.style.color = held ? new Color(0.1f, 0.8f, 0.1f) : Color.white;

            // 子信息行：状态与覆盖数
            var sub = new VisualElement { name = "sub-info" };
            sub.style.flexDirection = FlexDirection.Row;
            var status = new Label { name = "status" };
            status.text = held ? "已持有" : "未持有";
            status.style.color = held ? new Color(0.2f, 0.9f, 0.2f) : new Color(1f, 0.7f, 0.2f);
            status.style.marginRight = 10;
            var covLabel = new Label { name = "cov" };
            var total = _coveredTerrainTotal;
            covLabel.text = tl ? $"覆盖：{GetCoverageCount(tl)}/{total}" : "覆盖：0/" + total;
            covLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            sub.Add(status);
            sub.Add(covLabel);

            // 悬停高亮改由 USS :hover 控制，避免内联样式覆盖 selected 视觉

            // 选择与应用
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return; // 提前返回
                SelectLayer(tl);
                if (evt.clickCount == 2)
                {
                    TryApplyByDoubleClick(tl);
                }
            });

            row.Add(icon);
            content.Add(text);
            content.Add(sub);
            row.Add(content);
            if (_selected == tl) SetTileSelected(row, true);
            return row;
        }

        // 悬停样式统一交给 USS 处理，无需代码干预

        private void SetTileSelected(VisualElement ve, bool selected)
        {
            // 使用 USS 过渡：仅切换 selected 类，动画由样式表驱动
            var icon = ve.Q<VisualElement>("icon");
            if (icon != null)
            {
                // 不做任何内联边框赋值，避免覆盖 USS 动画
            }

            // 名称颜色保持“已持有为绿色，否则白色”，选中不改变名称颜色
            var nameLabel = ve.Q<Label>("name");
            if (nameLabel != null)
            {
                var tl = ve.userData as TerrainLayer;
                bool held = IsLayerHeldByCoveredTerrains(tl);
                nameLabel.style.color = held ? new Color(0.1f, 0.8f, 0.1f) : Color.white;
            }

            if (selected) ve.AddToClassList("selected");
            else ve.RemoveFromClassList("selected");
        }

        // 移除 C# 动画；动画改由 USS transition 实现


        private Texture2D GetLayerThumbnail(TerrainLayer tl)
        {
            if (!tl) return null; // 提前返回
            var tex = tl.diffuseTexture as Texture2D;
            if (tex) return tex;
            tex = AssetPreview.GetAssetPreview(tl) as Texture2D;
            if (tex) return tex;
            return AssetPreview.GetMiniThumbnail(tl) as Texture2D;
        }

        private void SelectLayer(TerrainLayer tl)
        {
            _selected = tl;
            // 预览联动：更新 RoadLayer 的 contentLayer 并立即刷新材料
            if (_targetRoadLayer != null)
            {
                _targetRoadLayer.contentLayer = tl;
                // 选择 Null 时不刷新材质，避免 Texture2DArray 警告
                MarkPreviewDirty(materials: tl != null);
            }
            // 轻量：不重建内容，只更新选中样式与详情，避免滚动跳跃
            UpdateSelectionStyles();
            UpdateDetailsPanel();
        }

        private void UpdateDetailsPanel()
        {
            var tl = _selected;
            if (!tl)
            {
                _nameLabel.text = "未选择图层";
                if (_terrainLabel != null) _terrainLabel.text = "地形覆盖：0/0";
                if (_sizeLabel != null) _sizeLabel.text = "尺寸：N/A";
                if (_pathLabel != null) _pathLabel.text = "路径：-";
                _detailIcon.style.backgroundImage = null;
                return; // 早退
            }
            _nameLabel.text = tl.name;
            var cov = GetCoverageCount(tl);
            var total = _coveredTerrainTotal;
            if (_terrainLabel != null) _terrainLabel.text = $"地形覆盖：{cov}/{total}";
            var texSize = tl.diffuseTexture as Texture2D;
            if (_sizeLabel != null) _sizeLabel.text = texSize ? $"尺寸：{texSize.width}x{texSize.height}" : "尺寸：N/A";
            if (_pathLabel != null)
            {
                var ap = AssetDatabase.GetAssetPath(tl);
                int maxPath = Mathf.Clamp((int)(position.width / 10f), 24, 60);
                var displayPath = string.IsNullOrEmpty(ap) ? "-" : TruncateMiddle(ap, maxPath);
                _pathLabel.text = $"路径：{displayPath}";
            }
            var tex = GetLayerThumbnail(tl);
            _detailIcon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
        }

        // ---------- 文本省略工具 ----------
        private static string TruncateEnd(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return string.Empty;
            if (s.Length <= max) return s;
            if (max <= 3) return new string('.', max);
            return s.Substring(0, max - 3) + "...";
        }

        private static string TruncateMiddle(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return string.Empty;
            if (s.Length <= max) return s;
            if (max <= 5) return TruncateEnd(s, max);
            int keep = max - 3;
            int head = Mathf.CeilToInt(keep * 0.6f);
            int tail = keep - head;
            return s.Substring(0, head) + "..." + s.Substring(s.Length - tail);
        }

        private void ApplySelection(TerrainLayer tl)
        {
            if (_targetRoadLayer == null)
            {
                ShowNotification(new GUIContent("目标 RoadLayer 为空，无法应用选择"));
                return; // 提前返回
            }

            var owner = _contextPathCreator ? _contextPathCreator.profile : null;
            if (owner) Undo.RecordObject(owner, "Select Terrain Layer");
            _targetRoadLayer.contentLayer = tl;
            if (owner) EditorUtility.SetDirty(owner);
            _applied = true;
            // 应用 Null 时不刷新材质
            MarkPreviewDirty(materials: tl != null);
            // 触发细粒度回调：仅更新对应行元素
            try { OnContentLayerApplied?.Invoke(_targetRoadLayer, tl); } catch { /* 防御：忽略回调异常 */ }
            Close();
        }

        // 双击应用前的确认逻辑（仅当不在已持有集合时弹窗）。
        private void TryApplyByDoubleClick(TerrainLayer tl)
        {
            if (tl == null)
            {
                // 双击 Null：清空当前 RoadLayer 的地形图层插槽
                ApplySelection(null);
                return; // 提前返回
            }
            if (IsLayerHeldByCoveredTerrains(tl))
            {
                ApplySelection(tl);
                return; // 提前返回
            }

            // 系统标准对话框，默认焦点在“取消”（第一个按钮）
            bool okIsCancel = EditorUtility.DisplayDialog(
                "添加图层到地形确认",
                "您正在尝试将新地形Layer添加到地形，是否继续？",
                "取消",
                "确认添加");

            if (!okIsCancel)
            {
                ApplySelection(tl);
            }
        }

        private void MarkPreviewDirty(bool spine = false, bool mesh = false, bool materials = true)
        {
            // 直接使用全局预览渲染器刷新指定 PathCreator
            try
            {
                if (_contextPathCreator)
                {
                    MultiPathPreviewRenderer.MarkCreatorDirty(_contextPathCreator, spine, mesh, materials);
                    SceneView.RepaintAll();
                }
            }
            catch { /* 忽略预览更新异常 */ }
        }

        private void OnLostFocus() => Close();

        private void OnDisable()
        {
            if (!_applied && _targetRoadLayer != null)
            {
                _targetRoadLayer.contentLayer = _originalLayer;
                // 回滚到 Null 时也不刷新材质
                MarkPreviewDirty(materials: _originalLayer != null);
            }
        }

        // -------- 过滤与数据 ---------
        private void SetFilterMode(FilterMode mode)
        {
            _filterMode = mode;
            UpdateTabActive();
            RebuildVisibleList();
        }

        private void RefreshLists()
        {
            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());
            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers(_contextPathCreator));
            RebuildCoverageStats();
        }

        private void RebuildVisibleList()
        {
            _visibleList.Clear();
            IEnumerable<TerrainLayer> seq = _assetLayers;
            if (!string.IsNullOrEmpty(_search))
            {
                var s = _search.ToLowerInvariant();
                seq = seq.Where(l => l && l.name != null && l.name.ToLowerInvariant().Contains(s));
            }
            switch (_filterMode)
            {
                case FilterMode.HeldOnly:
                    seq = seq.Where(IsLayerHeldByCoveredTerrains);
                    break;
                case FilterMode.MissingOnly:
                    seq = seq.Where(l => !IsLayerHeldByCoveredTerrains(l));
                    break;
            }
            _visibleList.AddRange(seq);
            RebuildContent();
            // 重建后立即同步选中样式，确保列表/网格状态一致
            UpdateSelectionStyles();
        }

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
            var distinct = new List<TerrainLayer>();
            var seen = new HashSet<int>();
            foreach (var l in result)
            {
                var id = l.GetInstanceID();
                if (seen.Add(id)) distinct.Add(l);
            }
            return distinct.OrderBy(l => l.name).ToList();
        }

        private List<TerrainLayer> CollectCoveredTerrainLayers(PathCreator ctx)
        {
            var res = new List<TerrainLayer>();
            var terrains = GameObject.FindObjectsOfType<UnityEngine.Terrain>();
            _coveredTerrainTotal = terrains?.Length ?? 0;
            if (terrains == null || terrains.Length == 0) return res; // 早退
            foreach (var t in terrains)
            {
                var data = t.terrainData;
                if (!data) continue;
                var tls = data.terrainLayers;
                if (tls == null || tls.Length == 0) continue;
                foreach (var l in tls)
                {
                    if (l) res.Add(l);
                }
            }
            // 去重
            var distinct = new List<TerrainLayer>();
            var seen = new HashSet<int>();
            foreach (var l in res)
            {
                var id = l.GetInstanceID();
                if (seen.Add(id)) distinct.Add(l);
            }
            return distinct.OrderBy(l => l.name).ToList();
        }

        private void RebuildCoverageStats()
        {
            _coverageStats.Clear();
            var terrains = GameObject.FindObjectsOfType<UnityEngine.Terrain>();
            _coveredTerrainTotal = terrains?.Length ?? 0;
            if (terrains == null || terrains.Length == 0) return;
            foreach (var t in terrains)
            {
                var data = t.terrainData;
                if (!data) continue;
                var tls = data.terrainLayers;
                if (tls == null || tls.Length == 0) continue;
                foreach (var l in tls)
                {
                    if (!l) continue;
                    var id = l.GetInstanceID();
                    if (_coverageStats.TryGetValue(id, out var cnt)) _coverageStats[id] = cnt + 1;
                    else _coverageStats[id] = 1;
                }
            }
        }

        private int GetCoverageCount(TerrainLayer tl)
        {
            if (!tl) return 0;
            var id = tl.GetInstanceID();
            return _coverageStats.TryGetValue(id, out var cnt) ? cnt : 0;
        }

        private bool IsLayerHeldByCoveredTerrains(TerrainLayer tl)
        {
            if (!tl || _coveredLayers == null || _coveredLayers.Count == 0) return false;
            int id = tl.GetInstanceID();
            foreach (var l in _coveredLayers)
            {
                if (l && l.GetInstanceID() == id) return true;
            }
            return false;
        }
    }
}
#endif
