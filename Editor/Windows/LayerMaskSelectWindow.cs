#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Editor;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
// 假设这些辅助类和运行时核心类型已存在于项目中
// 存放 MaskAssetService 和 MaskUsageIndex

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    ///     LayerMaskSelectWindow (优化版)
    ///     核心职责：管理 UI 流程、展示列表和详情，将资产操作和索引扫描委托给服务层。
    /// </summary>
    public class LayerMaskSelectWindow : EditorWindow
    {
        private const float ThumbSize = 40f;
        private static LayerMaskSelectWindow s_Instance; // 单例实例


        // --- 状态字段 ---
        private readonly List<BlendMaskBase> _visibleList = new List<BlendMaskBase>();
        private bool _applied; // 是否已最终应用（双击或关闭时应用）

        // --- 服务层引用 (依赖注入) ---
        private LayerMaskAssetService _assetService;
        private VisualElement _details; // MaskOSBox (详情/参数区)
        private ListView _listView;
        private DropdownField _maskTypeDropdown; // 用于新建类型的选择
        private TextField _nameField; // 新建名称输入
        private Button _newBtn;
        private BlendMaskBase _original; // 原始值：用于未应用时回滚

        // --- UITK 元素引用 ---
        private VisualElement _rootElement;
        private string _search = string.Empty;
        private ToolbarSearchField _searchField;
        private BlendMaskBase _selected; // 当前选中（列表/预览）
        private UnityEditor.Editor _selectedEditor; // 参数区渲染器
        private RoadLayer _targetLayer;

        // --- 静态事件 ---
        public static event Action<RoadLayer, BlendMaskBase> OnMaskApplied;

        #region 生命周期与初始化

        private void OnEnable()
        {
            // 确保服务被初始化
            _assetService = new LayerMaskAssetService();

        }

        private void OnDisable()
        {
            CleanupSelectedEditor();

            // 卫语句：已应用或无目标时，不进行回滚
            if (_applied || _targetLayer == null) return;

            // 安全回滚：使用 Undo 确保回滚操作可撤销
            //  Undo.RecordObject(_targetLayer, "Revert Mask Selection");//可以迁移到SO实现
            _targetLayer.layerMask = _original;
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void CreateGUI()
        {
            SetupRootElement();
            BindUIElements();
            SetupListView();
            BindSearchField();
            SetupMaskDropdown();
            BindCreateButton();
        }

        public static void Open(RoadLayer layer, BlendMaskBase current)
        {
            if (s_Instance != null)
            {
                FocusExistingInstance(layer, current);
                return;
            }

            CreateAndShowNewInstance(layer, current);
        }

        // 新入口：作为 Mask 资产管理器打开（不绑定 RoadLayer）
        [MenuItem("MrPath/Masks/Mask Manager")]
        public static void OpenManager()
        {
            Open(null, null);
        }

        private static void FocusExistingInstance(RoadLayer layer, BlendMaskBase current)
        {
            s_Instance.Initialize(layer, current);
            s_Instance.Focus();
        }

        private static void CreateAndShowNewInstance(RoadLayer layer, BlendMaskBase current)
        {
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
            _applied = false; // 每次打开重置应用状态

            // 委托数据加载/索引重建
            _assetService.Initialize();


            // 确保类型列表可用
            if (!_assetService.AvailableMaskTypes.Any())
            {
                _assetService.SetFallbackMaskType();
            }

            // UI 数据填充
            SetupMaskDropdownChoices();
            InitializeListViewData();
            RecreateEditor(); // 确保参数区准备好
            UpdateDetailsPanel(); // 刷新参数区内容
        }

        #endregion

        #region UITK 设置与绑定

        private void SetupRootElement()
        {
            _rootElement = rootVisualElement;
            _rootElement.Clear();


            var vta = UIResourceLoader.LoadUxml(typeof(LayerMaskSelectWindow));
            var styleSheet = UIResourceLoader.LoadUss(typeof(LayerMaskSelectWindow));
            if (styleSheet != null)
            {
                // 将样式表添加到根视觉元素的 styleSheets 列表中
                _rootElement.styleSheets.Add(styleSheet);
            }
            else
            {
                Debug.LogError($"[LayerMaskSelectWindow] 无法加载样式表: {styleSheet}. 请检查文件路径和资产是否存在。");
            }
            if (vta != null)
            {
                vta.CloneTree(_rootElement);
            }

        }

        private void BindUIElements()
        {
            _searchField = _rootElement.Q<ToolbarSearchField>("ToolbarSearchField");
            _details = _rootElement.Q<VisualElement>("MaskOSBox");
            _maskTypeDropdown = _rootElement.Q<DropdownField>("MaskDropDownField");
            _nameField = _rootElement.Q<TextField>("MaskName");
            _newBtn = _rootElement.Q<Button>("CreateMask");
        }

        private void SetupListView()
        {
            // 简化 ListView 实例化
            _listView = new ListView(_visibleList)
            {
                name = "MaskListView",
                selectionType = SelectionType.Multiple,
                fixedItemHeight = 52,
                makeItem = CreateListItem,
                bindItem = BindListItem
            };

            _listView.onSelectionChange += OnListViewSelectionChanged;
            _listView.onItemsChosen += OnListViewItemsChosen;
            _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown);

            _rootElement.Q<VisualElement>("MaskList")?.Add(_listView);
        }

        private void InitializeListViewData()
        {
            RebuildVisibleList();
            _listView.itemsSource = _visibleList;

            // 默认选中 Layer 当前遮罩，否则选中 Null (index 0)
            var defaultIndex = _selected != null ? _visibleList.IndexOf(_selected) : 0;
            defaultIndex = Mathf.Max(0, defaultIndex); // 确保不小于 0

            _listView.SetSelection(defaultIndex);
            _listView.ScrollToItem(defaultIndex);
            _listView.RefreshItems();
        }

        private void BindSearchField()
        {
            if (_searchField == null) return;
            _searchField.value = _search;
            _searchField.RegisterValueChangedCallback(OnSearchValueChanged);
        }

        private void SetupMaskDropdown()
        {
            // 初始值在 Initialize 中设置
            _maskTypeDropdown?.RegisterValueChangedCallback(OnMaskTypeChanged);
        }

        private void SetupMaskDropdownChoices()
        {
            if (_maskTypeDropdown == null) return;

            var typeNames = _assetService.AvailableMaskTypes.Select(t => t.Name).ToList();
            _maskTypeDropdown.choices = typeNames;
            // 确保选择一个有效类型
            if (typeNames.Any())
            {
                _maskTypeDropdown.value = typeNames.FirstOrDefault() ?? string.Empty;
            }
        }

        private void BindCreateButton()
        {
            if (_newBtn == null) return;
            _newBtn.clicked += OnCreateButtonClicked;
        }

        #endregion

        #region 数据与列表管理

        private void RebuildVisibleList()
        {
            _visibleList.Clear();
            _visibleList.Insert(0, null); // 顶部添加 Null 项用于清空遮罩槽位

            var query = _search.Trim();
            var masks = _assetService.AllMasks
                .Where(m => m != null); // 确保非空

            if (!string.IsNullOrWhiteSpace(query))
            {
                masks = masks.Where(m => m.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // 列表排序应委托给 AssetService，此处只处理过滤
            _visibleList.AddRange(masks.OrderBy(m => m.name));
        }

        // 统一的刷新流程
        private void RefreshAfterAssetOperation()
        {
            _assetService.ReloadMasksAndCache();


            RebuildVisibleList();
            _listView.RefreshItems();

            // 确保当前选中项的详情正确
            UpdateDetailsPanel();
        }

        #endregion

        #region 事件处理

        private void OnSearchValueChanged(ChangeEvent<string> evt)
        {
            _search = evt.newValue?.Trim() ?? string.Empty;
            RebuildVisibleList();
            _listView?.RefreshItems();
            _listView?.ScrollToItem(0); // 搜索后回到顶部
        }

        private void OnListViewSelectionChanged(IEnumerable<object> items)
        {
            var m = items.FirstOrDefault() as BlendMaskBase;
            SelectMask(m); // 单击选择：仅预览
            UpdateDetailsPanel();
        }

        private void OnListViewItemsChosen(IEnumerable<object> items)
        {
            var m = items.FirstOrDefault() as BlendMaskBase;
            ApplySelection(m); // 双击确认：应用并关闭
            Close();
        }

        private static void OnMaskTypeChanged(ChangeEvent<string> evt)
        {
            // 无需 _createTypeIndex 字段，直接通过名称查找类型
        }

        private void OnCreateButtonClicked()
        {
            var typeName = _maskTypeDropdown?.value;
            var maskType = _assetService.AvailableMaskTypes.FirstOrDefault(t => t.Name == typeName);

            if (maskType == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个有效的遮罩类型。", "确定");
                return;
            }

            var nameHint = _nameField?.value;

            // 委托给服务层处理创建和资产保存
            var newMask = _assetService.CreateNewMaskAsset(maskType, nameHint);

            if (newMask == null) return;

            // 刷新列表并选中新创建的遮罩
            RefreshAfterAssetOperation();
            SelectMaskInListView(newMask);
            ApplySelection(newMask); // 创建即应用
        }

        private void SelectMaskInListView(BlendMaskBase mask)
        {
            var index = _visibleList.IndexOf(mask);
            if (index < 0) return;

            _listView.SetSelection(index);
            _listView.ScrollToItem(index);
            SelectMask(mask);
        }

        #endregion

        #region 列表行渲染 (MakeItem, BindItem)

        private VisualElement CreateListItem()
        {
            // 1. 行容器 (name="row", class="list-item-row")
            var row = new VisualElement
            {
                name = "row"
                // 样式由 USS 文件中的 .list-item-row 规则控制
            };
            row.AddToClassList("list-item-row");

            row.AddManipulator(new ContextualMenuManipulator(PopulateRowContextMenu));

            // 2. 图标 (name="icon")
            var icon = new Image
            {
                name = "icon"
            };

            // 3. 文本内容容器 (name="content")
            var labelContent = new VisualElement
            {
                name = "content"
            };

            // 4. 主标签 (name="name")
            var nameLabel = new Label
            {
                name = "name"
            };

            // 5. 重命名输入框 (name="name-edit")
            var nameEdit = new TextField
            {
                name = "name-edit"
            };

            // 6. 次级标签 (name="sub")
            var subLabel = new Label
            {
                name = "sub"
            };

            // 7. 组装结构
            labelContent.Add(nameLabel);
            labelContent.Add(nameEdit);
            labelContent.Add(subLabel);

            row.Add(icon);
            row.Add(labelContent);

            return row;
        }


        // LayerMaskSelectWindow.cs (修复后的 BindListItem)

        private void BindListItem(VisualElement element, int index)
        {
            // 边界检查
            if (!IsValidIndex(index)) return;

            var mask = _visibleList[index];
            var row = SetupElementUserData(element, mask); // SetupElementUserData 返回了 row

            var icon = element.Q<Image>("icon");
            var label = element.Q<Label>("name");
            var sub = element.Q<Label>("sub");

            // ** 状态重置：确保重命名状态被清除 **
            if (row.ClassListContains("renaming"))
            {
                row.RemoveFromClassList("renaming");
            }


            UpdateElementContent(icon, label, sub, mask);
        }

// 边界检查辅助方法
        private bool IsValidIndex(int index) => index >= 0 && index < _visibleList.Count;

        // 设置用户数据辅助方法
        private static VisualElement SetupElementUserData(VisualElement element, object mask)
        {
            element.userData = mask;
            var row = element.Q<VisualElement>("row") ?? element;
            row.userData = mask;
            return row;
        }

// 应用图标样式辅助方法


// 更新元素内容辅助方法
        private void UpdateElementContent(Image icon, Label label, Label sub, BlendMaskBase mask)
        {
            if (mask == null)
            {
                UpdateNullContent(icon, label, sub);
            }
            else
            {
                UpdateMaskContent(icon, label, sub, mask);
            }
        }

// 更新空内容辅助方法
        private void UpdateNullContent(Image icon, Label label, Label sub)
        {
            if (icon != null) icon.image = _assetService.NullIcon;
            if (label != null) label.text = "Null";
            if (sub != null) sub.text = "None";
        }

// 更新掩码内容辅助方法
        private void UpdateMaskContent(Image icon, Label label, Label sub, BlendMaskBase mask)
        {
            if (icon != null) icon.image = _assetService.GetMaskThumbnail(mask);
            if (label != null) label.text = mask.name;
            if (sub != null) sub.text = mask.GetType().Name;
        }

        // 抽取样式设置

        #endregion

        #region 右键菜单与快捷键处理

        private void PopulateRowContextMenu(ContextualMenuPopulateEvent evt)
        {
            var mask = GetMaskFromElement(evt.target as VisualElement);

            if (mask == null)
            {
                evt.menu.AppendAction("Clear and Apply to Layer", _ => ApplySelection(null));
                return;
            }

            var targetVe = GetRowElementFromChild(evt.target as VisualElement);

            evt.menu.AppendAction("Ping Asset", _ => EditorGUIUtility.PingObject(mask));
            // 重命名使用 ScheduleRename
            evt.menu.AppendAction("Rename", _ => BeginInlineRename(targetVe, mask));
            // 复制/删除委托给服务
            evt.menu.AppendAction("Duplicate", _ => DuplicateMaskAsset(mask));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete", _ => DeleteMaskAsset(mask));
        }

        private void DuplicateMaskAsset(BlendMaskBase mask)
        {
            var newMask = _assetService.DuplicateMaskAsset(mask);
            if (newMask == null) return;

            RefreshAfterAssetOperation();
            SelectMaskInListView(newMask);
        }

        private void DeleteMaskAsset(BlendMaskBase mask)
        {
            if (mask == null) return;

            // 委托删除
            _assetService.DeleteMaskAssets(new List<BlendMaskBase>
            {
                mask
            });
            RefreshAfterAssetOperation();

            // 如果删除了当前选中项，则选中 Null
            if (_selected != mask) return;
            ApplySelection(null);
            _listView.SetSelection(0);
            _listView.ScrollToItem(0);
        }

        // 简化快捷键逻辑：Delete 统一调用 DeleteSelectedMasks
        private void OnListKeyDown(KeyDownEvent evt)
        {
            if (evt.target is TextField) return; // 正在编辑文本，忽略

            switch (evt.keyCode)
            {
                case KeyCode.F2:
                    HandleRenameKey();
                    evt.StopPropagation();
                    break;
                case KeyCode.Delete:
                    DeleteSelectedMasks();
                    evt.StopPropagation();
                    break;
            }
        }

        private void HandleRenameKey()
        {
            var index = _listView.selectedIndices?.FirstOrDefault() ?? -1;
            if (index <= 0 || index >= _visibleList.Count) return; // 忽略 Null (index 0)

            _listView.ScrollToItem(index);

            EditorApplication.delayCall += () =>
            {
                var mask = _visibleList[index];
                var row = FindRowElementForIndex(index);
                if (row != null && mask != null)
                    BeginInlineRename(row, mask);
            };
        }

        private void DeleteSelectedMasks()
        {
            if (_listView?.selectedIndices == null || !_visibleList.Any()) return;

            var masksToDelete = _listView.selectedIndices
                .Where(idx => idx > 0 && idx < _visibleList.Count) // 忽略 Null 项
                .Select(idx => _visibleList[idx])
                .Where(mask => mask != null)
                .ToList();

            if (masksToDelete.Count == 0) return;

            // 委托批量删除
            _assetService.DeleteMaskAssets(masksToDelete);
            RefreshAfterAssetOperation();

            // 如果当前选中被删除，则应用 null
            if (masksToDelete.Contains(_selected))
            {
                ApplySelection(null);
            }

            _listView.ClearSelection();
            _listView.SetSelection(0);
            _listView.ScrollToItem(0);
        }

        private VisualElement FindRowElementForIndex(int index)
        {
            var mask = index >= 0 && index < _visibleList.Count ? _visibleList[index] : null;
            if (mask == null && index != 0) return null;

            // 优化查询：只查询当前可见的行，通过 userData 比对
            var rows = _listView.Query<VisualElement>("row").ToList();
            return rows.FirstOrDefault(r => (BlendMaskBase)r.userData == mask);
        }

        // 重命名逻辑简化：委托给服务层处理数据修改，窗口只处理 UI 切换
        // LayerMaskSelectWindow.cs (BeginInlineRename)

        // LayerMaskSelectWindow.cs (优化后的 BeginInlineRename)

        private void BeginInlineRename(VisualElement row, BlendMaskBase mask)
        {
            if (row == null || mask == null) return;

            var nameLabel = row.Q<Label>("name");
            var nameEdit = row.Q<TextField>("name-edit");

            if (nameEdit == null || nameLabel == null) return;

            // **核心：添加状态类**
            row.AddToClassList("renaming"); // 让 USS 接管 Label/TextField 的显示切换

            nameEdit.value = mask.name;

            // ** 优化点：只调用一次延时执行 **
            // 延时一帧（1毫秒）来确保元素在 display: flex 之后，能正确获取焦点和全选。
            nameEdit.schedule.Execute(() =>
            {
                nameEdit.Focus();
                nameEdit.SelectAll();
            }).ExecuteLater(1);

            // 确保上下文包含 row 元素
            var context = (nameLabel, mask, row);

            // ... (后续事件清理和绑定逻辑保持不变) ...
            // ...
            nameEdit.userData = context;
            nameEdit.RegisterCallback<KeyDownEvent, (Label, BlendMaskBase, VisualElement)>(OnRenameKeyDown, context);
            nameEdit.RegisterCallback<FocusOutEvent, (Label, BlendMaskBase, VisualElement)>(OnRenameFocusOut, context);
        }

        private void OnRenameKeyDown(KeyDownEvent e, (Label nameLabel, BlendMaskBase mask, VisualElement row) context)
        {
            var nameEdit = e.currentTarget as TextField;
            if (nameEdit == null) return;

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    CommitRename(nameEdit, context.nameLabel, context.mask, context.row); // 传入 row
                    e.StopPropagation();
                    break;
                case KeyCode.Escape:
                    CancelRename(nameEdit, context.nameLabel, context.row); // 传入 row
                    e.StopPropagation();
                    break;
            }
        }

        private void OnRenameFocusOut(FocusOutEvent e, (Label nameLabel, BlendMaskBase mask, VisualElement row) context)
        {
            if (e.currentTarget is TextField nameEdit)
            {
                CommitRename(nameEdit, context.nameLabel, context.mask, context.row); // 传入 row
            }
        }

        private void CancelRename(TextField nameEdit, Label nameLabel, VisualElement row)
        {
            if (row.ClassListContains("renaming")) // 确保不会重复移除
            {
                row.RemoveFromClassList("renaming");
            }

            // 2. 清理值（可选，但推荐）
            nameEdit.value = string.Empty;

            // 3. 关键：解绑事件（防止事件处理器在元素回收后被错误触发）
            //    虽然 UIToolkit 理论上会处理，但手动解绑是最安全的防御性编程
            nameEdit.UnregisterCallback<KeyDownEvent, (Label, BlendMaskBase, VisualElement)>(OnRenameKeyDown);
            nameEdit.UnregisterCallback<FocusOutEvent, (Label, BlendMaskBase, VisualElement)>(OnRenameFocusOut);
        }

// LayerMaskSelectWindow.cs (CommitRename - 移除状态类)
        private void CommitRename(TextField nameEdit, Label nameLabel, BlendMaskBase mask, VisualElement row)
        {
            var newName = nameEdit.value?.Trim();
            if (string.IsNullOrEmpty(newName) || newName == mask.name)
            {
                CancelRename(nameEdit, nameLabel, row); // 传入 row
                return;
            }

            _assetService.RenameMaskAsset(mask, newName);
            CancelRename(nameEdit, nameLabel, row);
            RefreshAfterAssetOperation();

            // if (nameLabel != null) nameLabel.text = mask.name; // 从 mask 获取新名称，因为服务已更新它
            // CancelRename(nameEdit, nameLabel, row); // 传入 row
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

        private static VisualElement GetRowElementFromChild(VisualElement ve)
        {
            while (ve != null)
            {
                if (ve.name == "row") return ve;
                ve = ve.parent;
            }
            return null;
        }

        #endregion

        #region 详情/参数区管理

        private void CleanupSelectedEditor()
        {
            if (_selectedEditor == null) return;
            DestroyImmediate(_selectedEditor);
            _selectedEditor = null;
        }

        private void UpdateDetailsPanel()
        {
            if (_details == null) return;
            _details.Clear();

            if (_selected == null)
            {
                _details.Add(new Label("未选择遮罩 (Null)"));
                CleanupSelectedEditor();
                return;
            }

            // 统一使用 UIToolkit 的 InspectorElement，简化逻辑
            _details.Add(new Label("正在加载参数…"));
            EditorApplication.delayCall += UpdateInspectorOnNextFrame;
        }

        private void UpdateInspectorOnNextFrame()
        {
            if (_details == null || _selected == null) return;

            // 如果选中项在 delayCall 期间被销毁，则取消
            if (!EditorUtility.IsPersistent(_selected)) return;

            try
            {
                _details.Clear();
                // 推荐：直接使用 InspectorElement，它会处理 SerializedObject 和 Editor 的生命周期
                var inspector = new InspectorElement(_selected);
                _details.Add(inspector);
            }
            catch (Exception ex)
            {
                // 确保 IMGUI 兜底逻辑也被移除，统一 UITK 风格
                _details.Clear();
                _details.Add(new Label($"Inspector 加载失败: {ex.Message}"));
                Debug.LogError($"Failed to load Inspector for {_selected.name}: {ex.Message}");
            }
        }

        #endregion

        #region 选中与应用

        private void SelectMask(BlendMaskBase mask)
        {
            _selected = mask;
            // 单击：仅预览，不设置 _applied=true
            if (_targetLayer != null)
            {
                _targetLayer.layerMask = mask;

            }

            RecreateEditor();
            UpdateDetailsPanel();
        }

        private void ApplySelection(BlendMaskBase mask)
        {
            _selected = mask;

            if (_targetLayer != null)
            {
                _targetLayer.layerMask = mask;
                _applied = true; // 最终应用标记
                NotifyMaskApplied(mask);
            }
            else
            {
                _applied = false; // 资产管理器模式，不标记应用
            }

            // 应用可能改变引用关系，立即刷新列表使用信息

            _listView?.RefreshItems();

            RecreateEditor(); // 确保选中项的编辑器正确
            UpdateDetailsPanel();
        }

        private void NotifyMaskApplied(BlendMaskBase mask)
        {
            try
            {
                OnMaskApplied?.Invoke(_targetLayer, mask);
            }
            catch
            {
                // ignored
            }
        }

        private void RecreateEditor()
        {
            CleanupSelectedEditor();
            // 在 UpdateInspectorOnNextFrame 中处理编辑器的创建
        }

        #endregion
    }
}
#endif
