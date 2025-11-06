#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MrPathV2.Editor;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Providers;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    ///     基于 UITK + UXML 的 TerrainLayer 选择窗口。
    ///     - 单一职责：选择并应用 TerrainLayer 到 RoadLayer。
    ///     - 提前返回：所有空引用与异常情况快速退出。
    ///     - 高效：使用紧凑网格、仅必要刷新与最小化 GC。
    ///     - 现代：UI Toolkit 布局与事件、双击应用与预览更新。
    ///     兼容：保持与原 Open(...) 签名一致，供外部调用。
    /// </summary>
    public class SelectTerrainLayerWindow : EditorWindow
    {
        private const int ThumbMin = 48;
        private const int ThumbMax = 128;

        // 数据缓存
        private readonly List<TerrainLayer> _assetLayers = new List<TerrainLayer>();
        private readonly Dictionary<int, int> _coverageStats = new Dictionary<int, int>();
        private readonly List<TerrainLayer> _coveredLayers = new List<TerrainLayer>();

        // 可见列表
        private readonly List<TerrainLayer> _visibleList = new List<TerrainLayer>();
        private bool _applied;
        private Button _btnAll, _btnHeld;
        private PathCreator _contextPathCreator;
        private int _coveredTerrainTotal;
        private FilterMode _filterMode = FilterMode.All;
        private VisualElement _grid;
        private ScrollView _gridScroll;
        private VisualElement _iconBox;
        private GroupBox _layerBox;
        private Label _nameLabel, _sizeLabel, _covLabel, _pathLabel;
        private TerrainLayer _originalLayer;
        private string _search = string.Empty;

        // UXML 控件引用
        private ToolbarSearchField _searchField;
        private ToolbarPopupSearchField _searchFieldPopup;

        // 状态
        private TerrainLayer _selected;

        // 目标对象与上下文
        private RoadLayer _targetRoadLayer;

        // UI 缩略图尺寸
        private int _thumbSize = 72;
        private SliderInt _thumbSlider;
        private Slider _thumbSliderFloat;

        private void OnDisable()
        {
            if (!_applied && _targetRoadLayer != null)
            {
                _targetRoadLayer.contentLayer = _originalLayer;
                MarkPreviewDirty();
            }
        }

        public void CreateGUI()
        {
            // 加载 UXML 布局
            LoadUxml();
            // 查询控件
            QueryUIElements();
            // 构建网格滚动容器
            SetupGridContainer();
            // 事件绑定
            BindEvents();
            // 初始刷新与构建：确保打开窗口时最新列表与可见项
            RefreshLists();
            // 默认选中 All 标签
            SetFilterMode(FilterMode.All);
            UpdateDetailsPanel();
        }

        private void OnLostFocus() => Close();

        // 选择成功事件：供 StylizedRoadRecipeEditor 局部刷新行使用
        public static event Action<RoadLayer, TerrainLayer> OnContentLayerApplied;

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

        private void LoadUxml()
        {
            var vta = UIResourceLoader.LoadUxml(typeof(SelectTerrainLayerWindow));

            if (vta == null)
            {
                rootVisualElement.Add(new Label("缺少 UXML: SelectTerrainLayerWindow.uxml"));
                return; // 早退
            }
            vta.CloneTree(rootVisualElement);
        }

        private void QueryUIElements()
        {
            _searchFieldPopup = rootVisualElement.Q<ToolbarPopupSearchField>("ToolbarPopupSearchField");
            _searchField = rootVisualElement.Q<ToolbarSearchField>();
            _btnAll = rootVisualElement.Q<Button>("All");
            _btnHeld = rootVisualElement.Q<Button>("Holded");
            _layerBox = rootVisualElement.Q<GroupBox>("LayerBox");

            _iconBox = rootVisualElement.Q<VisualElement>("IconBox");
            _nameLabel = rootVisualElement.Q<Label>("Name");
            _sizeLabel = rootVisualElement.Q<Label>("Szie");
            _covLabel = rootVisualElement.Q<Label>("CoverTerrain");
            _pathLabel = rootVisualElement.Q<Label>("Path");
            _thumbSlider = rootVisualElement.Q<SliderInt>("IconScale");
            _thumbSliderFloat = rootVisualElement.Q<Slider>("IconScale");
        }

        private void SetupGridContainer()
        {
            _gridScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "GridScroll",
                style =
                {
                    flexGrow = 1
                }
            };

            _grid = new VisualElement
            {
                name = "Grid",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignContent = Align.FlexStart,
                    justifyContent = Justify.FlexStart
                }
            };

            _gridScroll.Add(_grid);

            if (_layerBox != null)
                _layerBox.Add(_gridScroll);
            else
                rootVisualElement.Add(_gridScroll);

            ApplyGridLayoutByMode();
        }

        private void BindEvents()
        {
            // 搜索字段事件绑定
            BindSearchFieldEvents();

            // 过滤按钮事件绑定
            BindFilterButtonEvents();

            // 缩略图缩放事件绑定
            BindThumbnailSliderEvents();
        }

        private void BindSearchFieldEvents()
        {
            if (_searchFieldPopup != null)
            {
                _searchFieldPopup.RegisterValueChangedCallback(ev =>
                {
                    _search = ev.newValue ?? string.Empty;
                    RebuildVisibleList();
                    RebuildGrid();
                });
            }
            else
            {
                _searchField?.RegisterValueChangedCallback(ev =>
                {
                    _search = ev.newValue ?? string.Empty;
                    RebuildVisibleList();
                    RebuildGrid();
                });
            }
        }

        private void BindFilterButtonEvents()
        {
            if (_btnAll != null)
                _btnAll.clicked += () => SetFilterMode(FilterMode.All);

            if (_btnHeld != null)
                _btnHeld.clicked += () => SetFilterMode(FilterMode.HeldOnly);
        }

        private void BindThumbnailSliderEvents()
        {
            if (_thumbSlider != null)
            {
                SetupIntSlider(_thumbSlider);
            }
            else if (_thumbSliderFloat != null)
            {
                SetupFloatSlider(_thumbSliderFloat);
            }
        }

        private void SetupIntSlider(SliderInt slider)
        {
            slider.lowValue = ThumbMin;
            slider.highValue = ThumbMax;
            slider.value = Mathf.Clamp(_thumbSize, ThumbMin, ThumbMax);
            slider.RegisterValueChangedCallback(ev =>
            {
                _thumbSize = Mathf.Clamp(ev.newValue, ThumbMin, ThumbMax);
                ApplyGridLayoutByMode();
                RebuildGrid();
            });
        }

        private void SetupFloatSlider(Slider slider)
        {
            slider.lowValue = ThumbMin;
            slider.highValue = ThumbMax;
            slider.value = Mathf.Clamp(_thumbSize, ThumbMin, ThumbMax);
            slider.RegisterValueChangedCallback(ev =>
            {
                _thumbSize = Mathf.Clamp(Mathf.RoundToInt(ev.newValue), ThumbMin, ThumbMax);
                ApplyGridLayoutByMode();
                RebuildGrid();
            });
        }

        // 列表与网格
        private void RebuildVisibleList()
        {
            _visibleList.Clear();
            IEnumerable<TerrainLayer> seq = _assetLayers;

            seq = ApplySearchFilter(seq);
            seq = ApplyModeFilter(seq);

            _visibleList.AddRange(seq);
            SortVisibleList();
        }

        private IEnumerable<TerrainLayer> ApplySearchFilter(IEnumerable<TerrainLayer> seq)
        {
            if (string.IsNullOrEmpty(_search))
                return seq;

            var s = _search.ToLowerInvariant();
            return seq.Where(l => l && l.name.ToLowerInvariant().Contains(s));
        }

        private IEnumerable<TerrainLayer> ApplyModeFilter(IEnumerable<TerrainLayer> seq)
        {
            return _filterMode switch
            {
                FilterMode.HeldOnly => seq.Where(IsLayerHeldByCoveredTerrains),
                FilterMode.MissingOnly => seq.Where(l => !IsLayerHeldByCoveredTerrains(l)),
                _ => seq
            };
        }

        private void SortVisibleList()
        {
            // 排序：绿色（持有）优先，黄色（等价）其次，其他最后
            _visibleList.Sort((a, b) =>
            {
                var priorityComparison = GetPriority(b).CompareTo(GetPriority(a));
                if (priorityComparison != 0)
                    return priorityComparison;

                var nameA = a ? a.name : string.Empty;
                var nameB = b ? b.name : string.Empty;
                return string.Compare(nameA, nameB, StringComparison.Ordinal);
            });
        }

        private void RebuildGrid()
        {
            if (_grid == null)
                return;

            _grid.Clear();

            // NullLayer 选项（允许清空）
            _grid.Add(MakeTile(null));

            foreach (var layer in _visibleList)
            {
                _grid.Add(MakeTile(layer));
            }
        }

        private VisualElement MakeTile(TerrainLayer tl)
        {
            var tile = new VisualElement
            {
                name = "Tile"
            };
            var icon = new VisualElement
            {
                name = "LayerIcon"
            };
            var nameLabel = new Label
            {
                name = "LayerName"
            };

            tile.Add(icon);
            tile.Add(nameLabel);

            // 布局配置
            SetupTileLayout(tile, icon, nameLabel);

            // 应用通用样式
            ApplyTileStyles(tile, icon);

            // 设置内容和交互
            SetupTileContent(icon, nameLabel, tl);
            SetupTileInteraction(tile, tl);

            return tile;
        }

        private void SetupTileLayout(VisualElement tile, VisualElement icon, Label nameLabel)
        {
            if (IsListMode())
            {
                ConfigureListModeLayout(tile, icon, nameLabel);
            }
            else
            {
                ConfigureGridModeLayout(tile, icon, nameLabel);
            }
        }

        private static void ConfigureListModeLayout(VisualElement tile, VisualElement icon, Label nameLabel)
        {
            // 列表模式
            tile.style.flexDirection = FlexDirection.Row;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.FlexStart;
            tile.style.width = Length.Percent(100);
            tile.style.height = 40; // 行高

            icon.style.width = 32;
            icon.style.height = 32;
            icon.style.marginLeft = 8;
            icon.style.marginRight = 8;

            // 层项间距缩小 1/2
            tile.style.marginLeft = 3;
            tile.style.marginRight = 3;
            tile.style.marginTop = 1;
            tile.style.marginBottom = 1;

            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        private void ConfigureGridModeLayout(VisualElement tile, VisualElement icon, Label nameLabel)
        {
            var tileSize = _thumbSize;

            // 网格模式
            tile.style.width = tileSize + 32;
            tile.style.height = tileSize + 46;

            // 层项间距缩小 1/2
            tile.style.marginLeft = 3;
            tile.style.marginRight = 3;
            tile.style.marginTop = 3;
            tile.style.marginBottom = 3;

            tile.style.flexDirection = FlexDirection.Column;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.FlexStart;

            icon.style.width = tileSize;
            icon.style.height = tileSize;
            icon.style.marginTop = 6;
            icon.style.marginBottom = 6;

            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private void ApplyTileStyles(VisualElement tile, VisualElement icon)
        {
            // 去除图标后方深色框（卡片底色清空）
            tile.style.borderBottomWidth = 0;
            tile.style.borderTopWidth = 0;
            tile.style.borderLeftWidth = 0;
            tile.style.borderRightWidth = 0;
            tile.style.backgroundColor = Color.clear;

            tile.style.borderTopLeftRadius = 8;
            tile.style.borderTopRightRadius = 8;
            tile.style.borderBottomLeftRadius = 8;
            tile.style.borderBottomRightRadius = 8;

            icon.style.borderBottomWidth = 1;
            icon.style.borderTopWidth = 1;
            icon.style.borderLeftWidth = 1;
            icon.style.borderRightWidth = 1;

            var borderColor = new Color(0.3f, 0.3f, 0.3f);
            icon.style.borderBottomColor = borderColor;
            icon.style.borderTopColor = borderColor;
            icon.style.borderLeftColor = borderColor;
            icon.style.borderRightColor = borderColor;

            // 图标圆角：列表模式减半（更直感的矩形视觉）
            ApplyIconBorderRadius(icon);
        }

        private void ApplyIconBorderRadius(VisualElement icon)
        {
            if (IsListMode())
            {
                icon.style.borderTopLeftRadius = 6;
                icon.style.borderTopRightRadius = 6;
                icon.style.borderBottomLeftRadius = 6;
                icon.style.borderBottomRightRadius = 6;
            }
            else
            {
                icon.style.borderTopLeftRadius = 12;
                icon.style.borderTopRightRadius = 12;
                icon.style.borderBottomLeftRadius = 12;
                icon.style.borderBottomRightRadius = 12;
            }
        }

        private void SetupTileContent(VisualElement icon, Label nameLabel, TerrainLayer tl)
        {
            // 缩略图与名称
            var tex = GetLayerThumbnail(tl);
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground((Texture2D)null);

            // Null 图标 50% 透明度
            icon.style.backgroundColor = tl ? Color.clear : new Color(0f, 0f, 0f, 0.25f);

            var rawName = tl ? tl.name : "Null";
            var maxChars = IsListMode() ? 22 : 12;
            nameLabel.text = TruncateName(rawName, maxChars);
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.tooltip = nameLabel.text;

            // 颜色：持有绿色、等价黄色、其他白色
            ApplyNameLabelColor(nameLabel, tl);
        }

        private void ApplyNameLabelColor(Label nameLabel, TerrainLayer tl)
        {
            var held = IsLayerHeldByCoveredTerrains(tl);
            var equivalent = !held && HasEquivalentInCoveredTerrains(tl);

            if (held)
                nameLabel.style.color = new Color(0.1f, 0.8f, 0.1f); // 绿色
            else if (equivalent)
                nameLabel.style.color = new Color(0.95f, 0.85f, 0.25f); // 黄色
            else
                nameLabel.style.color = Color.white;
        }

        private void SetupTileInteraction(VisualElement tile, TerrainLayer tl)
        {
            // 事件处理
            tile.RegisterCallback<MouseEnterEvent>(_ => SetTileHovered(tile, true));
            tile.RegisterCallback<MouseLeaveEvent>(_ => SetTileHovered(tile, false));
            tile.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                SelectTile(tl, tile);
                if (evt.clickCount == 2)
                {
                    ApplySelection(tl);
                }
            });

            if (_selected == tl)
                SetTileSelected(tile, true);
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
            if (_grid == null)
                return;

            foreach (var child in _grid.Children())
            {
                SetTileSelected(child, child == selectedTile);
            }
        }

        private static void SetTileSelected(VisualElement tile, bool selected)
        {
            var icon = tile.Q<VisualElement>("LayerIcon");
            if (icon == null)
                return;

            // 去除卡片底色，所有选中高亮集中在图标上
            tile.style.backgroundColor = Color.clear;

            if (selected)
            {
                // 橙色粗描边强调选中（贴近参考图）
                icon.style.borderBottomWidth = 3;
                icon.style.borderTopWidth = 3;
                icon.style.borderLeftWidth = 3;
                icon.style.borderRightWidth = 3;

                var orange = new Color(1f, 0.68f, 0.1f);
                icon.style.borderBottomColor = orange;
                icon.style.borderTopColor = orange;
                icon.style.borderLeftColor = orange;
                icon.style.borderRightColor = orange;
            }
            else
            {
                icon.style.borderBottomWidth = 1;
                icon.style.borderTopWidth = 1;
                icon.style.borderLeftWidth = 1;
                icon.style.borderRightWidth = 1;

                var gray = new Color(0.3f, 0.3f, 0.3f);
                icon.style.borderBottomColor = gray;
                icon.style.borderTopColor = gray;
                icon.style.borderLeftColor = gray;
                icon.style.borderRightColor = gray;
            }
        }

        private static void SetTileHovered(VisualElement tile, bool hovered)
        {
            var icon = tile.Q<VisualElement>("LayerIcon");
            if (icon == null)
                return;

            var selected = icon.style.borderBottomWidth.value > 2.4f; // 选中时宽度为 3
            if (selected)
                return;

            if (hovered)
            {
                // 滑动高亮：直接作用在图标边框上
                icon.style.borderBottomWidth = 2;
                icon.style.borderTopWidth = 2;
                icon.style.borderLeftWidth = 2;
                icon.style.borderRightWidth = 2;

                var hoverCol = new Color(0.85f, 0.85f, 0.85f);
                icon.style.borderBottomColor = hoverCol;
                icon.style.borderTopColor = hoverCol;
                icon.style.borderLeftColor = hoverCol;
                icon.style.borderRightColor = hoverCol;
            }
            else
            {
                // 恢复默认图标边框
                icon.style.borderBottomWidth = 1;
                icon.style.borderTopWidth = 1;
                icon.style.borderLeftWidth = 1;
                icon.style.borderRightWidth = 1;

                var gray = new Color(0.3f, 0.3f, 0.3f);
                icon.style.borderBottomColor = gray;
                icon.style.borderTopColor = gray;
                icon.style.borderLeftColor = gray;
                icon.style.borderRightColor = gray;
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
                var active = _filterMode == FilterMode.All;
                _btnAll.style.backgroundColor = active ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.17f, 0.17f, 0.17f);
                _btnAll.style.color = active ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.42f, 0.42f, 0.42f);
            }

            if (_btnHeld != null)
            {
                var active = _filterMode == FilterMode.HeldOnly;
                _btnHeld.style.backgroundColor = active ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.17f, 0.17f, 0.17f);
                _btnHeld.style.color = active ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.42f, 0.42f, 0.42f);
            }
        }

        private void UpdateDetailsPanel()
        {
            if (!AreDetailElementsAvailable())
                return;

            if (_selected == null)
            {
                UpdateDetailsForNullSelection();
                return;
            }

            UpdateDetailsForValidSelection();
        }

        private bool AreDetailElementsAvailable() => _nameLabel != null && _covLabel != null && _iconBox != null;

        private void UpdateDetailsForNullSelection()
        {
            _nameLabel.text = "未选择图层";
            _sizeLabel.text = string.Empty;
            _covLabel.text = $"覆盖数：0/{_coveredTerrainTotal}";
            _pathLabel.text = string.Empty;
            _iconBox.style.backgroundImage = new StyleBackground((Texture2D)null);

            // Null 图标在详情面板也保持 50% 透明
            _iconBox.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
        }

        private void UpdateDetailsForValidSelection()
        {
            _nameLabel.text = TruncateName(_selected.name, 24);

            _sizeLabel.text = _selected.diffuseTexture != null ? $"尺寸：{_selected.diffuseTexture.width}x{_selected.diffuseTexture.height}" : "尺寸：-";

            var cov = GetCoverageCount(_selected);
            _covLabel.text = $"覆盖数：{cov}/{_coveredTerrainTotal}";

            var path = AssetDatabase.GetAssetPath(_selected);
            _pathLabel.text = string.IsNullOrEmpty(path) ? "路径：-" : $"路径：{path}";

            var preview = GetLayerThumbnail(_selected);
            _iconBox.style.backgroundImage = preview != null ? new StyleBackground(preview) : new StyleBackground((Texture2D)null);
            _iconBox.style.backgroundColor = Color.clear;

            ApplyDetailIconStyles();
            ApplyDetailTextStyles();
        }

        private void ApplyDetailIconStyles()
        {
            _iconBox.style.borderTopLeftRadius = 12;
            _iconBox.style.borderTopRightRadius = 12;
            _iconBox.style.borderBottomLeftRadius = 12;
            _iconBox.style.borderBottomRightRadius = 12;

            _iconBox.style.borderBottomWidth = 1;
            _iconBox.style.borderTopWidth = 1;
            _iconBox.style.borderLeftWidth = 1;
            _iconBox.style.borderRightWidth = 1;

            var borderColor = new Color(0.3f, 0.3f, 0.3f);
            _iconBox.style.borderBottomColor = borderColor;
            _iconBox.style.borderTopColor = borderColor;
            _iconBox.style.borderLeftColor = borderColor;
            _iconBox.style.borderRightColor = borderColor;

            // 详情预览不参与缩放，固定尺寸，文字溢出省略
            _iconBox.style.width = 81;
            _iconBox.style.height = 74;
        }

        private void ApplyDetailTextStyles()
        {
            _nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _nameLabel.style.overflow = Overflow.Hidden;
            _nameLabel.tooltip = _selected.name;

            _sizeLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _sizeLabel.style.overflow = Overflow.Hidden;
            _sizeLabel.tooltip = _sizeLabel.text;

            _covLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _covLabel.style.overflow = Overflow.Hidden;
            _covLabel.tooltip = _covLabel.text;

            _pathLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _pathLabel.style.overflow = Overflow.Hidden;
            _pathLabel.tooltip = _pathLabel.text;
        }

        private void ApplySelection(TerrainLayer tl)
        {
            if (_targetRoadLayer == null)
            {
                ShowNotification(new GUIContent("目标 RoadLayer 为空，无法应用选择"));
                return;
            }

            // 若选择了有效 Layer，且存在路径上下文，则在应用前检测覆盖地形是否缺失该 Layer
            if (tl != null && _contextPathCreator != null)
            {
                HandleTerrainLayerCheck(tl);
            }

            ApplyTerrainLayer(tl);
            Close();
        }

        private void HandleTerrainLayerCheck(TerrainLayer tl)
        {
            try
            {
                var coveredTerrains = GetCoveredTerrains(_contextPathCreator);
                if (coveredTerrains.Count == 0)
                    return;

                var anyMissing = coveredTerrains.Any(t => !TerrainHoldsOrEquivalent(t, tl));
                if (!anyMissing)
                    return;

                // 三选项：添加到覆盖地形 / 仅选择(不添加) / 取消
                var choice = EditorUtility.DisplayDialogComplex(
                    "覆盖地形缺失图层",
                    $"选中的 TerrainLayer 在部分道路覆盖的地形中缺失：\n\n{tl.name}\n\n是否将其添加到所有覆盖地形？",
                    "添加到覆盖地形",
                    "仅选择(不添加)",
                    "取消");

                switch (choice)
                {
                    case 2: // 取消
                        return;
                    case 0: // 添加到所有覆盖地形
                        AddLayerToTerrains(coveredTerrains, tl);
                        break;
                }
                // choice == 1 仅选择：直接继续下面的应用流程
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SelectTerrainLayerWindow] 缺失Layer检测失败: {ex.Message}");
            }
        }

        private static void AddLayerToTerrains(List<UnityEngine.Terrain> terrains, TerrainLayer layer)
        {
            foreach (var t in terrains)
            {
                try
                {
                    EnsureLayerPresentOnTerrain(t, layer);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SelectTerrainLayerWindow] 添加图层到地形时出错: {ex.Message}");
                }
            }
        }

        private void ApplyTerrainLayer(TerrainLayer tl)
        {
            var owner = _contextPathCreator != null ? _contextPathCreator.profile : null;
            if (owner != null)
                Undo.RecordObject(owner, "Select Terrain Layer");

            _targetRoadLayer.contentLayer = tl;

            if (owner != null)
                EditorUtility.SetDirty(owner);

            _applied = true;

            // 通知外部（Inspector）进行对应行的轻量刷新
            try
            {
                OnContentLayerApplied?.Invoke(_targetRoadLayer, tl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SelectTerrainLayerWindow] 通知外部选择变更时出错: {ex.Message}");
            }

            MarkPreviewDirty();
        }

        private void MarkPreviewDirty()
        {
            try
            {
                var type = Type.GetType("MrPathV2.Editor.Preview.MultiPathPreviewRenderer, Assembly-CSharp-Editor");
                if (type == null)
                    return;

                var method = type.GetMethod("MarkCreatorDirty", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return;

                method.Invoke(null, new object[]
                {
                    _contextPathCreator, false, false, true
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SelectTerrainLayerWindow] 更新预览失败: {ex.Message}");
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
                if (tl != null)
                    result.Add(tl);
            }

            // 去重并排序
            return result
                .GroupBy(l => l.GetInstanceID())
                .Select(g => g.First())
                .OrderBy(l => l.name)
                .ToList();
        }

        private List<TerrainLayer> CollectCoveredTerrainLayers()
        {
            var result = new List<TerrainLayer>();
            var terrains = FindObjectsOfType<UnityEngine.Terrain>();
            _coveredTerrainTotal = terrains?.Length ?? 0;

            if (!AreTerrainsAvailable(terrains))
                return result;

            ProcessTerrainLayers(terrains, result);
            return DeduplicateAndSortLayers(result);
        }

        private static bool AreTerrainsAvailable(UnityEngine.Terrain[] terrains) => terrains is { Length: > 0 };

        private static void ProcessTerrainLayers(UnityEngine.Terrain[] terrains, List<TerrainLayer> result)
        {
            result.AddRange(from terrain in terrains
                where
                    terrain.terrainData != null
                select terrain.terrainData.terrainLayers
                into layers
                where layers != null && layers.Length != 0
                from layer in layers
                where layer != null
                select layer);
        }

        private static List<TerrainLayer> DeduplicateAndSortLayers(List<TerrainLayer> layers)
        {
            // 去重并排序
            return layers
                .GroupBy(l => l.GetInstanceID())
                .Select(g => g.First())
                .OrderBy(l => l.name)
                .ToList();
        }

        private void RebuildCoverageStats()
        {
            _coverageStats.Clear();
            var terrains = FindObjectsOfType<UnityEngine.Terrain>();
            _coveredTerrainTotal = terrains?.Length ?? 0;

            if (!AreTerrainsAvailable(terrains))
                return;

            ProcessTerrainCoverageStats(terrains);
        }

        private void ProcessTerrainCoverageStats(UnityEngine.Terrain[] terrains)
        {
            foreach (var terrain in terrains)
            {
                if (!IsTerrainDataAvailable(terrain))
                    continue;

                var layers = terrain.terrainData.terrainLayers;
                if (!AreLayersAvailable(layers))
                    continue;

                ProcessTerrainLayersForStats(layers);
            }
        }

        private static bool IsTerrainDataAvailable(UnityEngine.Terrain terrain) => terrain.terrainData != null;

        private static bool AreLayersAvailable(TerrainLayer[] layers) => layers is { Length: > 0 };

        private void ProcessTerrainLayersForStats(TerrainLayer[] layers)
        {
            foreach (var layer in layers)
            {
                if (layer == null)
                    continue;

                var id = layer.GetInstanceID();
                if (!_coverageStats.TryAdd(id, 1))
                    _coverageStats[id]++;
            }
        }

        private int GetCoverageCount(TerrainLayer tl)
        {
            if (tl == null)
                return 0;

            var id = tl.GetInstanceID();
            return _coverageStats.GetValueOrDefault(id, 0);
        }

        private bool IsLayerHeldByCoveredTerrains(TerrainLayer tl)
        {
            if (tl == null || _coveredLayers == null || _coveredLayers.Count == 0)
                return false;

            var id = tl.GetInstanceID();
            return _coveredLayers.Any(l => l != null && l.GetInstanceID() == id);
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
            if (tl == null)
                return false;

            var terrains = FindObjectsOfType<UnityEngine.Terrain>();
            if (!AreTerrainsAvailable(terrains))
                return false;

            var targetDiffuse = tl.diffuseTexture;
            var targetNormal = tl.normalMapTexture;

            return CheckForEquivalentLayer(terrains, tl, targetDiffuse, targetNormal);
        }

        private bool CheckForEquivalentLayer(UnityEngine.Terrain[] terrains, TerrainLayer targetLayer,
            Texture2D targetDiffuse, Texture2D targetNormal)
        {
            foreach (var terrain in terrains)
            {
                if (!IsTerrainDataAvailable(terrain))
                    continue;

                var layers = terrain.terrainData.terrainLayers;
                if (!AreLayersAvailable(layers))
                    continue;

                if (ContainsEquivalentLayer(layers, targetLayer, targetDiffuse, targetNormal))
                    return true;
            }

            return false;
        }

        private static bool ContainsEquivalentLayer(TerrainLayer[] layers, TerrainLayer targetLayer,
            Texture2D targetDiffuse, Texture2D targetNormal)
        {
            return layers.Where(layer => layer != null).Any(layer => IsEquivalentLayer(layer, targetLayer, targetDiffuse, targetNormal));

        }

        private static bool IsEquivalentLayer(TerrainLayer layer, TerrainLayer targetLayer,
            Texture2D targetDiffuse, Texture2D targetNormal)
        {
            // 直接引用相等
            if (ReferenceEquals(layer, targetLayer))
                return true;

            // 纹理完全匹配
            if (layer.diffuseTexture == targetDiffuse && layer.normalMapTexture == targetNormal)
                return true;

            // 仅漫反射纹理匹配
            if (layer.diffuseTexture == targetDiffuse)
                return true;

            // 无纹理但名称匹配
            if (targetDiffuse == null && layer.diffuseTexture == null && layer.name == targetLayer.name)
                return true;

            return false;
        }

        private int GetPriority(TerrainLayer tl)
        {
            if (tl == null)
                return 0;

            if (IsLayerHeldByCoveredTerrains(tl))
                return 2;

            return HasEquivalentInCoveredTerrains(tl) ? 1 : 0;
        }

        private bool IsListMode() => _thumbSize <= ThumbMin;

        private void ApplyGridLayoutByMode()
        {
            if (_grid == null)
                return;

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

        // -------- 覆盖地形检测与辅助 --------

        private static List<UnityEngine.Terrain> GetCoveredTerrains(PathCreator creator)
        {
            var result = new List<UnityEngine.Terrain>();

            if (!IsCreatorValid(creator))
                return result;

            try
            {
                ProcessTerrainCoverage(creator, result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GetCoveredTerrains] {ex.Message}");
            }

            return result;
        }

        private static bool IsCreatorValid(PathCreator creator) => creator != null && creator.profile != null &&
                                                                   creator.pathData is { KnotCount: >= 2 };

        private static void ProcessTerrainCoverage(PathCreator creator, List<UnityEngine.Terrain> result)
        {
            // 采样路径脊线
            var heightProvider = new TerrainHeightProvider();
            var spine = PathSampler.SamplePath(creator, heightProvider);

            // 计算扩展边界
            var bounds = GetExpandedXZBounds(spine, creator.profile);

            // 查找相交地形
            var terrains = UnityEngine.Terrain.activeTerrains;
            result.AddRange(from terrain in terrains
                where IsTerrainValid(terrain)
                let tb = GetTerrainBounds(terrain)
                where BoundsOverlap(bounds, tb)
                select terrain);
        }

        private static bool IsTerrainValid(UnityEngine.Terrain terrain) => terrain != null && terrain.terrainData != null;

        private static Vector4 GetExpandedXZBounds(PathSpine spine, PathProfile profile)
        {
            if (spine.Points == null || spine.VertexCount == 0)
                return new Vector4(0, 0, 0, 0);

            var halfWidth = profile.roadWidth * 0.5f + profile.falloffWidth;
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (var i = 0; i < spine.VertexCount; i++)
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

        private static bool BoundsOverlap(Vector4 a, Vector4 b) => !(a.z <= b.x || a.x >= b.z || a.w <= b.y || a.y >= b.w);

        private static bool TerrainHoldsOrEquivalent(UnityEngine.Terrain terrain, TerrainLayer layer)
        {
            if (!IsTerrainAndLayerValid(terrain, layer))
                return false;

            var layers = terrain.terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
            var targetDiffuse = layer.diffuseTexture;
            var targetNormal = layer.normalMapTexture;

            return CheckForEquivalentLayerInTerrain(layers, layer, targetDiffuse, targetNormal);
        }

        private static bool IsTerrainAndLayerValid(UnityEngine.Terrain terrain, TerrainLayer layer) => terrain != null && terrain.terrainData != null && layer != null;

        private static bool CheckForEquivalentLayerInTerrain(TerrainLayer[] layers, TerrainLayer targetLayer,
            Texture2D targetDiffuse, Texture2D targetNormal)
        {
            return layers.Where(l => l != null).Any(l => IsEquivalentLayer(l, targetLayer, targetDiffuse, targetNormal));

        }

        private static void EnsureLayerPresentOnTerrain(UnityEngine.Terrain terrain, TerrainLayer layer)
        {
            if (!IsTerrainAndLayerValid(terrain, layer))
                return;

            if (TerrainHoldsOrEquivalent(terrain, layer))
                return; // 已存在或等价，提前返回

            AddLayerToTerrain(terrain, layer);
        }

        private static void AddLayerToTerrain(UnityEngine.Terrain terrain, TerrainLayer layer)
        {
            var td = terrain.terrainData;
            var list = new List<TerrainLayer>(td.terrainLayers ?? Array.Empty<TerrainLayer>());

            Undo.RegisterCompleteObjectUndo(td, "添加地形图层");

            var insertIndex = FindEmptySlot(list);

            if (insertIndex >= 0)
                list[insertIndex] = layer;
            else
                list.Add(layer);

            td.terrainLayers = list.ToArray();
            EditorUtility.SetDirty(td);
        }

        private static int FindEmptySlot(List<TerrainLayer> layers)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Texture2D GetLayerThumbnail(TerrainLayer tl)
        {
            if (tl == null)
                return null;

            if (tl.diffuseTexture != null)
                return tl.diffuseTexture;

            var tex = AssetPreview.GetAssetPreview(tl);
            return tex ? tex : AssetPreview.GetMiniThumbnail(tl);
        }

        // 名称截断：过长时补充省略号，保持单行优雅
        private static string TruncateName(string input, int maxChars)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (maxChars <= 3 || input.Length <= maxChars)
                return input;

            return input[..(maxChars - 3)] + "...";
        }

        // 过滤模式
        private enum FilterMode
        {
            All,
            HeldOnly,
            MissingOnly
        }
    }
}
#endif
