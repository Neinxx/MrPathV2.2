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
        private UnityEngine.UIElements.Slider _masterOpacitySlider;
        private VisualElement _listHost;
        private __temp.MrPathV2.Editor.UI.ReorderableContainer _reorderContainer;
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
            _recipeUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrPathV2/Editor/UI/StylizedRoadRecipe.uxml");
            _layerUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrPathV2/Editor/UI/RoadLayer.uxml");
            _root = _recipeUxml ? _recipeUxml.Instantiate() : new VisualElement();

            // Master Opacity 绑定
            _masterOpacitySlider = _root.Q<UnityEngine.UIElements.Slider>("MasterOpacity");
            if (_masterOpacitySlider != null)
            {
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

            // 列表容器与添加按钮
            _listHost = _root.Q<VisualElement>("drapableRoot");
            var addBtn = _root.Q<UnityEngine.UIElements.Button>("addLayerButton");
            if (addBtn != null)
            {
                addBtn.text = "+ 新增图层";
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

            RebuildLayersUI();

            return _root;
        }

        private void RebuildLayersUI()
        {
            if (_listHost == null) return;
            _listHost.Clear();
            _reorderContainer = new __temp.MrPathV2.Editor.UI.ReorderableContainer();
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
            var item = new __temp.MrPathV2.Editor.UI.ReorderableItem { name = "layer-item" };
            item.userData = layer;
            item.Add(row);

            // 仅允许通过 UXML 中的 DrapPoint 进行拖拽
            var dragHandle = row.Q<UnityEngine.UIElements.VisualElement>("DrapPoint");
            if (dragHandle != null)
            {
                item.SetDragHandle(dragHandle, handleOnly: true);
            }

            // 顶部：启用、名称、删除
            var toggle = row.Q<UnityEngine.UIElements.Toggle>("Activelayer");
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

            var nameLabel = row.Q<UnityEngine.UIElements.Label>("Layername");
            if (nameLabel != null) nameLabel.text = layer.name;

            var removeBtn = row.Q<UnityEngine.UIElements.Button>("RemoveLayer");
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
            var blendDropdown = row.Q<UnityEngine.UIElements.DropdownField>("BlendModelEnum");
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

            var opacitySlider = row.Q<UnityEngine.UIElements.Slider>("LayerOpacity");
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
            var layerIcon = row.Q<UnityEngine.UIElements.VisualElement>("LayerIcon");
            var layerName = row.Q<UnityEngine.UIElements.Label>("LayerName");
            var contentSlot = layerIcon?.parent ?? row; // 槽位容器
            var clearButtons = row.Query<UnityEngine.UIElements.Button>(name: "ClearButton").ToList();
            var contentClear = clearButtons.Count > 0 ? clearButtons[0] : null;

            UpdateContentSlot(layer, layerIcon, layerName);
            SetClearButtonState(contentClear, layer.contentLayer != null);
            if (contentSlot != null)
            {
                contentSlot.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    OpenTerrainLayerPicker(layer);
                });
            }
            if (contentClear != null)
            {
                contentClear.clicked += () =>
                {
                    layer.contentLayer = null;
                    UpdateContentSlot(layer, layerIcon, layerName);
                    SetClearButtonState(contentClear, false);
                    EditorUtility.SetDirty(_recipe);
                    _recipe.RaiseRecipeChanged();
                };
            }

            // 遮罩槽位
            var maskIcon = row.Q<UnityEngine.UIElements.VisualElement>("MaskIcon");
            var maskName = row.Q<UnityEngine.UIElements.Label>("MaskName");
            var maskSlot = maskIcon?.parent ?? row;
            var maskClear = clearButtons.Count > 1 ? clearButtons[1] : null;

            UpdateMaskSlot(layer, maskIcon, maskName);
            SetClearButtonState(maskClear, layer.layerMask != null);
            if (maskSlot != null)
            {
                maskSlot.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    OpenMaskSelectWindow(layer);
                });
            }
            if (maskClear != null)
            {
                maskClear.clicked += () =>
                {
                    layer.layerMask = null;
                    UpdateMaskSlot(layer, maskIcon, maskName);
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
                MaskIcon = maskIcon,
                MaskName = maskName,
                MaskClear = maskClear
            };

            return item;
        }

        private void UpdateContentSlot(RoadLayer layer, UnityEngine.UIElements.VisualElement icon, UnityEngine.UIElements.Label name)
        {
            if (name != null) name.text = layer.contentLayer ? layer.contentLayer.name : "未选择";
            if (icon != null)
            {
                Texture2D tex = null;
                if (layer.contentLayer)
                {
                    var dtex = layer.contentLayer.diffuseTexture;
                    tex = dtex ? (Texture2D)(AssetPreview.GetAssetPreview(dtex) ?? AssetPreview.GetMiniThumbnail(dtex))
                               : (Texture2D)(AssetPreview.GetAssetPreview(layer.contentLayer) ?? AssetPreview.GetMiniThumbnail(layer.contentLayer));
                }
                icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
            }
        }

        private void UpdateMaskSlot(RoadLayer layer, UnityEngine.UIElements.VisualElement icon, UnityEngine.UIElements.Label name)
        {
            if (name != null) name.text = layer.layerMask ? layer.layerMask.name : "未选择";
            if (icon != null)
            {
                var tex = layer.layerMask ? AssetPreview.GetMiniThumbnail(layer.layerMask) as Texture2D : null;
                icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : null;
            }
        }

        private void RefreshLayerRowsContents()
        {
            if (_reorderContainer == null) return;
            for (int i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as __temp.MrPathV2.Editor.UI.ReorderableItem;
                if (item == null) continue;
                var layer = item.userData as RoadLayer;
                if (layer == null) continue;
                var row = item.ElementAt(0);
                var layerIcon = row.Q<UnityEngine.UIElements.VisualElement>("LayerIcon");
                var layerName = row.Q<UnityEngine.UIElements.Label>("LayerName");
                var maskIcon = row.Q<UnityEngine.UIElements.VisualElement>("MaskIcon");
                var maskName = row.Q<UnityEngine.UIElements.Label>("MaskName");
                var clearButtons = row.Query<UnityEngine.UIElements.Button>(name: "ClearButton").ToList();
                var contentClear = clearButtons.Count > 0 ? clearButtons[0] : null;
                var maskClear = clearButtons.Count > 1 ? clearButtons[1] : null;
                UpdateContentSlot(layer, layerIcon, layerName);
                UpdateMaskSlot(layer, maskIcon, maskName);
                SetClearButtonState(contentClear, layer.contentLayer != null);
                SetClearButtonState(maskClear, layer.layerMask != null);
            }
        }

        private void OnRecipeChanged()
        {
            if (_root == null) return;
            RefreshLayerRowsContents();
        }

        private static void SetClearButtonState(UnityEngine.UIElements.Button btn, bool enabled)
        {
            if (btn == null) return;
            // 统一通过类名让 USS 控制透明度（变量化）
            btn.RemoveFromClassList(enabled ? "clear-btn-hidden" : "clear-btn-visible");
            btn.AddToClassList(enabled ? "clear-btn-visible" : "clear-btn-hidden");

            // 交互仍由 C# 控制（USS 不支持 pickingMode）
            btn.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            btn.focusable = enabled;
        }

        private void SyncOrderFromUI()
        {
            if (_reorderContainer == null || _recipe == null) return;
            var newOrder = new System.Collections.Generic.List<RoadLayer>();
            for (int i = 0; i < _reorderContainer.childCount; i++)
            {
                var item = _reorderContainer.ElementAt(i) as __temp.MrPathV2.Editor.UI.ReorderableItem;
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
            global::MrPathV2.Editor.Windows.SelectTerrainLayerWindow.Open(layer, layer.contentLayer, contextPathCreator);
        }

        private void OpenMaskSelectWindow(RoadLayer layer)
        {
            global::MrPathV2.Editor.Windows.LayerMaskSelectWindow.Open(layer, layer.layerMask);
        }

        // ---- 细粒度窗口事件回调：仅更新对应行 ----
        private void OnContentLayerApplied(RoadLayer layer, TerrainLayer tl)
        {
            if (layer == null) return;
            if (_rowMap.TryGetValue(layer, out var refs))
            {
                layer.contentLayer = tl;
                UpdateContentSlot(layer, refs.ContentIcon, refs.ContentName);
                SetClearButtonState(refs.ContentClear, tl != null);
                if (_recipe) EditorUtility.SetDirty(_recipe);
            }
        }

        private void OnMaskApplied(RoadLayer layer, __temp.MrPathV2.Runtime.Core.BlendMasks.BlendMaskBase mask)
        {
            if (layer == null) return;
            if (_rowMap.TryGetValue(layer, out var refs))
            {
                layer.layerMask = mask;
                UpdateMaskSlot(layer, refs.MaskIcon, refs.MaskName);
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
            public VisualElement MaskIcon;
            public Label MaskName;
            public Button MaskClear;
        }
    }
}
