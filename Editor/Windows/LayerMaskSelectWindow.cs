#if UNITY_EDITOR
using System;
using System.IO;
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
        // 新入口：作为 Mask 资产管理器打开（不绑定 RoadLayer）
        [MenuItem("MrPath/Masks/Mask Manager")]
        public static void OpenManager()
        {
            Open(null, null);
        }
        private static LayerMaskSelectWindow s_Instance; // 单例实例

        // 选择成功事件：向外部（Inspector）提供遮罩选择的行级更新
        public static event Action<RoadLayer, BlendMaskBase> OnMaskApplied;
        private RoadLayer _targetLayer;
        private BlendMaskBase _selected;
        private BlendMaskBase _original; // 原始值：用于未应用时回滚
        private bool _applied; // 是否已最终应用（双击）
        private UnityEditor.Editor _selectedEditor; // 参数区渲染

        private readonly List<BlendMaskBase> _masks = new List<BlendMaskBase>();
        private readonly Dictionary<int, Texture2D> _iconCache = new Dictionary<int, Texture2D>();
        // 移除 IMGUI 滚动状态字段，UITK 不需要这些
        private string _search = string.Empty;

        // 新建遮罩相关
        private Type[] _availableMaskTypes = Array.Empty<Type>();
        private int _createTypeIndex;
        private const string NewMaskName = "NewMask";

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
        private readonly List<BlendMaskBase> _visibleList = new List<BlendMaskBase>();
        // 遮罩使用信息缓存：key 为 Mask InstanceID，value 为精简的引用摘要
        private readonly Dictionary<int, string> _usageLabelCache = new Dictionary<int, string>();

        public static void Open(RoadLayer layer, BlendMaskBase current)
        {
            // 单例：若已打开则聚焦并复用（避免多窗口资源占用）
            if (s_Instance != null)
            {
                s_Instance.minSize = new Vector2(520, 360);
                s_Instance.Initialize(layer, current);
                s_Instance.Focus();
                return;
            }

            var win = GetWindow<LayerMaskSelectWindow>(true, "Layer Mask Select", true);
            s_Instance = win;
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
                _availableMaskTypes = new[]
                {
                    typeof(BlendMaskBase)
                };
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

        private void OnDestroy()
        {
            // 释放单例引用
            if (s_Instance == this) s_Instance = null;
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
            _details = _rootElement.Q<VisualElement>("MaskOSBox") ?? new VisualElement
            {
                name = "MaskOSBox"
            };
            if (_details.parent == null) _rootElement.Add(_details);

            // 创建 ListView 并添加到 UXML 的列表容器
            _listView = new ListView
            {
                name = "MaskListView",
                selectionType = SelectionType.Multiple,
                showBorder = false,
                fixedItemHeight = 52,
                style =
                {
                    flexGrow = 1
                },
                makeItem = MakeListItem,
                bindItem = BindListItem
            };
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
            if (listContainer != null) listContainer.Add(_listView);
            else _rootElement.Add(_listView);

            // 快捷键：F2重命名、Delete批量删除
            _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown);

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
            if (maskDropdown != null)
            {
                maskDropdown.name = "Masks";
                maskDropdown.choices = typeNames;
                maskDropdown.value = typeNames[Mathf.Clamp(_createTypeIndex, 0, typeNames.Count - 1)];
                maskDropdown.RegisterValueChangedCallback(ev =>
                {
                    var idx = typeNames.IndexOf(ev.newValue);
                    _createTypeIndex = Mathf.Clamp(idx, 0, typeNames.Count - 1);
                });
                _maskEnumDropdown = maskDropdown;
            }


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
                    var nameHint = _nameField != null ? _nameField.value : NewMaskName;
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
            // 确保默认选中项在可视区域内，提升可用性
            _listView.ScrollToItem(defaultIndex);
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
            var source = _masks.Where(m => m);
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
            var row = new VisualElement
            {
                name = "row",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 6,
                    paddingBottom = 6,
                    marginBottom = 0,
                    height = 52, // 固定行高，避免 ListView 选中偏移
                    overflow = Overflow.Hidden,
                    flexShrink = 0
                }
            };

            // 行级右键菜单：删除/重命名/复制（含撤销支持）
            row.AddManipulator(new ContextualMenuManipulator(PopulateRowContextMenu));

            var icon = new Image
            {
                name = "icon",
                style =
                {
                    width = _thumbSize,
                    height = _thumbSize,
                    marginRight = 8
                },
                scaleMode = ScaleMode.ScaleToFit
            };
            var mName = new Label
            {
                name = "name",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Normal,
                    fontSize = 13
                }
            };
            var sub = new Label
            {
                name = "sub",
                style =
                {
                    color = new Color(0.8f, 0.8f, 0.8f),
                    opacity = 0.65f,
                    marginTop = 0
                }
            };

            var content = new VisualElement
            {
                name = "content",
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };

            // 行尾：使用标记徽标（仅在有引用时显示）
            var tail = new VisualElement
            {
                name = "tail",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    flexShrink = 0,
                }
            };
            var usage = new Label
            {
                name = "usage",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Normal,
                    fontSize = 11,
                    color = new Color(0.82f, 0.9f, 0.82f, 0.95f),
                    backgroundColor = new Color(0.18f, 0.64f, 0.32f, 0.16f),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 2,
                    paddingBottom = 2,
                    marginLeft = 10,
                    borderTopLeftRadius = 999,
                    borderTopRightRadius = 999,
                    borderBottomLeftRadius = 999,
                    borderBottomRightRadius = 999,
                    overflow = Overflow.Hidden,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    display = DisplayStyle.None
                }
            };
            tail.Add(usage);

            row.Add(icon);
            content.Add(mName);
            content.Add(sub);
            row.Add(content);
            row.Add(tail);
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
            var mName = element.Q<Label>("name");
            var sub = element.Q<Label>("sub");
            var usage = element.Q<Label>("usage");

            // 处理图标显示，为null时使用半透明黑色圆角图标
            if (m == null)
            {
                // 设置默认的半透明黑色圆角图标
                icon.image = CreateNullIcon();
                mName.text = "Null";
                sub.text = "None";
                if (usage != null) usage.style.display = DisplayStyle.None;
            }
            else
            {
                // 使用正常的缩略图
                icon.image = GetMaskThumbnail(m);
                mName.text = m.name;
                sub.text = m.GetType().Name;
                // 行尾使用标记：仅在被引用时显示，文本为精简摘要
                if (usage != null)
                {
                    var id = m.GetInstanceID();
                    if (_usageLabelCache.TryGetValue(id, out var summary) && !string.IsNullOrEmpty(summary))
                    {
                        usage.text = summary;
                        usage.tooltip = summary;
                        usage.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        usage.text = string.Empty;
                        usage.tooltip = null;
                        usage.style.display = DisplayStyle.None;
                    }
                }
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

        // 右键菜单构建（按行数据）
        private void PopulateRowContextMenu(ContextualMenuPopulateEvent evt)
        {
            var targetVe = evt.target as VisualElement;
            var mask = GetMaskFromElement(targetVe);

            // 为 Null 项提供快捷清空应用；其他项提供增删改
            if (mask == null)
            {
                evt.menu.AppendAction("Clear and Apply to Layer", _ => ApplySelection(null));
                return; // 提前返回
            }

            evt.menu.AppendAction("Ping Asset", _ => EditorGUIUtility.PingObject(mask));
            evt.menu.AppendAction("Rename", _ => BeginInlineRename(GetRowElementFromChild(targetVe), mask));
            evt.menu.AppendAction("Duplicate", _ => DuplicateMaskAsset(mask));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete", _ => DeleteMaskAsset(mask));
        }

        private static BlendMaskBase GetMaskFromElement(VisualElement ve)
        {
            var cur = ve;
            while (cur != null)
            {
                if (cur.userData is BlendMaskBase bm) return bm;
                cur = cur.parent;
            }
            return null;
        }

        private void BeginInlineRename(VisualElement row, BlendMaskBase mask)
        {
            if (row == null || !mask) return;
            var nameLabel = row.Q<Label>("name");
            var nameEdit = row.Q<TextField>("name-edit");
            if (nameEdit == null)
            {
                nameEdit = new TextField
                {
                    name = "name-edit",
                    style =
                    {
                        display = DisplayStyle.None
                    }
                };
                var content = row.Q<VisualElement>("content") ?? row;
                content.Add(nameEdit);
            }

            nameEdit.value = mask.name;
            nameEdit.style.display = DisplayStyle.Flex;
            if (nameLabel != null) nameLabel.style.display = DisplayStyle.None;
            nameEdit.Focus();

            void Cancel()
            {
                nameEdit.style.display = DisplayStyle.None;
                if (nameLabel != null) nameLabel.style.display = DisplayStyle.Flex;
            }

            nameEdit.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Commit();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Cancel();
                    e.StopPropagation();
                }
            });
            nameEdit.RegisterCallback<FocusOutEvent>(_ => Commit());
            return;

            void Commit()
            {
                var newName = nameEdit.value?.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    Cancel();
                    return;
                }
                Undo.RegisterCompleteObjectUndo(mask, "Rename Mask");
                mask.name = newName;
                EditorUtility.SetDirty(mask);
                AssetDatabase.SaveAssets();
                RebuildList();
                RebuildVisibleList();
                _listView.itemsSource = _visibleList;
                _listView.RefreshItems();
                if (nameLabel != null)
                {
                    nameLabel.text = newName;
                    nameLabel.style.display = DisplayStyle.Flex;
                }
                nameEdit.style.display = DisplayStyle.None;
            }
        }

        private static VisualElement GetRowElementFromChild(VisualElement ve)
        {
            while (ve != null)
            {
                if (ve.name == "row") return ve;
                ve = ve.parent;
            }
            return null;
        }

        private void DuplicateMaskAsset(BlendMaskBase mask)
        {
            if (!mask) return; // 提前返回
            var srcPath = AssetDatabase.GetAssetPath(mask);
            if (string.IsNullOrEmpty(srcPath)) return; // 提前返回

            var folder = Path.GetDirectoryName(srcPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets";

            var newName = ObjectNames.GetUniqueName(_masks.Where(x => x).Select(x => x.name).ToArray(), mask.name + " Copy");
            var newObj = Instantiate(mask);
            newObj.name = newName;
            var newPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{newName}.asset");
            AssetDatabase.CreateAsset(newObj, newPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _masks.Add(newObj);
            _iconCache[newObj.GetInstanceID()] = AssetPreview.GetMiniThumbnail(newObj);
            RebuildVisibleList();
            _listView.itemsSource = _visibleList;
            _listView.RefreshItems();

            var idx = _visibleList.IndexOf(newObj);
            if (idx < 0) return;
            _listView.SetSelection(idx);
            _listView.ScrollToItem(idx);
            SelectMask(newObj);
        }

        private void DeleteMaskAsset(BlendMaskBase mask)
        {
            if (!mask) return; // 提前返回
            if (mask == null) return;
            var path = AssetDatabase.GetAssetPath(mask);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 刷新列表与选择
            RebuildList();
            RebuildVisibleList();
            _listView.itemsSource = _visibleList;
            _listView.RefreshItems();

            // 若当前选中被删除，自动清空应用
            if (_selected != null && _masks.Contains(_selected)) return;
            ApplySelection(null);
            _listView.SetSelection(0);
            _listView.ScrollToItem(0);
            UpdateDetailsPanel();
        }

        // 批量删除所选遮罩（无确认、无撤销）
        private void DeleteSelectedMasks()
        {
            if (_listView == null || _visibleList == null) return;
            var indices = _listView.selectedIndices?.ToList() ?? new List<int>();
            if (indices.Count == 0) return;

            // 先收集再删除，避免索引变化
            var toDelete = Enumerable.ToList((from idx in indices where idx >= 0 && idx < _visibleList.Count select _visibleList[idx] into m where m != null select m));

            foreach (var path in toDelete.Select(m => AssetDatabase.GetAssetPath(m)).Where(path => !string.IsNullOrEmpty(path)))
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RebuildList();
            RebuildVisibleList();
            _listView.itemsSource = _visibleList;
            _listView.RefreshItems();

            ApplySelection(null);
            _listView.ClearSelection();
            _listView.SetSelection(0);
            _listView.ScrollToItem(0);
            UpdateDetailsPanel();
        }

        // 快捷键处理：F2 重命名、Delete 批量删除
        private void OnListKeyDown(KeyDownEvent e)
        {
            // 若正在编辑文本，则忽略删除/重命名快捷键
            if (e.target is TextField { name: "name-edit" } tf && tf.style.display == DisplayStyle.Flex)
            {
                return;
            }

            if (e.keyCode == KeyCode.F2)
            {
                var idxEnum = _listView?.selectedIndices;
                if (idxEnum == null) return;
                var idx = idxEnum.FirstOrDefault();
                if (idx < 0 || idx >= _visibleList.Count) return;
                // 保证可见后再启动重命名
                _listView.ScrollToItem(idx);
                EditorApplication.delayCall += () =>
                {
                    var mask = _visibleList[idx];
                    if (mask == null) return;
                    var row = FindRowElementForIndex(idx);
                    if (row != null) BeginInlineRename(row, mask);
                };
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Delete)
            {
                DeleteSelectedMasks();
                e.StopPropagation();
            }
        }

        // 在当前可见的行中查找指定索引的行元素
        private VisualElement FindRowElementForIndex(int index)
        {
            var mask = (index >= 0 && index < _visibleList.Count) ? _visibleList[index] : null;
            if (mask == null && index != 0) return null; // 允许 index==0 的 Null 行
            var rows = _listView.Query<VisualElement>(name: "row").ToList();
            return rows.FirstOrDefault(r => (BlendMaskBase)r.userData == mask);
        }


        // 创建半透明黑色圆角图标
        private static Texture2D CreateNullIcon()
        {
            // 创建一个简单的2D纹理作为默认图标
            const int size = 32; // 图标尺寸
            var nullIcon = new Texture2D(size, size);
            var pixels = new Color32[size * size];

            // 设置半透明黑色 (alpha值设为100，范围0-255)
            var nullColor = new Color32(0, 0, 0, 42);

            // 填充所有像素
            for (var i = 0; i < pixels.Length; i++)
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
            EditorApplication.delayCall += () =>
            {
                if (_details == null) return;
                // 选中已变化则取消
                if (_selected != target) return;
                try
                {
                    _details.Clear();
                    var inspector = new InspectorElement(target);
                    _details.Add(inspector);
                }
                catch
                {
                    try
                    {
                        if (_selectedEditor)
                        {
                            DestroyImmediate(_selectedEditor);
                            _selectedEditor = null;
                        }
                        _selectedEditor = UnityEditor.Editor.CreateEditor(target);
                        var ui = _selectedEditor.CreateInspectorGUI();
                        _details.Add(ui ?? new IMGUIContainer(() => _selectedEditor.OnInspectorGUI()));
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
            _selected = m;
            if (_targetLayer != null)
            {
                _targetLayer.layerMask = m;
                _applied = true;
                // 最终应用后通知
                try
                {
                    OnMaskApplied?.Invoke(_targetLayer, m);
                }
                catch
                {
                    // ignored
                }
                // 应用可能改变引用关系：重建索引并刷新可见行
                RebuildUsageIndex();
                _listView?.RefreshItems();
            }
            else
            {
                _applied = false;
            }
            RecreateEditor();
            UpdateDetailsPanel();
            // 单击应用不关闭窗口，双击由 onItemsChosen 关闭
        }

        // 单击选择：仅预览并刷新参数区，不触发最终应用事件
        private void SelectMask(BlendMaskBase m)
        {
            _selected = m;
            if (_targetLayer != null)
            {
                _targetLayer.layerMask = m;
            }
            RecreateEditor();
            UpdateDetailsPanel();
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
                if (!m) continue;
                _masks.Add(m);
                _iconCache[m.GetInstanceID()] = AssetPreview.GetMiniThumbnail(m);
            }
            _masks.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            // 重建“使用中”索引缓存
            RebuildUsageIndex();
        }

        private void RecreateEditor()
        {
            if (_selectedEditor)
            {
                DestroyImmediate(_selectedEditor);
                _selectedEditor = null;
            }
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


            // 获取插件根目录，并确保 MrPathV2/Settings/Masks 路径存在（动态定位，不硬编码 Assets 下具体位置）
            var defaultStorePath = GetMasksFolder();


            // 确定资产的干净名称
            var cleanName = string.IsNullOrWhiteSpace(nameHint)
                ? maskType.Name
                : nameHint.Trim();

            // 生成唯一的资产路径
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{defaultStorePath}/{cleanName}.asset");
            Debug.Log($"默认存储路径：{assetPath}");
            var instance = CreateInstance(maskType) as BlendMaskBase;
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
        // 解析插件根目录（包含 "MrPathV2" 的文件夹），支持插件位于 Assets 下任意层级
        private string GetPluginRootFolder()
        {
            try
            {
                var ms = MonoScript.FromScriptableObject(this);
                var scriptPath = AssetDatabase.GetAssetPath(ms);
                if (string.IsNullOrEmpty(scriptPath)) return "Assets/MrPathV2";
                scriptPath = scriptPath.Replace('\\', '/');
                var parts = scriptPath.Split('/');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i], "MrPathV2", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Join("/", parts.Take(i + 1));
                    }
                }
            }
            catch { }
            // Fallback：默认返回 Assets/MrPathV2
            return "Assets/MrPathV2";
        }

        // 返回并确保存在的 Masks 存储目录（MrPathV2/Settings/Masks）
        private string GetMasksFolder()
        {
            var root = GetPluginRootFolder();
            var masks = $"{root}/Settings/Masks";
            EnsureFolderPath(masks);
            return masks;
        }

        // 确保形如 "Assets/AAA/BBB" 的 Unity 相对路径存在
        private static void EnsureFolderPath(string unityFolderPath)
        {
            if (string.IsNullOrEmpty(unityFolderPath)) return;
            unityFolderPath = unityFolderPath.Replace('\\', '/');
            var parts = unityFolderPath.Split(new[]
            {
                '/'
            }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = parts[i];
                var candidate = $"{current}/{next}";
                if (!AssetDatabase.IsValidFolder(candidate))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = candidate;
            }
        }

        // 构建遮罩被引用的摘要缓存（按 Mask 聚合，文本尽量简洁）
        private void RebuildUsageIndex()
        {
            _usageLabelCache.Clear();
            try
            {
                // 全量检索所有 StylizedRoadRecipe 资产，避免仅扫描已加载对象漏报
                var recipeGuids = AssetDatabase.FindAssets("t:StylizedRoadRecipe");
                var recipes = new List<StylizedRoadRecipe>(recipeGuids.Length);
                foreach (var guid in recipeGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var recipe = AssetDatabase.LoadAssetAtPath<StylizedRoadRecipe>(path);
                    if (recipe) recipes.Add(recipe);
                }
                // 兜底：若项目中无资产，仍扫描已加载对象，保证编辑器中临时对象也能显示
                if (recipes.Count == 0)
                {
                    recipes.AddRange(Resources.FindObjectsOfTypeAll<StylizedRoadRecipe>() ?? Array.Empty<StylizedRoadRecipe>());
                }
                if (recipes is null) return; // 无配方则无引用

                // 统计 (maskId, recipe) -> 次数
                var pairCount = new Dictionary<(int maskId, StylizedRoadRecipe recipe), int>();
                foreach (var recipe in recipes)
                {
                    if (recipe == null) continue;
                    var layers = recipe.layers; // 直接访问序列化字段
                    if (layers == null) continue;
                    for (var i = 0; i < layers.Count; i++)
                    {
                        var layer = layers[i];
                        if (layer == null) continue;
                        var mask = layer.layerMask;
                        if (!mask) continue;
                        var key = (mask.GetInstanceID(), recipe);
                        pairCount.TryGetValue(key, out var c);
                        pairCount[key] = c + 1;
                    }
                }

                // 聚合到每个 mask 上，生成精简摘要
                var byMask = new Dictionary<int, List<(string recipeName, int count)>>();
                foreach (var kv in pairCount)
                {
                    var maskId = kv.Key.maskId;
                    var recipe = kv.Key.recipe;
                    var count = kv.Value;
                    if (!byMask.TryGetValue(maskId, out var list))
                    {
                        list = new List<(string, int)>();
                        byMask[maskId] = list;
                    }
                    var rName = recipe ? recipe.name : "Recipe";
                    list.Add((rName, count));
                }

                foreach (var kv in byMask)
                {
                    var entries = kv.Value.OrderByDescending(x => x.count).ToList();
                    string text;
                    if (entries.Count == 1)
                    {
                        var (rn, count) = entries[0];
                        text = count > 1 ? $"{rn}×{count}" : rn;
                    }
                    else
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < Mathf.Min(entries.Count, 2); i++)
                        {
                            var (rn, c) = entries[i];
                            parts.Add(c > 1 ? $"{rn}×{c}" : rn);
                        }
                        if (entries.Count > 2)
                        {
                            parts.Add($"+{entries.Count - 2}");
                        }
                        text = string.Join(", ", parts);
                    }
                    // 在文本前添加简洁的点缀，提升视觉识别但保持克制
                    _usageLabelCache[kv.Key] = string.IsNullOrEmpty(text) ? string.Empty : $"• {text}";
                }
            }
            catch
            {
                // 忽略编辑器态扫描异常，避免打断使用
            }
        }

    }
}
#endif
