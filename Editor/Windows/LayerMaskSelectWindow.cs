#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using __temp.MrPathV2.Editor;

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
        public static event Action<RoadLayer, BlendMaskBase> OnMaskApplied;
        private RoadLayer _targetLayer;
        private BlendMaskBase _selected;
        private BlendMaskBase _original; // 原始值：用于未应用时回滚
        private bool _applied;           // 是否已最终应用（双击）
        private UnityEditor.Editor _selectedEditor; // 参数区渲染

        private readonly List<BlendMaskBase> _masks = new();
        private readonly Dictionary<int, Texture2D> _iconCache = new();
        // 移除 IMGUI 滚动状态字段，UITK 不需要这些
        private string _search = string.Empty;

        // 新建遮罩相关
        private Type[] _availableMaskTypes = Array.Empty<Type>();
        private int _createTypeIndex = 0;
        private string _newMaskName = "NewMask";
        private const string DefaultMaskFolder = "Assets/MrPathV2/Masks";

        // 已移除拖拽分割条相关字段（保留UITK固定高度实现）

        // --- UITK 重构新增字段 ---
        private VisualElement _rootElement;
        private VisualElement _contentRoot;
        private ListView _listView;
        private VisualElement _details; // 绑定到 UXML 中的 MaskOSBox
        private ToolbarSearchField _searchField; // 绑定到 UXML 中的 ToolbarSearchField
        private float _thumbSize = 42f; // 默认缩略图尺寸（固定，已移除缩放控件）
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

        // 取消失焦自动关闭，确保新建遮罩与编辑参数时窗口保持打开
        private void OnLostFocus()
        {
            // 保持窗口，不执行 Close()
        }



        // --- UITK: 构建界面 ---
        public void CreateGUI()
        {
            // 使用 UXML 构建界面，确保布局与效果图一致
            _rootElement = rootVisualElement;
            _rootElement.Clear();

            var vta = UIResourceLoader.LoadUxml(typeof(LayerMaskSelectWindow));
            if (vta != null)
            {
                vta.CloneTree(_rootElement);
            }
            else
            {
                // 兜底：若 UXML 未找到，维持最小可用界面
                _rootElement.style.flexDirection = FlexDirection.Column;
            }

            // 不加载外部样式以避免橙色选中效果

            // 绑定 UXML 元素
            _searchField = _rootElement.Q<ToolbarSearchField>("ToolbarSearchField");
            var listContainer = _rootElement.Q<VisualElement>("MaskList");
            _details = _rootElement.Q<VisualElement>("MaskOSBox") ?? new VisualElement { name = "MaskOSBox" };
            if (_details.parent == null) _rootElement.Add(_details);

            // 创建 ListView 并添加到 UXML 的列表容器
            _listView = new ListView
            {
                name = "MaskListView",
                selectionType = SelectionType.Single,
                showBorder = false,
                fixedItemHeight = 52
            };
            _listView.style.flexGrow = 1;
            _listView.makeItem = MakeListItem;
            _listView.bindItem = BindListItem;
            _listView.onSelectionChange += items =>
            {
                var m = items.FirstOrDefault() as BlendMaskBase;
                ApplySelection(m);
                UpdateDetailsPanel();
            };
            _listView.onItemsChosen += items =>
            {
                var m = items.FirstOrDefault() as BlendMaskBase;
                ApplySelection(m);
                // 双击确认后关闭窗口
                Close();
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


            _availableMaskTypes = FindAvailableMaskTypes();
            var typeNames = (_availableMaskTypes ?? Array.Empty<Type>()).Select(t => t.Name).ToList();
            if (typeNames.Count == 0) typeNames.Add("BlendMaskBase");
            var maskDropdown = _rootElement.Q<DropdownField>("MaskDropDownField");
            maskDropdown.name = "Masks";
            maskDropdown.choices = typeNames;
            maskDropdown.value = typeNames[Mathf.Clamp(_createTypeIndex, 0, typeNames.Count - 1)];
            maskDropdown.RegisterValueChangedCallback(ev =>
            {
                var idx = typeNames.IndexOf(ev.newValue);
                _createTypeIndex = Mathf.Clamp(idx, 0, typeNames.Count - 1);
            });


            _rootElement.Add(_maskEnumDropdown);



            _nameField = _rootElement.Q<TextField>("MaskName");
            if (_nameField == null)
            {
                Debug.Log("TextField name is not matching!");
            }

            // 使用固定缩略图尺寸 40x40
            _thumbSize = 40f;


            _newBtn = _rootElement.Q<Button>("CreateMask");

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
            // 默认选中 Layer 当前遮罩
            var defaultIndex = (_selected != null) ? _visibleList.IndexOf(_selected) : -1;
            if (defaultIndex < 0) defaultIndex = 0;
            _listView.SetSelection(defaultIndex);
            UpdateDetailsPanel();

            // 固定参数区初始高度（移除分割条与拖拽）
            // var detailsGroup = _details.parent;
            // if (detailsGroup != null) detailsGroup.style.flexGrow = 0;
            // _details.style.height = 820f;
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
            // 顶部添加 Null 项用于清空遮罩槽位
            _visibleList.Insert(0, null);
        }

        private VisualElement MakeListItem()
        {
            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6; row.style.paddingRight = 6;
            row.style.paddingTop = 6; row.style.paddingBottom = 6;
            row.style.marginBottom = 0;
            row.style.height = 52; // 固定行高，避免 ListView 选中偏移
            row.style.overflow = Overflow.Hidden;
            row.style.flexShrink = 0;

            var icon = new Image { name = "icon" };
            icon.style.width = _thumbSize; icon.style.height = _thumbSize; icon.scaleMode = ScaleMode.ScaleToFit;
            icon.style.marginRight = 8;
            var name = new Label { name = "name" };
            name.style.unityFontStyleAndWeight = FontStyle.Normal;
            name.style.fontSize = 13;
            var sub = new Label { name = "sub" };
            sub.style.color = new Color(0.8f, 0.8f, 0.8f);
            sub.style.opacity = 0.65f;
            sub.style.marginTop = 0;

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

            // 处理图标显示，为null时使用半透明黑色圆角图标
            if (m == null)
            {
                // 设置默认的半透明黑色圆角图标
                icon.image = CreateNullIcon();
                name.text = "Null";
                sub.text = "None";
            }
            else
            {
                // 使用正常的缩略图
                icon.image = GetMaskThumbnail(m);
                name.text = m.name;
                sub.text = m.GetType().Name;
            }

            // 统一应用缩略图尺寸
            icon.style.width = _thumbSize;
            icon.style.height = _thumbSize;
            // 添加圆角样式
            icon.style.borderTopLeftRadius = 8;
            icon.style.borderTopRightRadius = 8;
            icon.style.borderBottomLeftRadius = 8;
            icon.style.borderBottomRightRadius = 8;
        }

        // 创建半透明黑色圆角图标
        private Texture2D CreateNullIcon()
        {
            // 创建一个简单的2D纹理作为默认图标
            int size = 32; // 图标尺寸
            Texture2D nullIcon = new Texture2D(size, size);
            Color32[] pixels = new Color32[size * size];

            // 设置半透明黑色 (alpha值设为100，范围0-255)
            Color32 nullColor = new(0, 0, 0, 42);

            // 填充所有像素
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = nullColor;
            }

            nullIcon.SetPixels32(pixels);
            nullIcon.Apply();
            return nullIcon;
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



        private void ApplySelection(BlendMaskBase m)
        {
            if (_targetLayer == null) return; // 移除提示框，保持静默提前返回
            _selected = m;
            _targetLayer.layerMask = m;
            _applied = true;
            RecreateEditor();
            // 最终应用后通知，并关闭窗口（一致的交互）
            try { OnMaskApplied?.Invoke(_targetLayer, m); } catch { /* 防御：忽略回调异常 */ }
            // 单击应用不关闭窗口，双击由 onItemsChosen 关闭
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

    }
}
#endif
