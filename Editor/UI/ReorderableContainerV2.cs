using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.UI
{
    /// <summary>
    ///     ReorderableContainerV2
    ///     - 以邻接元素“半高(中线)”为唯一换位触发点：鼠标越过某行中线即切换插入点
    ///     - 自适应占位符高度：占位间距与目标相邻元素高度一致，避免跳变
    ///     - 更宽容的自动滚动区域与速度，拖拽跨视口更顺畅
    ///     - 提前返回与单一职责，逻辑清晰，低 GC
    ///     适配 Unity 2021.3.18f1（UITK）
    /// </summary>
    public class ReorderableContainerV2 : VisualElement
    {

        // 常量参数（与 USS 的过渡时长保持一致，样式里 placeholder-spacing-element: transition: height 90ms ease-out）
        private const float AutoScrollZonePx = 28f; // 自动滚动触发区域（顶部/底部）
        private const float AutoScrollSpeedPx = 9f; // 自动滚动速度（像素/帧）

        // 缓存中线（半高触发）
     //   private readonly List<float> _childMidYs = new List<float>(32);
        private VisualElement _draggedItem; // 被拖拽项（作为幽灵）
        private Vector2 _dragStartOffset; // 鼠标相对项左上角的偏移（容器局部坐标）
        private float _ghostFixedWidth; // 幽灵项固定宽度，避免每帧改宽造成闪烁
        private int _insertionIndex = -1; // 预定的插入索引

        // 拖拽状态
        private bool _isDragging;
        private bool _isMarginAbove; // margin 在目标元素上方/下方

        // DropContainer 风格的“边距撑开”插入指示
        private VisualElement _marginTarget; // 当前被施加 margin 的元素
       // private int _pendingTargetIndex = -1; // 本帧待应用的占位索引（去抖与批处理）

        // 占位符（单蓝线 + 间隙）
        private VisualElement _placeholder;
        private int _placeholderIndex = -1; // 当前占位插入索引（基于 Children()）
        private ScrollView _scrollView; // 最近的滚动视图
        private VisualElement _spacing;
      //  private bool _swapScheduled = false; // 是否已调度本帧的占位移动
        private VisualElement _topLine;

        public ReorderableContainerV2()
        {
            style.flexDirection = FlexDirection.Column;
            AddToClassList("reorderable-container");
            InitPlaceholder();

            RegisterCallback<DragEnterEvent>(OnDragEnter);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragExitedEvent>(OnDragExited);
        }

        private void InitPlaceholder()
        {
            _placeholder = new VisualElement
            {
                name = "placeholder-container",
                style =
                {
                    flexDirection = FlexDirection.Column,
                    display = DisplayStyle.None
                }
            };

            _topLine = new VisualElement();
            _topLine.AddToClassList("placeholder-line");
            _topLine.AddToClassList("placeholder-line-top");

            _spacing = new VisualElement();
            _spacing.AddToClassList("placeholder-spacing-element");
            _spacing.style.height = 0;

            _placeholder.Add(_topLine);
            _placeholder.Add(_spacing);
            // 单蓝线，不再添加底部蓝线，改用间隙表示插入点

            Add(_placeholder);
        }

        // 事件入口
        private void OnDragEnter(DragEnterEvent evt)
        {
            TryBeginDrag(evt.mousePosition);
            evt.StopPropagation();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (!_isDragging || _draggedItem == null)
                TryBeginDrag(evt.mousePosition);
            if (!_isDragging || _draggedItem == null)
                return; // 提前返回

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            UpdateGhostPosition(evt.mousePosition);
            MaybeAutoScroll(evt.mousePosition);

            // 采用 DropContainer 的逻辑：以鼠标相对每个子项的“中线”为依据，
            // 通过给目标元素的 marginTop/marginBottom 撑开插入间隙，避免频繁 reparent 造成闪烁
            UpdateDropMarginTarget(evt.mousePosition);

            evt.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!_isDragging || _draggedItem == null) return;
            FinalizeDrag(true);
            evt.StopPropagation();
        }

        private void OnDragExited(DragExitedEvent evt)
        {
            if (!_isDragging || _draggedItem == null) return;
            FinalizeDrag(true);
            evt.StopPropagation();
        }

        // 初始化拖拽
        private void TryBeginDrag(Vector2 mouseWorld)
        {
            if (_isDragging) return;

            var data = DragAndDrop.GetGenericData("ReorderableItem") as VisualElement;
            if (data == null) return;
            if (data.parent != this) return; // 仅支持同一容器内重排

            _draggedItem = data;
            _isDragging = true;
            _scrollView = GetFirstAncestorOfType<ScrollView>();

            // 计算鼠标到项左上角的偏移（容器局部）
            var mouseLocal = mouseWorld - worldBound.position;
            _dragStartOffset = mouseLocal - new Vector2(_draggedItem.layout.x, _draggedItem.layout.y);

            // 锁定幽灵宽度一次，避免每帧修改宽度造成额外布局与闪烁
            _ghostFixedWidth = Mathf.Max(1f,
                resolvedStyle.width > 0 ? resolvedStyle.width :
                contentRect.width > 0 ? contentRect.width :
                layout.width > 0 ? layout.width : 1f);

            // 记录初始索引，移除占位插入，改用边距撑开视觉
            _placeholderIndex = IndexOf(_draggedItem);
            if (_placeholderIndex < 0) _placeholderIndex = 0;
            _placeholder.style.display = DisplayStyle.None;
            _marginTarget = null;
            _insertionIndex = -1;
            _isMarginAbove = false;

            // 初始化幽灵中心线与拖拽态样式
            AddToClassList("dragging-active");

            ConfigureDraggedAsGhost();
            CacheChildMidYPositions();
        }

        private void ConfigureDraggedAsGhost()
        {
            // 将真实项转为“幽灵”浮动元素
            _draggedItem.pickingMode = PickingMode.Ignore;
            _draggedItem.style.position = Position.Absolute;
            _draggedItem.style.left = _draggedItem.layout.x;
            _draggedItem.style.top = _draggedItem.layout.y;
            _draggedItem.style.width = _ghostFixedWidth;

            _draggedItem.BringToFront();
            _draggedItem.style.opacity = 0.85f;
            _draggedItem.AddToClassList("drag-ghost");
        }

        private void UpdateGhostPosition(Vector2 mouseWorld)
        {
            var local = mouseWorld - worldBound.position;
            var targetTop = local.y - _dragStartOffset.y;
            // 限制到容器区域（避免飞出）
            const float minTop = 0f;
            var maxTop = Mathf.Max(0f, contentRect.height - _draggedItem.layout.height);
            targetTop = Mathf.Clamp(targetTop, minTop, maxTop);

            _draggedItem.style.left = 0;
            _draggedItem.style.top = targetTop;
            // 宽度使用固定值，避免每帧改宽触发布局
        }

        private void CacheChildMidYPositions()
        {
          //  _childMidYs.Clear();
            foreach (var child in Children())
            {
                if (child == _draggedItem || child == _placeholder) continue;
                // 优先使用子项中的 TriggerLine 可视元素作为中线触发来源
                var triggerLine = child.Q<VisualElement>("TriggerLine");
                if (triggerLine != null)
                {
                    // 将 TriggerLine 的世界坐标中心 Y 转换到容器局部坐标系
                    //     _childMidYs.Add(midLocal);
                }
                // 回退：使用该子项的几何半高中线
                //      _childMidYs.Add(child.layout.y + child.layout.height * 0.5f);
            }
        }



        private void MaybeAutoScroll(Vector2 mouseWorld)
        {
            if (_scrollView == null) return;
            var svWorld = _scrollView.worldBound;
            var y = mouseWorld.y;

            if (y >= svWorld.yMax - AutoScrollZonePx)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, _scrollView.scrollOffset.y + AutoScrollSpeedPx);
            }
            else if (y <= svWorld.yMin + AutoScrollZonePx)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, Mathf.Max(0f, _scrollView.scrollOffset.y - AutoScrollSpeedPx));
            }
        }

        private void FinalizeDrag(bool commit)
        {
            if (!_isDragging) return;

            // 清除调度状态（占位方案不再使用）
       //     _swapScheduled = false;

            if (commit && _draggedItem != null)
            {
                // 使用边距撑开计算得到的插入索引；若无有效索引则回退到原始占位索引
                var target = _insertionIndex >= 0 ? _insertionIndex : _placeholderIndex;
                target = Mathf.Clamp(target, 0, childCount);

                // 恢复样式
                _draggedItem.pickingMode = PickingMode.Position;
                _draggedItem.style.position = Position.Relative;
                _draggedItem.style.left = StyleKeyword.Null;
                _draggedItem.style.top = StyleKeyword.Null;
                _draggedItem.style.width = StyleKeyword.Null;
                _draggedItem.RemoveFromClassList("drag-ghost");
                _draggedItem.style.opacity = StyleKeyword.Null;
                _draggedItem.style.translate = StyleKeyword.Null;

                // 执行最终插入
                Insert(Mathf.Clamp(target, 0, childCount), _draggedItem);
            }

            // 清理状态
            _placeholder.style.display = DisplayStyle.None;
            _placeholderIndex = -1;
            _draggedItem = null;
            _isDragging = false;
          //  _childMidYs.Clear();
        //    _pendingTargetIndex = -1;
       //     _swapScheduled = false;
       ClearDropMarginTarget();
            RemoveFromClassList("dragging-active");
        }

        // --- DropContainer 风格：计算并应用边距撑开插入点 ---
        private void UpdateDropMarginTarget(Vector2 pointerWorld)
        {
            var elements = Children().Where(c => c != _draggedItem && c != _placeholder).ToList();
            VisualElement closest = null;
            var closestDist = float.MaxValue;

            foreach (var child in elements)
            {
                // 使用 TriggerLine 的中线作为距离计算基准；若不存在则使用子项中心
                var triggerLine = child.Q<VisualElement>("TriggerLine");
                var baseMidY = triggerLine != null
                    ? triggerLine.worldBound.center.y
                    : child.worldBound.center.y;
                var dist = Mathf.Abs(pointerWorld.y - baseMidY);
                if (!(dist < closestDist)) continue;
                closestDist = dist;
                closest = child;
            }

            VisualElement newTarget = null;
            var newIndex = -1;
            var newAbove = false;
            var spaceSize = _draggedItem != null
                ? _draggedItem.resolvedStyle.height > 0 ? _draggedItem.resolvedStyle.height : _draggedItem.layout.height
                : 0f;

            if (closest != null)
            {
                var triggerLine = closest.Q<VisualElement>("TriggerLine");
                var childMidY = triggerLine != null
                    ? triggerLine.worldBound.center.y
                    : closest.worldBound.yMin + closest.worldBound.height * 0.5f;
                if (pointerWorld.y < childMidY)
                {
                    newIndex = IndexOf(closest);
                    newTarget = closest;
                    newAbove = true;
                }
                else
                {
                    newIndex = IndexOf(closest) + 1;
                    newTarget = closest;
                }
            }
            else if (elements.Count > 0)
            {
                var first = elements[0];
                var last = elements[^1];
                if (pointerWorld.y < first.worldBound.yMin)
                {
                    newIndex = 0;
                    newTarget = first;
                    newAbove = true;
                }
                else if (pointerWorld.y > last.worldBound.yMax)
                {
                    newIndex = childCount;
                    newTarget = last;
                }
            }
            else
            {
                newIndex = 0;
            }

            ApplyOrClearDropMargin(newTarget, newAbove, newIndex, spaceSize);
        }

        private void ApplyOrClearDropMargin(VisualElement newTarget, bool newAbove, int newIndex, float spaceSize)
        {
            // 提前返回：插入点没有变化
            if (_marginTarget == newTarget && _isMarginAbove == newAbove) return;

            // 清除旧的 margin
            ClearDropMarginTarget();

            // 记录并应用新的 margin
            _marginTarget = newTarget;
            _isMarginAbove = newAbove;
            _insertionIndex = newIndex;

            if (_marginTarget == null) return;
            if (_isMarginAbove)
            {
                _marginTarget.style.marginTop = new Length(spaceSize, LengthUnit.Pixel);
            }
            else
            {
                _marginTarget.style.marginBottom = new Length(spaceSize, LengthUnit.Pixel);
            }
        }

        private void ClearDropMarginTarget()
        {
            if (_marginTarget == null) return;
            _marginTarget.style.marginTop = StyleKeyword.Null;
            _marginTarget.style.marginBottom = StyleKeyword.Null;
            _marginTarget = null;
            _insertionIndex = -1;
            _isMarginAbove = false;
        }

        // UXML/工厂定义
        public new class UxmlFactory : UxmlFactory<ReorderableContainerV2, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }
    }
}
