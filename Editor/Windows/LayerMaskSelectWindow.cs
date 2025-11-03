#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// LayerMaskSelectWindow
    /// 顶部列表（可搜索滚动）+ 底部参数（可拖拽分割调整高度）。
    /// 选中即应用到 RoadLayer.layerMask；遵循提前返回与单一职责原则。
    /// </summary>
    public class LayerMaskSelectWindow : EditorWindow
    {
        // 选择成功事件：向外部（Inspector）提供遮罩选择的行级更新
        public static event System.Action<__temp.MrPathV2.Runtime.Core.RoadLayer, __temp.MrPathV2.Runtime.Core.BlendMasks.BlendMaskBase> OnMaskApplied;
        private RoadLayer _targetLayer;
        private BlendMaskBase _selected;
        private BlendMaskBase _original; // 原始值：用于未应用时回滚
        private bool _applied;           // 是否已最终应用（双击）
        private UnityEditor.Editor _selectedEditor; // 参数区渲染

        private readonly List<BlendMaskBase> _masks = new();
        private readonly Dictionary<int, Texture2D> _iconCache = new();
        private Vector2 _listScroll;
        private Vector2 _paramScroll;
        private string _search = string.Empty;

        // 新建遮罩相关
        private Type[] _availableMaskTypes = Array.Empty<Type>();
        private int _createTypeIndex = 0;
        private string _newMaskName = "NewMask";
        private const string DefaultMaskFolder = "Assets/MrPathV2/Masks";

        // 可拖拽分割条（IMGUI遗留，不再使用）
        private float _listTopHeight = 240f;
        private bool _resizing;
        private const float SplitterHeight = 6f;

        // --- UITK 重构新增字段 ---
        private VisualElement _rootElement;
        private VisualElement _contentRoot;
        private ListView _listView;
        private VisualElement _details; // 绑定到 UXML 中的 MaskOSBox
        private ToolbarSearchField _searchField; // 绑定到 UXML 中的 ToolbarSearchField
        private Slider _thumbSlider; // 绑定到 UXML 中的 scaleImage
        private float _thumbSize = 42f; // 默认缩略图尺寸，受滑块驱动
        private Button _newBtn; // 来自 UXML（文本：新建噪声），可选
        private DropdownField _maskEnumDropdown; // 动态生成的“MaskEnum”，用于类型选择
        private TextField _nameField; // UXML中的名称输入（可选）
        private readonly List<BlendMaskBase> _visibleList = new();

        public static void Open(RoadLayer layer, BlendMaskBase current)
        {
            var win = GetWindow<LayerMaskSelectWindow>(true, "Layer Mask Select", true);
            win.minSize = new Vector2(520, 360);
            win.Initialize(layer, current);
            win.Show();
        }

        private void Initialize(RoadLayer layer, BlendMaskBase current)
        {
            _targetLayer = layer;
            _selected = current;
            _original = current;
            _availableMaskTypes = FindAvailableMaskTypes();
            if (_availableMaskTypes == null || _availableMaskTypes.Length == 0)
                _availableMaskTypes = new[] { typeof(BlendMaskBase) };
            RebuildList();
            RecreateEditor();
        }

        private void OnDisable()
        {
            if (_selectedEditor)
            {
                DestroyImmediate(_selectedEditor);
                _selectedEditor = null;
            }

            // 未最终应用则回滚到原始值，交互行为与 SelectTerrainLayerWindow 保持一致
            if (!_applied && _targetLayer != null)
            {
                _targetLayer.layerMask = _original;
            }
        }

        // 点击非窗口区域（失去焦点）自动关闭，符合 Unity 选择器交互
        private void OnLostFocus()
        {
            Close();
        }

        private void OnGUI()
        {
            // 完全重构为 UITK，IMGUI 渲染置空
            return;
        }

        // --- UITK: 构建界面 ---
        public void CreateGUI()
        {
            // 使用 UXML 构建界面，确保布局与效果图一致
            _rootElement = rootVisualElement;
            _rootElement.Clear();

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrPathV2/Editor/Windows/LayerMaskSelectWindow.uxml");
            if (vta != null)
            {
                vta.CloneTree(_rootElement);
            }
            else
            {
                // 兜底：若 UXML 未找到，维持最小可用界面
                _rootElement.style.flexDirection = FlexDirection.Column;
            }

            // 加载与地形选择窗口一致的样式（包含 :hover 和 .selected）
            var selSs = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/MrPathV2/Editor/Styles/SelectTerrainLayerWindow.uss");
            if (selSs) _rootElement.styleSheets.Add(selSs);

            // 绑定 UXML 元素
            _searchField = _rootElement.Q<ToolbarSearchField>("ToolbarSearchField");
            _thumbSlider = _rootElement.Q<Slider>("scaleImage");
            var listContainer = _rootElement.Q<VisualElement>("MaskList");
            _details = _rootElement.Q<VisualElement>("MaskOSBox") ?? new VisualElement { name = "MaskOSBox" };
            if (_details.parent == null) _rootElement.Add(_details);

            // 创建 ListView 并添加到 UXML 的列表容器
            _listView = new ListView
            {
                name = "MaskListView",
                selectionType = SelectionType.Single,
                showBorder = true,
                fixedItemHeight = Mathf.Max(64, _thumbSize + 20)
            };
            _listView.style.flexGrow = 1;
            _listView.makeItem = MakeListItem;
            _listView.bindItem = BindListItem;
            _listView.onSelectionChange += items =>
            {
                var m = items.FirstOrDefault() as BlendMaskBase;
                SelectMask(m);
                UpdateDetailsPanel();
                UpdateSelectionStyles();
            };
            _listView.onItemsChosen += items =>
            {
                var m = items.FirstOrDefault() as BlendMaskBase;
                ApplySelection(m);
            };
            if (listContainer != null) listContainer.Add(_listView); else _rootElement.Add(_listView);

            // 搜索框绑定
            if (_searchField != null)
            {
                _searchField.value = _search;
                _searchField.RegisterValueChangedCallback(ev =>
                {
                    _search = ev.newValue?.Trim() ?? string.Empty;
                    RebuildVisibleList();
                    _listView?.RefreshItems();
                });
            }

            // 动态生成 MaskEnum（替换 UXML 中的占位 EnumField）
            _availableMaskTypes = FindAvailableMaskTypes();
            var typeNames = (_availableMaskTypes ?? Array.Empty<Type>()).Select(t => t.Name).ToList();
            if (typeNames.Count == 0) typeNames.Add("BlendMaskBase");
            var enumPlaceholder = _rootElement.Query<EnumField>().First();
            _maskEnumDropdown = new DropdownField { name = "MaskEnum", choices = typeNames };
            _maskEnumDropdown.value = typeNames[Mathf.Clamp(_createTypeIndex, 0, typeNames.Count - 1)];
            _maskEnumDropdown.style.flexGrow = 1;
            _maskEnumDropdown.style.width = new StyleLength(new Length(69, LengthUnit.Percent));
            _maskEnumDropdown.RegisterValueChangedCallback(ev =>
            {
                var idx = typeNames.IndexOf(ev.newValue);
                _createTypeIndex = Mathf.Clamp(idx, 0, typeNames.Count - 1);
            });
            if (enumPlaceholder != null && enumPlaceholder.parent != null)
            {
                var p = enumPlaceholder.parent;
                var i = p.IndexOf(enumPlaceholder);
                p.Insert(Mathf.Max(0, i), _maskEnumDropdown);
                enumPlaceholder.RemoveFromHierarchy();
            }
            else
            {
                _rootElement.Add(_maskEnumDropdown);
            }

            // 绑定名称输入（若 UXML 提供）
            _nameField = _rootElement.Query<TextField>().ToList().FirstOrDefault(tf => !string.IsNullOrEmpty(tf.text) || !string.IsNullOrEmpty(tf.value));

            // 缩略图缩放绑定
            if (_thumbSlider != null)
            {
                _thumbSize = Mathf.Clamp(_thumbSlider.value, 16f, 128f);
                _thumbSlider.RegisterValueChangedCallback(ev =>
                {
                    _thumbSize = Mathf.Clamp(ev.newValue, 16f, 128f);
                    if (_listView != null) _listView.fixedItemHeight = Mathf.Max(64, _thumbSize + 20);
                    _listView?.RefreshItems(); // 让绑定更新图标尺寸
                });
            }

            // 可选：绑定“新建噪声”按钮（若存在）
            _newBtn = _rootElement.Query<Button>().ToList().FirstOrDefault(b => string.Equals(b.text, "新建噪声"));
            if (_newBtn != null)
            {
                _newBtn.clicked += () =>
                {
                    var types = _availableMaskTypes ?? Array.Empty<Type>();
                    var idx = Mathf.Clamp(_createTypeIndex, 0, types.Length - 1);
                    var type = types.Length > 0 ? types[idx] : typeof(BlendMaskBase);
                    var nameHint = _nameField != null ? _nameField.value : _newMaskName;
                    CreateNewMaskAsset(type, nameHint);
                    RebuildVisibleList();
                    _listView?.RefreshItems();
                };
            }

            // 数据初始化
            RebuildList();
            RebuildVisibleList();
            _listView.itemsSource = _visibleList;
            UpdateDetailsPanel();
            UpdateSelectionStyles();
        }

        private void RebuildVisibleList()
        {
            _visibleList.Clear();
            var query = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim();
            IEnumerable<BlendMaskBase> source = _masks.Where(m => m);
            if (query != null)
            {
                source = source.Where(m => m.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            _visibleList.AddRange(source.OrderBy(m => m.name));
        }

        private VisualElement MakeListItem()
        {
            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6; row.style.paddingRight = 6;
            row.AddToClassList("mrp-list-row"); // 复用现有列表行样式

            var icon = new Image { name = "icon" };
            icon.style.width = _thumbSize; icon.style.height = _thumbSize; icon.scaleMode = ScaleMode.ScaleToFit;
            icon.style.marginRight = 8;
            icon.AddToClassList("tile-icon"); // 启用 :hover 和 .selected 的边框动画
            icon.style.borderTopLeftRadius = 4;
            icon.style.borderTopRightRadius = 4;
            icon.style.borderBottomLeftRadius = 4;
            icon.style.borderBottomRightRadius = 4;
            var name = new Label { name = "name" };
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.fontSize = 14;
            var sub = new Label { name = "sub" };
            sub.style.color = new Color(0.8f, 0.8f, 0.8f);
            sub.style.opacity = 0.65f;

            var content = new VisualElement { name = "content" };
            content.style.flexDirection = FlexDirection.Column;
            content.style.flexGrow = 1;

            row.Add(icon);
            content.Add(name);
            content.Add(sub);
            row.Add(content);
            return row;
        }

        private void BindListItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleList.Count) return;
            var m = _visibleList[index];
            element.userData = m; // 绑定数据到容器，便于选中样式更新
            var row = element.Q<VisualElement>("row") ?? element;
            row.userData = m; // 行本身也存储数据，UpdateSelectionStyles 使用
            var icon = element.Q<Image>("icon");
            var name = element.Q<Label>("name");
            var sub = element.Q<Label>("sub");
            icon.image = GetMaskThumbnail(m);
            icon.style.width = _thumbSize; icon.style.height = _thumbSize; // 应用当前缩略图尺寸
            name.text = m ? m.name : "<null>";
            sub.text = m ? m.GetType().Name : string.Empty;
            // 在绑定阶段更新选中样式，避免首次渲染遗漏
            if (_selected == m) row.AddToClassList("selected"); else row.RemoveFromClassList("selected");
        }

        private Texture2D GetMaskThumbnail(BlendMaskBase m)
        {
            if (!m) return null;
            var id = m.GetInstanceID();
            if (_iconCache.TryGetValue(id, out var tex) && tex) return tex;
            tex = AssetPreview.GetMiniThumbnail(m);
            _iconCache[id] = tex;
            return tex;
        }

        private void UpdateDetailsPanel()
        {
            if (_details == null) return; // 提前返回
            _details.Clear();
            var target = _selected;
            if (!target)
            {
                _details.Add(new Label("未选择遮罩"));
                return; // 提前返回
            }

            // 为减少首次选中卡顿：延迟构建 Inspector 到下一帧
            _details.Add(new Label("正在加载参数…"));
            var captured = target;
            EditorApplication.delayCall += () =>
            {
                if (_details == null) return;
                // 选中已变化则取消
                if (_selected != captured) return;
                try
                {
                    _details.Clear();
                    var inspector = new InspectorElement(captured);
                    _details.Add(inspector);
                }
                catch
                {
                    try
                    {
                        if (_selectedEditor) { DestroyImmediate(_selectedEditor); _selectedEditor = null; }
                        _selectedEditor = UnityEditor.Editor.CreateEditor(captured);
                        var ui = _selectedEditor.CreateInspectorGUI();
                        if (ui != null)
                        {
                            _details.Add(ui);
                        }
                        else
                        {
                            _details.Add(new IMGUIContainer(() => _selectedEditor.OnInspectorGUI()));
                        }
                    }
                    catch (Exception ex)
                    {
                        _details.Add(new Label($"Inspector 构建失败: {ex.Message}"));
                    }
                }
            };
        }

        private void UpdateSelectionStyles()
        {
            if (_listView == null) return;
            var cc = _listView.contentContainer;
            if (cc == null) return;
            foreach (var row in cc.Children())
            {
                var data = row.userData as BlendMaskBase ?? row.Q<VisualElement>("row")?.userData as BlendMaskBase;
                if (data == null) { row.RemoveFromClassList("selected"); continue; }
                if (data == _selected) row.AddToClassList("selected"); else row.RemoveFromClassList("selected");
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            // 移除"遮罩列表"，只保留筛选；输入框水平铺满
            GUILayout.Label("筛选:", GUILayout.Width(40)); // 固定标签宽度
            var newSearch = GUILayout.TextField(_search, EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true)); // 仅文本框扩展
            if (!string.Equals(newSearch, _search))
            {
                _search = newSearch?.Trim() ?? string.Empty;
                Repaint();
            }
            // GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RebuildList();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMaskList(float height)
        {
            if (_masks == null || _masks.Count == 0)
            {
                EditorGUILayout.HelpBox("项目中未找到任何 BlendMask 资产。", MessageType.Info);
                return; // 提前返回
            }

            var query = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim();
            var filtered = (query == null ? _masks.Where(m => m) : _masks.Where(m => m && m.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                            .OrderBy(m => m.name)
                            .ToList();
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("筛选条件下无匹配遮罩。", MessageType.Info);
                return; // 提前返回
            }

            EditorGUILayout.BeginVertical(GUILayout.Height(height));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            foreach (var m in filtered) DrawMaskRow(m);
            EditorGUILayout.EndScrollView();

            // 底部“新建遮罩”区域（底对齐）
            EditorGUILayout.BeginHorizontal();
            var typeNames = _availableMaskTypes.Select(t => t.Name).ToArray();
            _createTypeIndex = EditorGUILayout.Popup(_createTypeIndex, typeNames, GUILayout.Width(140));
            _newMaskName = EditorGUILayout.TextField(_newMaskName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("新建遮罩", GUILayout.Width(88)))
            {
                CreateNewMaskAsset(_availableMaskTypes[Mathf.Clamp(_createTypeIndex, 0, _availableMaskTypes.Length - 1)], _newMaskName);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMaskRow(BlendMaskBase m)
        {
            var rowRect = EditorGUILayout.BeginHorizontal();
            Texture2D icon = null;
            var id = m.GetInstanceID();
            if (!_iconCache.TryGetValue(id, out icon) || !icon)
            {
                icon = AssetPreview.GetMiniThumbnail(m);
                _iconCache[id] = icon;
            }
            var isSelected = _selected == m;
            var isHover = rowRect.Contains(Event.current.mousePosition);
            var bg = isSelected ? new Color(0.2f, 0.6f, 0.2f, 0.15f) : (isHover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.03f));
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, rowRect.height), bg);

            GUILayout.Space(4);
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.Label(m.name, EditorStyles.label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 选中即应用
            var e = Event.current;
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
            {
                // 单击：预览选择，但不最终应用（窗口关闭时可回滚）
                SelectMask(m);
                if (e.clickCount == 2)
                {
                    // 双击：最终应用并关闭窗口
                    ApplySelection(m);
                }
                e.Use();
            }
        }

        private void DrawParamsPanel()
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("遮罩参数", EditorStyles.boldLabel);
            if (!_selected)
            {
                GUILayout.Label("未选择遮罩", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return; // 提前返回
            }
            _paramScroll = EditorGUILayout.BeginScrollView(_paramScroll, GUILayout.ExpandHeight(true));
            if (_selectedEditor)
            {
                try { _selectedEditor.OnInspectorGUI(); }
                catch (Exception ex) { EditorGUILayout.HelpBox($"渲染遮罩 Inspector 失败: {ex.Message}", MessageType.Error); }
            }
            else
            {
                EditorGUILayout.HelpBox("Inspector 构建失败", MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            // if (GUILayout.Button("关闭", GUILayout.Width(80), GUILayout.Height(24))) Close();
            EditorGUILayout.EndHorizontal();
        }

        private void ApplySelection(BlendMaskBase m)
        {
            if (_targetLayer == null)
            {
                EditorUtility.DisplayDialog("提示", "目标 RoadLayer 为空，无法应用。", "确定");
                return; // 提前返回
            }
            _selected = m;
            _targetLayer.layerMask = m;
            _applied = true;
            RecreateEditor();
            // 最终应用后通知，并关闭窗口（一致的交互）
            try { OnMaskApplied?.Invoke(_targetLayer, m); } catch { /* 防御：忽略回调异常 */ }
            Close();
        }

        // 单击选择：仅预览并刷新参数区，不触发最终应用事件
        private void SelectMask(BlendMaskBase m)
        {
            if (_targetLayer == null) return; // 提前返回
            _selected = m;
            _targetLayer.layerMask = m;
            RecreateEditor();
        }

        private void RebuildList()
        {
            _masks.Clear();
            _iconCache.Clear();
            var guids = AssetDatabase.FindAssets("t:BlendMaskBase");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<BlendMaskBase>(path);
                if (m)
                {
                    _masks.Add(m);
                    _iconCache[m.GetInstanceID()] = AssetPreview.GetMiniThumbnail(m);
                }
            }
            _masks.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        }

        private void RecreateEditor()
        {
            if (_selectedEditor) { DestroyImmediate(_selectedEditor); _selectedEditor = null; }
            if (_selected) _selectedEditor = UnityEditor.Editor.CreateEditor(_selected);
        }

        // --- 新建遮罩工具 ---
        private static Type[] FindAvailableMaskTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
        }

        private void CreateNewMaskAsset(Type maskType, string nameHint)
        {
            if (maskType == null || !typeof(BlendMaskBase).IsAssignableFrom(maskType))
            {
                EditorUtility.DisplayDialog("错误", "无效的遮罩类型。", "确定");
                return; // 提前返回
            }

            // 确保目标文件夹存在
            EnsureFolderExists(DefaultMaskFolder);
            var cleanName = string.IsNullOrWhiteSpace(nameHint) ? maskType.Name : nameHint.Trim();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DefaultMaskFolder}/{cleanName}.asset");

            var instance = ScriptableObject.CreateInstance(maskType) as BlendMaskBase;
            if (!instance)
            {
                EditorUtility.DisplayDialog("错误", "创建遮罩实例失败。", "确定");
                return; // 提前返回
            }

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 更新列表并应用到目标图层
            _masks.Add(instance);
            _iconCache[instance.GetInstanceID()] = AssetPreview.GetMiniThumbnail(instance);
            ApplySelection(instance);
            Repaint();
        }

        private static void EnsureFolderExists(string fullPath)
        {
            // fullPath 形如 Assets/AAA/BBB
            var parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var current = parts[0]; // 应为 Assets
            for (var i = 1; i < parts.Length; i++)
            {
                var next = parts[i];
                if (!AssetDatabase.IsValidFolder($"{current}/{next}"))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = $"{current}/{next}";
            }
        }

        private void DrawHeightSplitter()
        {
            var y = Mathf.Clamp(_listTopHeight, 140f, position.height - 180f);
            var splitterRect = new Rect(0, y, position.width, SplitterHeight);
            EditorGUI.DrawRect(splitterRect, new Color(0.2f, 0.2f, 0.2f, 0.35f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition)) { _resizing = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_resizing)
                    {
                        _listTopHeight = Mathf.Clamp(e.mousePosition.y, 140f, position.height - 180f);
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_resizing) { _resizing = false; e.Use(); }
                    break;
            }
        }
    }
}
#endif
