using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Editor.UI;
using MrPathV2.Editor.Windows;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : UnityEditor.Editor
    {
        private VisualTreeAsset _layerUxml;
        private VisualElement _listHost;
        private Slider _masterOpacitySlider;
        private StylizedRoadRecipe _recipe;

        private VisualTreeAsset _recipeUxml;
        private ReorderableContainerV2 _reorderContainer;
        private VisualElement _root;
        // 行元素映射：用于接收窗口事件后只更新对应行
        private readonly Dictionary<RoadLayer, RowRefs> _rowMap = new Dictionary<RoadLayer, RowRefs>();

        protected void OnEnable()
        {

            _recipe = target as StylizedRoadRecipe;
            if (_recipe != null)
            {
                _recipe.RecipeChanged += OnRecipeChanged;
            }

            // 订阅两个选择窗口的细粒度事件
            try
            {
                SelectTerrainLayerWindow.OnContentLayerApplied += OnContentLayerApplied;
            }
            catch
            {
                // ignored
            }
            try
            {
                LayerMaskSelectWindow.OnMaskApplied += OnMaskApplied;
            }
            catch
            {
                // ignored
            }

        }

        protected void OnDisable()
        {

            _root = null;
            _reorderContainer = null;
            _rowMap.Clear();
            if (_recipe != null)
            {
                _recipe.RecipeChanged -= OnRecipeChanged;
            }

            // 取消订阅窗口事件
            try
            {
                SelectTerrainLayerWindow.OnContentLayerApplied -= OnContentLayerApplied;
            }
            catch
            {
                // ignored
            }
            try
            {
                LayerMaskSelectWindow.OnMaskApplied -= OnMaskApplied;
            }
            catch
            {
                // ignored
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            // 使用资源加载器的缓存以避免频繁重建和磁盘IO，提升选中性能
            _recipeUxml = UIResourceLoader.LoadUxml(typeof(StylizedRoadRecipeEditor));
            _layerUxml = UIResourceLoader.LoadUxml(typeof(RoadLayer));

            if (!_recipeUxml || !_layerUxml)
            {
                Debug.LogError("StylizedRoadRecipeEditor.uxml or RoadLayer.uxml is not find");
                return null;
            }
            _root = _recipeUxml ? _recipeUxml.Instantiate() : new VisualElement();
            AttachEditorStyles(_root);

            // Master Opacity 绑定
            SetupMasterOpacity();

            // 列表容器与添加按钮
            _listHost = _root.Q<VisualElement>("drapableRoot");
            SetupAddButton();

            RebuildLayersUI();

            return _root;
        }

        private static void AttachEditorStyles(VisualElement root)
        {
            if (root == null) return;
            // 明确附加样式，避免 OnEnable 中绑定到临时 root 导致丢失
            var reorderUss = UIResourceLoader.LoadUssByName("ReorderableStyles");
            var buttonsUss = UIResourceLoader.LoadUssByName("Buttons");
            if (reorderUss != null && !root.styleSheets.Contains(reorderUss))
                root.styleSheets.Add(reorderUss);
            if (buttonsUss != null && !root.styleSheets.Contains(buttonsUss))
                root.styleSheets.Add(buttonsUss);
        }

        private void SetupMasterOpacity()
        {
            _masterOpacitySlider = _root.Q<Slider>("MasterOpacity");
            if (_masterOpacitySlider == null) return;
            _masterOpacitySlider.lowValue = 0f;
            _masterOpacitySlider.highValue = 1f;
            _masterOpacitySlider.value = _recipe ? _recipe.masterOpacity : 1f;
            _masterOpacitySlider.RegisterValueChangedCallback(ev =>
            {
                if (_recipe == null) return;
                Undo.RecordObject(_recipe, "Change Master Opacity");
                _recipe.masterOpacity = Mathf.Clamp01(ev.newValue);
                EditorUtility.SetDirty(_recipe);
                _recipe.RaiseRecipeChanged();
            });
        }

        private void SetupAddButton()
        {
            var addBtn = _root.Q<Button>("addLayerButton");
            if (addBtn == null) return;
            addBtn.text = "Create Layer";
            addBtn.clicked += CreateRoadLayer;
        }
        private void CreateRoadLayer()
        {
            if (_recipe == null) return;
            Undo.RecordObject(_recipe, "Add RoadLayer");
            var newLayer = new RoadLayer
            {
                name = $"Layer {_recipe.layers.Count + 1}",
                blendMode = BlendMode.Normal,
                opacity = 1f,
                enabled = true
            };
            _recipe.layers.Add(newLayer);
            EditorUtility.SetDirty(_recipe);
            RebuildLayersUI();
            _recipe.RaiseRecipeChanged();
        }

        private void RebuildLayersUI()
        {
            if (_listHost == null) return;
            _listHost.Clear();
            _reorderContainer = new ReorderableContainerV2();
            _listHost.Add(_reorderContainer);
            _rowMap.Clear();

            if (_recipe == null || _recipe.layers == null) return;
            foreach (var item in _recipe.layers.Select(BuildLayerItem))
            {
                _reorderContainer.Add(item);
            }

            // 在拖拽结束后同步顺序到数据
            _reorderContainer.RegisterCallback<DragPerformEvent>(_ => _root.schedule.Execute(SyncOrderFromUI).StartingIn(40));
            _reorderContainer.RegisterCallback<DragExitedEvent>(_ => _root.schedule.Execute(SyncOrderFromUI).StartingIn(40));
        }

        private VisualElement BuildLayerItem(RoadLayer layer)
        {
            if (layer == null) return new VisualElement();
            
            var row = _layerUxml ? _layerUxml.Instantiate() : new VisualElement();
            var item = new ReorderableItem
            {
                name = "layer-item",
                userData = layer
            };
            item.Add(row);

            SetupDragHandle(row, item);
            SetupTopControls(row, layer);
            SetupBlendControls(row, layer);
            SetupContentLayerSlot(row, layer);
            SetupMaskSlot(row, layer);
            
            // 建立行元素引用映射，用于事件驱动的局部更新
            _rowMap[layer] = CreateRowRefs(row);

            return item;
        }

        private static void SetupDragHandle(VisualElement row, ReorderableItem item)
        {
            var dragHandle = row.Q<VisualElement>("DrapPoint");
            if (dragHandle != null)
            {
                item.SetDragHandle(dragHandle);
            }
        }

        private void SetupTopControls(VisualElement row, RoadLayer layer)
        {
            // 启用开关
            var toggle = row.Q<Toggle>("Activelayer");
            if (toggle != null)
            {
                toggle.value = layer.enabled;
                toggle.RegisterValueChangedCallback(ev =>
                {
                    layer.enabled = ev.newValue;
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                });
            }

            // 层名称
            var nameLabel = row.Q<Label>("Layername");
            if (nameLabel != null) nameLabel.text = layer.name;

            // 删除按钮
            var removeBtn = row.Q<Button>("RemoveLayer");
            if (removeBtn != null)
            {
                removeBtn.clicked += RemoveLayer;
            }
            return;

            void RemoveLayer()
            {
                if (_recipe == null) return;
                Undo.RecordObject(_recipe, "Remove RoadLayer");
                _recipe.layers.Remove(layer);
                EditorUtility.SetDirty(_recipe);
                RebuildLayersUI();
                _recipe.RaiseRecipeChanged();
            }
        }

        private void SetupBlendControls(VisualElement row, RoadLayer layer)
        {
            // 混合模式下拉框
            var blendDropdown = row.Q<DropdownField>("BlendModelEnum");
            if (blendDropdown != null)
            {
                var names = Enum.GetNames(typeof(BlendMode)).ToList();
                blendDropdown.choices = names;
                blendDropdown.index = Mathf.Clamp((int)layer.blendMode, 0, names.Count - 1);
                blendDropdown.RegisterValueChangedCallback(_ =>
                {
                    layer.blendMode = (BlendMode)blendDropdown.index;
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                });
            }

            // 不透明度滑块
            var opacitySlider = row.Q<Slider>("LayerOpacity");
            if (opacitySlider != null)
            {
                opacitySlider.lowValue = 0f;
                opacitySlider.highValue = 1f;
                opacitySlider.value = layer.opacity;
                opacitySlider.RegisterValueChangedCallback(ev =>
                {
                    layer.opacity = Mathf.Clamp01(ev.newValue);
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                });
            }
        }

        private void SetupContentLayerSlot(VisualElement row, RoadLayer layer)
        {
            var layerIcon = row.Q<VisualElement>("LayerIcon");
            var layerName = row.Q<Label>("LayerName");
            var contentSlot = layerIcon?.parent ?? row; // 槽位容器
            var contentClear = row.Q<Button>("ClearLayerButton");

            if (contentSlot == null) return;

            UpdateContentSlot(layer, layerIcon, layerName);
            ToggleSlotEmptyClass(contentSlot, layer.contentLayer == null);
            SetClearButtonState(contentClear, layer.contentLayer != null);
            
            contentSlot.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                OpenTerrainLayerPicker(layer);
            });

            layerName?.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                OpenTerrainLayerPicker(layer);
                evt.StopPropagation();
            });

            if (contentClear != null)
            {
                contentClear.clicked += () =>
                {
                    layer.contentLayer = null;
                    UpdateContentSlot(layer, layerIcon, layerName);
                    ToggleSlotEmptyClass(contentSlot, true);
                    SetClearButtonState(contentClear, false);
                    if (_recipe == null) return;
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                };
            }
        }

        private void SetupMaskSlot(VisualElement row, RoadLayer layer)
        {
            var maskIcon = row.Q<VisualElement>("MaskIcon");
            var maskName = row.Q<Label>("MaskName");
            var maskSlot = maskIcon?.parent ?? row;
            var maskClear = row.Q<Button>("ClearMaskButton");

            if (maskSlot == null) return;

            UpdateMaskSlot(layer, maskIcon, maskName);
            ToggleSlotEmptyClass(maskSlot, layer.layerMask == null || !layer.maskEnabled);
            SetClearButtonState(maskClear, layer.layerMask != null);
            
            maskSlot.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                OpenMaskSelectWindow(layer);
            });

            maskName?.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                OpenMaskSelectWindow(layer);
                evt.StopPropagation(); // 阻止事件冒泡到 maskSlot，防止重复触发
            });

            if (maskClear is not null)
            {
                maskClear.clicked += ClearMask;
            }

            // 绑定 UXML 中现有的遮罩启用开关（优先使用用户提供的ID）
            var maskToggle = row.Q<Toggle>("MaskEnabled") ?? row.Q<Toggle>("EnableMask");
            if (maskToggle != null)
            {
                maskToggle.value = layer.maskEnabled;
                maskToggle.RegisterValueChangedCallback(ev =>
                {
                    layer.maskEnabled = ev.newValue;
                    ToggleSlotEmptyClass(maskSlot, layer.layerMask == null || !layer.maskEnabled);
                    UpdateMaskSlot(layer, maskIcon, maskName);
                    if (_recipe != null)
                    {
                        EditorUtility.SetDirty(_recipe);
                        _recipe.RaiseRecipeChanged();
                    }
                });
            }
            return;

            void ClearMask()
            {
                layer.layerMask = null;
                UpdateMaskSlot(layer, maskIcon, maskName);
                ToggleSlotEmptyClass(maskSlot, true);
                SetClearButtonState(maskClear, false);
                if (_recipe == null) return;
                EditorUtility.SetDirty(_recipe);
                _recipe.RaiseRecipeChanged();
            }
        }

        private static RowRefs CreateRowRefs(VisualElement row)
        {
            return new RowRefs
            {
                ContentIcon = row.Q<VisualElement>("LayerIcon"),
                ContentName = row.Q<Label>("LayerName"),
                ContentClear = row.Q<Button>("ClearLayerButton"),
                ContentSlot = row.Q<VisualElement>("LayerIcon")?.parent ?? row,
                MaskIcon = row.Q<VisualElement>("MaskIcon"),
                MaskName = row.Q<Label>("MaskName"),
                MaskClear = row.Q<Button>("ClearMaskButton"),
                MaskSlot = row.Q<VisualElement>("MaskIcon")?.parent ?? row
            };
        }

        private static void UpdateContentSlot(RoadLayer layer, VisualElement icon, Label name)
        {
            // 提前返回：无名称或无图标时不做任何处理
            if (name == null && icon == null) return;

            // 仅更新名称文本，避免不必要的操作
            if (name != null)
            {
                name.text = layer.contentLayer ? layer.contentLayer.name : "Take a layer to begin";
            }

            // 关键优化：使用 GetMiniThumbnail，避免首次选中时生成昂贵的 AssetPreview
            // 说明：AssetPreview.GetAssetPreview 会触发主线程的预览生成，层数较多/贴图较大时会造成卡顿。
            if (icon == null) return;
            Texture2D tex = null;
            if (layer.contentLayer)
            {
                var text = layer.contentLayer.diffuseTexture;
                tex = text
                    ? AssetPreview.GetMiniThumbnail(text)
                    : AssetPreview.GetMiniThumbnail(layer.contentLayer);
            }

            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
        }

        private static void UpdateMaskSlot(RoadLayer layer, VisualElement icon, Label name)
        {
            if (name != null)
            {
                if (layer.layerMask == null) name.text = "Take a mask to begin";
                else name.text = layer.maskEnabled ? layer.layerMask.name : $"{layer.layerMask.name} (Disabled)";
            }
            if (icon == null) return;
            var tex = layer.layerMask ? AssetPreview.GetMiniThumbnail(layer.layerMask) : null;
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
        }

        private void RefreshLayerRowsContents()
        {
            if (_reorderContainer == null) return;
            for (var i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as ReorderableItem;
                var layer = item?.userData as RoadLayer;
                if (layer == null) continue;
                var row = item.ElementAt(0);
                var layerIcon = row.Q<VisualElement>("LayerIcon");
                var layerName = row.Q<Label>("LayerName");
                var maskIcon = row.Q<VisualElement>("MaskIcon");
                var maskName = row.Q<Label>("MaskName");
                var contentClear = row.Q<Button>("ClearLayerButton");
                var maskClear = row.Q<Button>("ClearMaskButton");
                var maskToggle = row.Q<Toggle>("MaskEnabled") ?? row.Q<Toggle>("EnableMask");
                UpdateContentSlot(layer, layerIcon, layerName);
                UpdateMaskSlot(layer, maskIcon, maskName);
                ToggleSlotEmptyClass(layerIcon?.parent ?? row, layer.contentLayer == null);
                ToggleSlotEmptyClass(maskIcon?.parent ?? row, layer.layerMask == null || !layer.maskEnabled);
                SetClearButtonState(contentClear, layer.contentLayer != null);
                SetClearButtonState(maskClear, layer.layerMask != null);
                if (maskToggle != null) maskToggle.SetValueWithoutNotify(layer.maskEnabled);
            }
        }

        private void OnRecipeChanged()
        {
            if (_root == null) return;
            RefreshLayerRowsContents();
        }

        private static void SetClearButtonState(Button btn, bool visibleAndEnabled)
        {
            if (btn == null) return;

            // 控制交互性（保持原有逻辑）
            btn.pickingMode = visibleAndEnabled ? PickingMode.Position : PickingMode.Ignore;
            btn.focusable = visibleAndEnabled;

            // 控制可见性（通过添加/移除USS类）
            if (visibleAndEnabled)
            {
                btn.RemoveFromClassList("clear-btn-hidden");
                btn.AddToClassList("clear-btn-visible");
            }
            else
            {
                btn.RemoveFromClassList("clear-btn-visible");
                btn.AddToClassList("clear-btn-hidden");
            }
        }

        private void SyncOrderFromUI()
        {
            if (_reorderContainer == null || _recipe == null) return;
            var newOrder = new List<RoadLayer>();
            for (var i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as ReorderableItem;
                if (item?.userData is RoadLayer layer) newOrder.Add(layer);
            }
            if (newOrder.Count != _recipe.layers.Count) return;
            Undo.RecordObject(_recipe, "Reorder RoadLayers");
            _recipe.layers.Clear();
            _recipe.layers.AddRange(newOrder);
            EditorUtility.SetDirty(_recipe);
            _recipe.RaiseRecipeChanged();
        }

        private static void OpenTerrainLayerPicker(RoadLayer layer)
        {
            // 通过 Selection 获取上下文 PathCreator
            PathCreator contextPathCreator = null;
            var activeGo = Selection.activeGameObject;
            if (activeGo) contextPathCreator = activeGo.GetComponent<PathCreator>();
            SelectTerrainLayerWindow.Open(layer, layer.contentLayer, contextPathCreator);
        }

        private static void OpenMaskSelectWindow(RoadLayer layer)
        {
            LayerMaskSelectWindow.Open(layer, layer.layerMask);
        }

        // ---- 细粒度窗口事件回调：仅更新对应行 ----
        private void OnContentLayerApplied(RoadLayer layer, TerrainLayer tl)
        {
            if (layer == null) return;
            if (!_rowMap.TryGetValue(layer, out var refs)) return;
            layer.contentLayer = tl;
            UpdateContentSlot(layer, refs.ContentIcon, refs.ContentName);
            ToggleSlotEmptyClass(refs.ContentSlot, tl == null);
            SetClearButtonState(refs.ContentClear, tl != null);
            if (_recipe) EditorUtility.SetDirty(_recipe);
        }

        private void OnMaskApplied(RoadLayer layer, BlendMaskBase mask)
        {
            if (layer == null) return;
            if (!_rowMap.TryGetValue(layer, out var refs)) return;
            layer.layerMask = mask;
            UpdateMaskSlot(layer, refs.MaskIcon, refs.MaskName);
            ToggleSlotEmptyClass(refs.MaskSlot, mask == null);
            SetClearButtonState(refs.MaskClear, mask != null);
            if (_recipe) EditorUtility.SetDirty(_recipe);
        }

        private static void ToggleSlotEmptyClass(VisualElement slot, bool isEmpty)
        {
            if (slot == null) return;
            if (isEmpty)
            {
                slot.AddToClassList("slot-empty");
                slot.RemoveFromClassList("slot-filled");
            }
            else
            {
                slot.AddToClassList("slot-filled");
                slot.RemoveFromClassList("slot-empty");
            }
        }

        // 行引用结构体
        private class RowRefs
        {
            public Button ContentClear;
            public VisualElement ContentIcon;
            public Label ContentName;
            public VisualElement ContentSlot;
            public Button MaskClear;
            public VisualElement MaskIcon;
            public Label MaskName;
            public VisualElement MaskSlot;
        }
    }
}
