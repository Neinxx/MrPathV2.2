#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using __temp.MrPathV2.Editor.Terrain;
using __temp.MrPathV2.Editor;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// 基于 UITK + UXML 的 TerrainLayer 选择窗口。
    /// - 单一职责：选择并应用 TerrainLayer 到 RoadLayer。
    /// - 提前返回：所有空引用与异常情况快速退出。
    /// - 高效：使用紧凑网格、仅必要刷新与最小化 GC。
    /// - 现代：UI Toolkit 布局与事件、双击应用与预览更新。
    /// 兼容：保持与原 Open(...) 签名一致，供外部调用。
    /// </summary>
    public class SelectTerrainLayerWindow : EditorWindow
    {
        // 选择成功事件：供 StylizedRoadRecipeEditor 局部刷新行使用
        public static event System.Action<RoadLayer, TerrainLayer> OnContentLayerApplied;
        // 目标对象与上下文
        private RoadLayer _targetRoadLayer;
        private TerrainLayer _originalLayer;
        private PathCreator _contextPathCreator;

        // 数据缓存
        private readonly List<TerrainLayer> _assetLayers = new();
        private readonly List<TerrainLayer> _coveredLayers = new();
        private readonly Dictionary<int, int> _coverageStats = new();

        // 状态
        private TerrainLayer _selected;
        private bool _applied;

        // UI 缩略图尺寸
        private int _thumbSize = 72;
        private const int ThumbMin = 48;
        private const int ThumbMax = 128;

        // 过滤模式
        private enum FilterMode { All, HeldOnly, MissingOnly }
        private FilterMode _filterMode = FilterMode.All;
        private string _search = string.Empty;
        private int _coveredTerrainTotal = 0;

        // UXML 控件引用
        private ToolbarSearchField _searchField;
        private ToolbarPopupSearchField _searchFieldPopup;
        private Button _btnAll, _btnHeld;
        private GroupBox _layerBox;
        private VisualElement _iconBox;
        private Label _nameLabel, _sizeLabel, _covLabel, _pathLabel;
        private SliderInt _thumbSlider;
        private Slider _thumbSliderFloat;
        private ScrollView _gridScroll;
        private VisualElement _grid;

        // 可见列表
        private readonly List<TerrainLayer> _visibleList = new();

        // 对外 API：保持签名一致
        public static void Open(RoadLayer roadLayer, TerrainLayer current, PathCreator contextPathCreator)
        {
            var win = GetWindow<SelectTerrainLayerWindow>(true, "Select Terrain Layer", true);
            win.minSize = new Vector2(420, 340);
            win.Initialize(roadLayer, current, contextPathCreator);
            win.Show();
        }

        private void Initialize(RoadLayer roadLayer, TerrainLayer current, PathCreator context)
        {
            _targetRoadLayer = roadLayer;
            _originalLayer = current;
            _contextPathCreator = context;

            // 收集数据
            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());

            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers());

            RebuildCoverageStats();
            _selected = current;
        }

        public void CreateGUI()
        {
            // 加载 UXML 布局
            // var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            //     "Assets/__temp/MrPathV2/Editor/Windows/SelectTerrainLayerWindow.uxml");
            var vta = UIResourceLoader.LoadUxml(typeof(SelectTerrainLayerWindow));

            if (!vta)
            {
                rootVisualElement.Add(new Label("缺少 UXML: SelectTerrainLayerWindow.uxml"));
                return; // 早退
            }
            vta.CloneTree(rootVisualElement);

            // 不再使用外部图标模板，全部通过代码构建 tile（更灵活）

            // 查询控件
            _searchFieldPopup = rootVisualElement.Q<ToolbarPopupSearchField>(name: "ToolbarPopupSearchField");
            _searchField = rootVisualElement.Q<ToolbarSearchField>();
            _btnAll = rootVisualElement.Q<Button>(name: "All");
            _btnHeld = rootVisualElement.Q<Button>(name: "Holded");
            _layerBox = rootVisualElement.Q<GroupBox>(name: "LayerBox");

            _iconBox = rootVisualElement.Q<VisualElement>(name: "IconBox");
            _nameLabel = rootVisualElement.Q<Label>(name: "Name");
            _sizeLabel = rootVisualElement.Q<Label>(name: "Szie"); // UXML 拼写为 Szie
            _covLabel = rootVisualElement.Q<Label>(name: "CoverTerrain");
            _pathLabel = rootVisualElement.Q<Label>(name: "Path");
            _thumbSlider = rootVisualElement.Q<SliderInt>(name: "IconScale");
            _thumbSliderFloat = rootVisualElement.Q<Slider>(name: "IconScale");

            // 构建网格滚动容器
            _gridScroll = new ScrollView(ScrollViewMode.Vertical) { name = "GridScroll" };
            _gridScroll.style.flexGrow = 1;
            _grid = new VisualElement { name = "Grid" };
            _grid.style.flexDirection = FlexDirection.Row;
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.justifyContent = Justify.FlexStart;
            _gridScroll.Add(_grid);
            if (_layerBox != null)
            {
                _layerBox.Add(_gridScroll);
            }
            else
            {
                rootVisualElement.Add(_gridScroll);
            }

            ApplyGridLayoutByMode();

            // 事件绑定（优先使用 ToolbarPopupSearchField）
            if (_searchFieldPopup != null)
            {
                _searchFieldPopup.RegisterValueChangedCallback(ev =>
                {
                    _search = ev.newValue ?? string.Empty;
                    RebuildVisibleList();
                    RebuildGrid();
                });
            }
            else if (_searchField != null)
            {
                _searchField.RegisterValueChangedCallback(ev =>
                {
                    _search = ev.newValue ?? string.Empty;
                    RebuildVisibleList();
                    RebuildGrid();
                });
            }
            if (_btnAll != null) _btnAll.clicked += () => { SetFilterMode(FilterMode.All); };
            if (_btnHeld != null) _btnHeld.clicked += () => { SetFilterMode(FilterMode.HeldOnly); };

            // 缩略图缩放：支持 SliderInt 与 Slider（UXML 中为 Slider）
            if (_thumbSlider != null)
            {
                _thumbSlider.lowValue = ThumbMin;
                _thumbSlider.highValue = ThumbMax;
                _thumbSlider.value = Mathf.Clamp(_thumbSize, ThumbMin, ThumbMax);
                _thumbSlider.RegisterValueChangedCallback(ev =>
                {
                    _thumbSize = Mathf.Clamp(ev.newValue, ThumbMin, ThumbMax);
                    ApplyGridLayoutByMode();
                    RebuildGrid();
                });
            }
            else if (_thumbSliderFloat != null)
            {
                _thumbSliderFloat.lowValue = ThumbMin;
                _thumbSliderFloat.highValue = ThumbMax;
                _thumbSliderFloat.value = Mathf.Clamp(_thumbSize, ThumbMin, ThumbMax);
                _thumbSliderFloat.RegisterValueChangedCallback(ev =>
                {
                    _thumbSize = Mathf.Clamp(Mathf.RoundToInt(ev.newValue), ThumbMin, ThumbMax);
                    ApplyGridLayoutByMode();
                    RebuildGrid();
                });
            }

            // 初始刷新与构建：确保打开窗口时最新列表与可见项
            RefreshLists();
            // 默认选中 All 标签
            SetFilterMode(FilterMode.All);
            UpdateDetailsPanel();
        }

        // 列表与网格
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
            // 排序：绿色（持有）优先，黄色（等价）其次，其他最后
            _visibleList.Sort((a, b) => GetPriority(b).CompareTo(GetPriority(a)) != 0
                ? GetPriority(b).CompareTo(GetPriority(a))
                : string.Compare(a ? a.name : string.Empty, b ? b.name : string.Empty, StringComparison.Ordinal));
        }

        private void RebuildGrid()
        {
            if (_grid == null) return;
            _grid.Clear();

            // NullLayer 选项（允许清空）
            _grid.Add(MakeTile(null));

            for (int i = 0; i < _visibleList.Count; i++)
            {
                _grid.Add(MakeTile(_visibleList[i]));
            }
        }

        private VisualElement MakeTile(TerrainLayer tl)
        {
            int tileSize = _thumbSize;
            var tile = new VisualElement { name = "Tile" };
            var icon = new VisualElement { name = "LayerIcon" };
            var name = new Label { name = "LayerName" };
            tile.Add(icon);
            tile.Add(name);

            // 布局：最小时列表模式，否则网格模式
            if (IsListMode())
            {
                tile.style.flexDirection = FlexDirection.Row;
                tile.style.alignItems = Align.Center;
                tile.style.justifyContent = Justify.FlexStart;
                tile.style.width = Length.Percent(100);
                // 放大列表模式的行高与图标尺寸，增强可读性
                tile.style.height = 40;
                icon.style.width = 32;
                icon.style.height = 32;
                icon.style.marginLeft = 8; icon.style.marginRight = 8;
                // 层项间距缩小 1/2
                tile.style.marginLeft = 3; tile.style.marginRight = 3; tile.style.marginTop = 1; tile.style.marginBottom = 1;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            else
            {
                tile.style.width = tileSize + 32;
                tile.style.height = tileSize + 46;
                // 层项间距缩小 1/2（网格模式）
                tile.style.marginLeft = 3; tile.style.marginRight = 3; tile.style.marginTop = 3; tile.style.marginBottom = 3;
                tile.style.flexDirection = FlexDirection.Column;
                tile.style.alignItems = Align.Center;
                tile.style.justifyContent = Justify.FlexStart;
                icon.style.width = tileSize; icon.style.height = tileSize;
                icon.style.marginTop = 6; icon.style.marginBottom = 6;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            // 通用样式：去除图标后方深色框（卡片底色清空）
            tile.style.borderBottomWidth = 0; tile.style.borderTopWidth = 0; tile.style.borderLeftWidth = 0; tile.style.borderRightWidth = 0;
            tile.style.backgroundColor = Color.clear;
            tile.style.borderTopLeftRadius = 8; tile.style.borderTopRightRadius = 8; tile.style.borderBottomLeftRadius = 8; tile.style.borderBottomRightRadius = 8;
            icon.style.borderBottomWidth = 1; icon.style.borderTopWidth = 1; icon.style.borderLeftWidth = 1; icon.style.borderRightWidth = 1;
            icon.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f); icon.style.borderTopColor = icon.style.borderBottomColor; icon.style.borderLeftColor = icon.style.borderBottomColor; icon.style.borderRightColor = icon.style.borderBottomColor;

            // 图标圆角：列表模式减半（更直感的矩形视觉）
            if (IsListMode())
            {
                icon.style.borderTopLeftRadius = 6; icon.style.borderTopRightRadius = 6; icon.style.borderBottomLeftRadius = 6; icon.style.borderBottomRightRadius = 6;
            }
            else
            {
                icon.style.borderTopLeftRadius = 12; icon.style.borderTopRightRadius = 12; icon.style.borderBottomLeftRadius = 12; icon.style.borderBottomRightRadius = 12;
            }

            // 缩略图与名称
            Texture2D tex = GetLayerThumbnail(tl);
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
            // Null 图标 50% 透明度
            icon.style.backgroundColor = tl ? Color.clear : new Color(0f, 0f, 0f, 0.5f);
            var rawName = tl ? tl.name : "Null";
            int maxChars = IsListMode() ? 22 : 12;
            name.text = TruncateName(rawName, maxChars);
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.tooltip = name.text;

            // 颜色：持有绿色、等价黄色、其他白色
            bool held = IsLayerHeldByCoveredTerrains(tl);
            bool eq = !held && HasEquivalentInCoveredTerrains(tl);
            name.style.color = held ? new Color(0.1f, 0.8f, 0.1f) : (eq ? new Color(0.95f, 0.85f, 0.25f) : Color.white);

            tile.RegisterCallback<MouseEnterEvent>(_ => SetTileHovered(tile, true));
            tile.RegisterCallback<MouseLeaveEvent>(_ => SetTileHovered(tile, false));
            tile.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectTile(tl, tile);
                    if (evt.clickCount == 2)
                    {
                        ApplySelection(tl);
                    }
                }
            });

            if (_selected == tl) SetTileSelected(tile, true);
            return tile;
        }

        private static Texture2D GetLayerThumbnail(TerrainLayer tl)
        {
            if (!tl) return null;
            var tex = tl.diffuseTexture as Texture2D;
            if (tex) return tex;
            tex = AssetPreview.GetAssetPreview(tl) as Texture2D;
            if (tex) return tex;
            return AssetPreview.GetMiniThumbnail(tl) as Texture2D;
        }

        // 名称截断：过长时补充省略号，保持单行优雅
        private static string TruncateName(string input, int maxChars)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            if (maxChars <= 3 || input.Length <= maxChars) return input;
            return input.Substring(0, maxChars - 3) + "...";
        }

        private void SelectTile(TerrainLayer tl, VisualElement tile)
        {
            _selected = tl;
            // 预览指派：首次选中刷新预览
            if (_targetRoadLayer != null)
            {
                _targetRoadLayer.contentLayer = tl;
                MarkPreviewDirty();
            }
            UpdateSelectionVisual(tile);
            UpdateDetailsPanel();
        }

        private void UpdateSelectionVisual(VisualElement selectedTile)
        {
            if (_grid == null) return;
            foreach (var child in _grid.Children())
            {
                SetTileSelected(child, child == selectedTile);
            }
        }

        private void SetTileSelected(VisualElement tile, bool selected)
        {
            var icon = tile.Q<VisualElement>(name: "LayerIcon");
            if (icon == null) return;

            // 去除卡片底色，所有选中高亮集中在图标上
            tile.style.backgroundColor = Color.clear;

            if (selected)
            {
                // 橙色粗描边强调选中（贴近参考图）
                icon.style.borderBottomWidth = 3; icon.style.borderTopWidth = 3; icon.style.borderLeftWidth = 3; icon.style.borderRightWidth = 3;
                var orange = new Color(1f, 0.68f, 0.1f);
                icon.style.borderBottomColor = orange; icon.style.borderTopColor = orange; icon.style.borderLeftColor = orange; icon.style.borderRightColor = orange;
            }
            else
            {
                icon.style.borderBottomWidth = 1; icon.style.borderTopWidth = 1; icon.style.borderLeftWidth = 1; icon.style.borderRightWidth = 1;
                var gray = new Color(0.3f, 0.3f, 0.3f);
                icon.style.borderBottomColor = gray; icon.style.borderTopColor = gray; icon.style.borderLeftColor = gray; icon.style.borderRightColor = gray;
            }
        }

        private void SetTileHovered(VisualElement tile, bool hovered)
        {
            var icon = tile.Q<VisualElement>(name: "LayerIcon");
            if (icon == null) return;

            bool selected = icon.style.borderBottomWidth.value > 2.4f; // 选中时宽度为 3
            if (hovered)
            {
                if (!selected)
                {
                    // 滑动高亮：直接作用在图标边框上
                    icon.style.borderBottomWidth = 2; icon.style.borderTopWidth = 2; icon.style.borderLeftWidth = 2; icon.style.borderRightWidth = 2;
                    var hoverCol = new Color(0.85f, 0.85f, 0.85f);
                    icon.style.borderBottomColor = hoverCol; icon.style.borderTopColor = hoverCol; icon.style.borderLeftColor = hoverCol; icon.style.borderRightColor = hoverCol;
                }
            }
            else
            {
                if (!selected)
                {
                    // 恢复默认图标边框
                    icon.style.borderBottomWidth = 1; icon.style.borderTopWidth = 1; icon.style.borderLeftWidth = 1; icon.style.borderRightWidth = 1;
                    var gray = new Color(0.3f, 0.3f, 0.3f);
                    icon.style.borderBottomColor = gray; icon.style.borderTopColor = gray; icon.style.borderLeftColor = gray; icon.style.borderRightColor = gray;
                }
            }
        }

        private void SetFilterMode(FilterMode mode)
        {
            _filterMode = mode;
            RebuildVisibleList();
            RebuildGrid();
            UpdateFilterButtonsVisual();
        }

        // 更新过滤按钮视觉状态：打开窗口时默认 All 高亮
        private void UpdateFilterButtonsVisual()
        {
            if (_btnAll != null)
            {
                bool active = _filterMode == FilterMode.All;
                _btnAll.style.backgroundColor = active ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.17f, 0.17f, 0.17f);
                _btnAll.style.color = active ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.42f, 0.42f, 0.42f);
            }
            if (_btnHeld != null)
            {
                bool active = _filterMode == FilterMode.HeldOnly;
                _btnHeld.style.backgroundColor = active ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.17f, 0.17f, 0.17f);
                _btnHeld.style.color = active ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.42f, 0.42f, 0.42f);
            }
        }

        private void UpdateDetailsPanel()
        {
            var tl = _selected;
            if (_nameLabel == null || _covLabel == null || _iconBox == null) return;

            if (!tl)
            {
                _nameLabel.text = "未选择图层";
                if (_sizeLabel != null) _sizeLabel.text = string.Empty;
                _covLabel.text = "覆盖数：0/0";
                if (_pathLabel != null) _pathLabel.text = string.Empty;
                _iconBox.style.backgroundImage = null;
                // Null 图标在详情面板也保持 50% 透明
                _iconBox.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
                return; // 早退
            }

            _nameLabel.text = TruncateName(tl.name, 24);
            var tex = tl.diffuseTexture as Texture2D;
            if (_sizeLabel != null)
            {
                _sizeLabel.text = tex ? $"尺寸：{tex.width}x{tex.height}" : "尺寸：-";
            }
            var cov = GetCoverageCount(tl);
            _covLabel.text = $"覆盖数：{cov}/{_coveredTerrainTotal}";
            if (_pathLabel != null)
            {
                var path = AssetDatabase.GetAssetPath(tl);
                _pathLabel.text = string.IsNullOrEmpty(path) ? "路径：-" : $"路径：{path}";
            }
            var preview = GetLayerThumbnail(tl);
            _iconBox.style.backgroundImage = preview != null ? new StyleBackground(preview) : null;
            _iconBox.style.backgroundColor = Color.clear;
            _iconBox.style.borderTopLeftRadius = 12; _iconBox.style.borderTopRightRadius = 12; _iconBox.style.borderBottomLeftRadius = 12; _iconBox.style.borderBottomRightRadius = 12;
            _iconBox.style.borderBottomWidth = 1; _iconBox.style.borderTopWidth = 1; _iconBox.style.borderLeftWidth = 1; _iconBox.style.borderRightWidth = 1;
            _iconBox.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f); _iconBox.style.borderTopColor = _iconBox.style.borderBottomColor; _iconBox.style.borderLeftColor = _iconBox.style.borderBottomColor; _iconBox.style.borderRightColor = _iconBox.style.borderBottomColor;
            // 详情预览不参与缩放，固定尺寸，文字溢出省略
            _iconBox.style.width = 81; // 固定示例尺寸，可按需调整
            _iconBox.style.height = 74;
            if (_nameLabel != null) { _nameLabel.style.whiteSpace = WhiteSpace.NoWrap; _nameLabel.style.overflow = Overflow.Hidden; _nameLabel.tooltip = tl.name; }
            if (_sizeLabel != null) { _sizeLabel.style.whiteSpace = WhiteSpace.NoWrap; _sizeLabel.style.overflow = Overflow.Hidden; _sizeLabel.tooltip = _sizeLabel.text; }
            if (_covLabel != null) { _covLabel.style.whiteSpace = WhiteSpace.NoWrap; _covLabel.style.overflow = Overflow.Hidden; _covLabel.tooltip = _covLabel.text; }
            if (_pathLabel != null) { _pathLabel.style.whiteSpace = WhiteSpace.NoWrap; _pathLabel.style.overflow = Overflow.Hidden; _pathLabel.tooltip = _pathLabel.text; }
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
            // 通知外部（Inspector）进行对应行的轻量刷新
            try { OnContentLayerApplied?.Invoke(_targetRoadLayer, tl); } catch { }
            MarkPreviewDirty();
            Close();
        }

        private void MarkPreviewDirty()
        {
            try
            {
                var type = System.Type.GetType("MrPathV2.Editor.Preview.MultiPathPreviewRenderer, Assembly-CSharp-Editor");
                if (type != null)
                {
                    var m = type.GetMethod("MarkCreatorDirty", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (m != null)
                    {
                        m.Invoke(null, new object[] { _contextPathCreator, false, false, true });
                        return;
                    }
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
                MarkPreviewDirty();
            }
        }

        // -------- 数据收集与筛选 --------
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

        private List<TerrainLayer> CollectCoveredTerrainLayers()
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

        // 刷新数据源：项目与场景覆盖层，并重新构建视图
        private void RefreshLists()
        {
            _assetLayers.Clear();
            _assetLayers.AddRange(CollectProjectLayers());
            _coveredLayers.Clear();
            _coveredLayers.AddRange(CollectCoveredTerrainLayers());
            RebuildCoverageStats();
            RebuildVisibleList();
            RebuildGrid();
        }

        // 等价层：贴图匹配（参考 LayerResolver 逻辑），用于黄色标记与排序次级
        private bool HasEquivalentInCoveredTerrains(TerrainLayer tl)
        {
            if (!tl) return false;
            var terrains = GameObject.FindObjectsOfType<UnityEngine.Terrain>();
            if (terrains == null || terrains.Length == 0) return false;
            var targetDiffuse = tl.diffuseTexture;
            var targetNormal = tl.normalMapTexture;
            for (int ti = 0; ti < terrains.Length; ti++)
            {
                var data = terrains[ti].terrainData;
                if (!data) continue;
                var layers = data.terrainLayers;
                if (layers == null || layers.Length == 0) continue;
                for (int i = 0; i < layers.Length; i++)
                {
                    var l = layers[i];
                    if (!l) continue;
                    if (ReferenceEquals(l, tl)) return true;
                    if (l.diffuseTexture == targetDiffuse && l.normalMapTexture == targetNormal) return true;
                    if (l.diffuseTexture == targetDiffuse) return true;
                    if (!targetDiffuse && !l.diffuseTexture && l.name == tl.name) return true;
                }
            }
            return false;
        }

        private int GetPriority(TerrainLayer tl)
        {
            if (!tl) return 0;
            if (IsLayerHeldByCoveredTerrains(tl)) return 2;
            if (HasEquivalentInCoveredTerrains(tl)) return 1;
            return 0;
        }

        private bool IsListMode() => _thumbSize <= ThumbMin;

        private void ApplyGridLayoutByMode()
        {
            if (_grid == null) return;
            if (IsListMode())
            {
                _grid.style.flexDirection = FlexDirection.Column;
                _grid.style.flexWrap = Wrap.NoWrap;
            }
            else
            {
                _grid.style.flexDirection = FlexDirection.Row;
                _grid.style.flexWrap = Wrap.Wrap;
            }
        }
    }
}
#endif