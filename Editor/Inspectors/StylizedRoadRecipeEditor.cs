using System;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using MrPathV2.Editor.Windows;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.Inspectors
{
    [CustomEditor(inspectedType: typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : UnityEditor.Editor
    {
        private StylizedRoadRecipe _recipe;
        private VisualElement _root;
        private Slider _masterOpacitySlider;
        private VisualElement _listHost;
        private UI.ReorderableContainerV2 _reorderContainer;
        // 行元素映射：用于接收窗口事件后只更新对应行
        private System.Collections.Generic.Dictionary<RoadLayer, RowRefs> _rowMap = new System.Collections.Generic.Dictionary<RoadLayer, RowRefs>();

        private VisualTreeAsset _recipeUxml;
        private VisualTreeAsset _layerUxml;

        protected void OnEnable()
        {

            _recipe = target as StylizedRoadRecipe;
            if (_recipe != null)
            {
                _recipe.RecipeChanged += OnRecipeChanged;
            }

            // 订阅两个选择窗口的细粒度事件
            try { SelectTerrainLayerWindow.OnContentLayerApplied += OnContentLayerApplied; } catch { }
            try { LayerMaskSelectWindow.OnMaskApplied += OnMaskApplied; } catch { }

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
            try { SelectTerrainLayerWindow.OnContentLayerApplied -= OnContentLayerApplied; } catch { }
            try { LayerMaskSelectWindow.OnMaskApplied -= OnMaskApplied; } catch { }
        }

        public override VisualElement CreateInspectorGUI()
        {
            // 使用资源加载器的缓存以避免频繁重建和磁盘IO，提升选中性能
            _recipeUxml = UIResourceLoader.LoadUxml(typeof(StylizedRoadRecipeEditor));
            _layerUxml = UIResourceLoader.LoadUxml(typeof(RoadLayer));

            if (!_recipeUxml || !_layerUxml)
            {
                Debug.LogError($"StylizedRoadRecipeEditor.uxml or RoadLayer.uxml is not find");
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

        private void AttachEditorStyles(VisualElement root)
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
            addBtn.clicked += () =>
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
            };
        }

        private void RebuildLayersUI()
        {
            if (_listHost == null) return;
            _listHost.Clear();
            _reorderContainer = new UI.ReorderableContainerV2();
            _listHost.Add(_reorderContainer);
            _rowMap.Clear();

            if (_recipe == null || _recipe.layers == null) return;
            foreach (var layer in _recipe.layers)
            {
                var item = BuildLayerItem(layer);
                _reorderContainer.Add(item);
            }

            // 在拖拽结束后同步顺序到数据
            _reorderContainer.RegisterCallback<DragPerformEvent>(_ => _root.schedule.Execute(SyncOrderFromUI).StartingIn(40));
            _reorderContainer.RegisterCallback<DragExitedEvent>(_ => _root.schedule.Execute(SyncOrderFromUI).StartingIn(40));
        }

        private VisualElement BuildLayerItem(RoadLayer layer)
        {
            var row = _layerUxml ? _layerUxml.Instantiate() : new VisualElement();
            var item = new UI.ReorderableItem { name = "layer-item" };
            item.userData = layer;
            item.Add(row);

            // 仅允许通过 UXML 中的 DrapPoint 进行拖拽
            var dragHandle = row.Q<VisualElement>("DrapPoint");
            if (dragHandle != null)
            {
                item.SetDragHandle(dragHandle, handleOnly: true);
            }

            // 顶部：启用、名称、删除
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

            var nameLabel = row.Q<Label>("Layername");
            if (nameLabel != null) nameLabel.text = layer.name;

            var removeBtn = row.Q<Button>("RemoveLayer");
            if (removeBtn != null)
            {
                removeBtn.clicked += () =>
                {
                    Undo.RecordObject(_recipe, "Remove RoadLayer");
                    _recipe.layers.Remove(layer);
                    EditorUtility.SetDirty(_recipe);
                    RebuildLayersUI();
                    _recipe.RaiseRecipeChanged();
                };
            }

            // 混合模式与不透明度
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

            // 内容层槽位
            var layerIcon = row.Q<VisualElement>("LayerIcon");
            var layerName = row.Q<Label>("LayerName");
            var contentSlot = layerIcon?.parent ?? row; // 槽位容器
            var contentClear = row.Q<Button>("ClearLayerButton");

            UpdateContentSlot(layer, layerIcon, layerName);
            ToggleSlotEmptyClass(contentSlot, isEmpty: layer.contentLayer == null);
            SetClearButtonState(contentClear, layer.contentLayer != null);
            contentSlot?.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    OpenTerrainLayerPicker(layer);
                });
            if (contentClear != null)
            {
                contentClear.clicked += () =>
                {
                    layer.contentLayer = null;
                    UpdateContentSlot(layer, layerIcon, layerName);
                    ToggleSlotEmptyClass(contentSlot, isEmpty: true);
                    SetClearButtonState(contentClear, false);
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                };
            }

            // 遮罩槽位
            var maskIcon = row.Q<VisualElement>("MaskIcon");
            var maskName = row.Q<Label>("MaskName");
            var maskSlot = maskIcon?.parent ?? row;
            var maskClear = row.Q<Button>("ClearMaskButton");

            UpdateMaskSlot(layer, maskIcon, maskName);
            ToggleSlotEmptyClass(maskSlot, isEmpty: layer.layerMask == null);
            SetClearButtonState(maskClear, layer.layerMask != null);
            maskSlot?.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    OpenMaskSelectWindow(layer);
                });
            if (maskClear != null)
            {
                maskClear.clicked += () =>
                {
                    layer.layerMask = null;
                    UpdateMaskSlot(layer, maskIcon, maskName);
                    ToggleSlotEmptyClass(maskSlot, isEmpty: true);
                    SetClearButtonState(maskClear, false);
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                };
            }

            // 建立行元素引用映射，用于事件驱动的局部更新
            _rowMap[layer] = new RowRefs
            {
                ContentIcon = layerIcon,
                ContentName = layerName,
                ContentClear = contentClear,
                ContentSlot = contentSlot,
                MaskIcon = maskIcon,
                MaskName = maskName,
                MaskClear = maskClear,
                MaskSlot = maskSlot
            };

            return item;
        }

        private void UpdateContentSlot(RoadLayer layer, VisualElement icon, Label name)
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
            if (icon != null)
            {
                Texture2D tex = null;
                if (layer.contentLayer)
                {
                    var dtex = layer.contentLayer.diffuseTexture;
                    tex = dtex ? AssetPreview.GetMiniThumbnail(dtex)
                               : AssetPreview.GetMiniThumbnail(layer.contentLayer);
                }

                icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
            }
        }

        private void UpdateMaskSlot(RoadLayer layer, VisualElement icon, Label name)
        {
            if (name != null) name.text = layer.layerMask ? layer.layerMask.name : "Take a mask to begin";
            if (icon != null)
            {
                var tex = layer.layerMask ? AssetPreview.GetMiniThumbnail(layer.layerMask) : null;
                icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
            }
        }

        private void RefreshLayerRowsContents()
        {
            if (_reorderContainer == null) return;
            for (int i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as UI.ReorderableItem;
                if (item == null) continue;
                var layer = item.userData as RoadLayer;
                if (layer == null) continue;
                var row = item.ElementAt(0);
                var layerIcon = row.Q<VisualElement>("LayerIcon");
                var layerName = row.Q<Label>("LayerName");
                var maskIcon = row.Q<VisualElement>("MaskIcon");
                var maskName = row.Q<Label>("MaskName");
                var contentClear = row.Q<Button>("ClearLayerButton");
                var maskClear = row.Q<Button>("ClearMaskButton");
                UpdateContentSlot(layer, layerIcon, layerName);
                UpdateMaskSlot(layer, maskIcon, maskName);
                ToggleSlotEmptyClass(layerIcon?.parent ?? row, isEmpty: layer.contentLayer == null);
                ToggleSlotEmptyClass(maskIcon?.parent ?? row, isEmpty: layer.layerMask == null);
                SetClearButtonState(contentClear, layer.contentLayer != null);
                SetClearButtonState(maskClear, layer.layerMask != null);
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
            var newOrder = new System.Collections.Generic.List<RoadLayer>();
            for (int i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as UI.ReorderableItem;
                var layer = item?.userData as RoadLayer;
                if (layer != null) newOrder.Add(layer);
            }
            if (newOrder.Count == _recipe.layers.Count)
            {
                Undo.RecordObject(_recipe, "Reorder RoadLayers");
                _recipe.layers.Clear();
                _recipe.layers.AddRange(newOrder);
                EditorUtility.SetDirty(_recipe);
                _recipe.RaiseRecipeChanged();
            }
        }

        private void OpenTerrainLayerPicker(RoadLayer layer)
        {
            // 通过 Selection 获取上下文 PathCreator
            PathCreator contextPathCreator = null;
            var activeGO = Selection.activeGameObject;
            if (activeGO) contextPathCreator = activeGO.GetComponent<PathCreator>();
            SelectTerrainLayerWindow.Open(layer, layer.contentLayer, contextPathCreator);
        }

        private void OpenMaskSelectWindow(RoadLayer layer)
        {
            LayerMaskSelectWindow.Open(layer, layer.layerMask);
        }

        // ---- 细粒度窗口事件回调：仅更新对应行 ----
        private void OnContentLayerApplied(RoadLayer layer, TerrainLayer tl)
        {
            if (layer == null) return;
            if (_rowMap.TryGetValue(layer, out var refs))
            {
                layer.contentLayer = tl;
                UpdateContentSlot(layer, refs.ContentIcon, refs.ContentName);
                ToggleSlotEmptyClass(refs.ContentSlot, isEmpty: tl == null);
                SetClearButtonState(refs.ContentClear, tl != null);
                if (_recipe) EditorUtility.SetDirty(_recipe);
            }
        }

        private void OnMaskApplied(RoadLayer layer, Runtime.Core.BlendMasks.BlendMaskBase mask)
        {
            if (layer == null) return;
            if (_rowMap.TryGetValue(layer, out var refs))
            {
                layer.layerMask = mask;
                UpdateMaskSlot(layer, refs.MaskIcon, refs.MaskName);
                ToggleSlotEmptyClass(refs.MaskSlot, isEmpty: mask == null);
                SetClearButtonState(refs.MaskClear, mask != null);
                if (_recipe) EditorUtility.SetDirty(_recipe);
            }
        }

        // 行引用结构体
        private class RowRefs
        {
            public VisualElement ContentIcon;
            public Label ContentName;
            public Button ContentClear;
            public VisualElement ContentSlot;
            public VisualElement MaskIcon;
            public Label MaskName;
            public Button MaskClear;
            public VisualElement MaskSlot;
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
    }
}
