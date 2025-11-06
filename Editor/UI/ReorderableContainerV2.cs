using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.UI
{
    /// <summary>
    /// ReorderableContainerV2
    /// - 以邻接元素“半高(中线)”为唯一换位触发点：鼠标越过某行中线即切换插入点
    /// - 自适应占位符高度：占位间距与目标相邻元素高度一致，避免跳变
    /// - 更宽容的自动滚动区域与速度，拖拽跨视口更顺畅
    /// - 提前返回与单一职责，逻辑清晰，低 GC
    /// 适配 Unity 2021.3.18f1（UITK）
    /// </summary>
    public class ReorderableContainerV2 : VisualElement
    {
        // UXML/工厂定义
        public new class UxmlFactory : UxmlFactory<ReorderableContainerV2, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        // 常量参数（与 USS 的过渡时长保持一致，样式里 placeholder-spacing-element: transition: height 90ms ease-out）
        private const float AutoScrollZonePx = 28f;     // 自动滚动触发区域（顶部/底部）
        private const float AutoScrollSpeedPx = 9f;     // 自动滚动速度（像素/帧）
        private const float IndexSwitchDeadZonePx = 16f;// 插入切换死区，减少来回抖动（更稳）
        private const float EdgeMagnetPx = 24f;         // 边缘磁吸区：更易将项拖到首/尾索引

        // 拖拽状态
        private bool _isDragging;
        private VisualElement _draggedItem;     // 被拖拽项（作为幽灵）
        private Vector2 _dragStartOffset;       // 鼠标相对项左上角的偏移（容器局部坐标）
        private int _placeholderIndex = -1;     // 当前占位插入索引（基于 Children()）
        private ScrollView _scrollView;         // 最近的滚动视图
        private int _pendingTargetIndex = -1;   // 本帧待应用的占位索引（去抖与批处理）
        private bool _swapScheduled;            // 是否已调度本帧的占位移动
        private float _ghostFixedWidth;         // 幽灵项固定宽度，避免每帧改宽造成闪烁
        private float _ghostCurrentTop;         // 幽灵当前 top（容器局部），用于中心线触发
        private float _smoothedGhostCenter;     // 平滑后的幽灵中心线，降低抖动
        private const float CenterSmoothAlpha = 0.35f; // 幽灵中心平滑系数（0-1）

        // 占位符（单蓝线 + 间隙）
        private VisualElement _placeholder;
        private VisualElement _topLine;
        private VisualElement _spacing;

        // DropContainer 风格的“边距撑开”插入指示
        private VisualElement _marginTarget;    // 当前被施加 margin 的元素
        private int _insertionIndex = -1;       // 预定的插入索引
        private bool _isMarginAbove = false;    // margin 在目标元素上方/下方

        // 缓存中线（半高触发）
        private readonly List<float> _childMidYs = new List<float>(32);

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
                style = { flexDirection = FlexDirection.Column, display = DisplayStyle.None }
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
            FinalizeDrag(commit: true);
            evt.StopPropagation();
        }

        private void OnDragExited(DragExitedEvent evt)
        {
            if (!_isDragging || _draggedItem == null) return;
            FinalizeDrag(commit: true);
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
            _scrollView = this.GetFirstAncestorOfType<ScrollView>();

            // 计算鼠标到项左上角的偏移（容器局部）
            var mouseLocal = (Vector2)mouseWorld - worldBound.position;
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
            _ghostCurrentTop = _draggedItem.layout.y;
            var initH = _draggedItem.resolvedStyle.height > 0 ? _draggedItem.resolvedStyle.height : _draggedItem.layout.height;
            _smoothedGhostCenter = _ghostCurrentTop + initH * 0.5f;
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
            // 使用层级 BringToFront 提升渲染层级，避免依赖 zIndex（2021.3 未公开）
            _draggedItem.BringToFront();
            _draggedItem.style.opacity = 0.85f;
            _draggedItem.AddToClassList("drag-ghost");
        }

        private void UpdateGhostPosition(Vector2 mouseWorld)
        {
            var local = (Vector2)mouseWorld - worldBound.position;
            var targetTop = local.y - _dragStartOffset.y;
            // 限制到容器区域（避免飞出）
            var minTop = 0f;
            var maxTop = Mathf.Max(0f, contentRect.height - _draggedItem.layout.height);
            targetTop = Mathf.Clamp(targetTop, minTop, maxTop);

            _draggedItem.style.left = 0;
            _draggedItem.style.top = targetTop;
            _ghostCurrentTop = targetTop; // 记录当前 top，以幽灵中心线作为触发依据
            // 宽度使用固定值，避免每帧改宽触发布局
        }

        private void CacheChildMidYPositions()
        {
            _childMidYs.Clear();
            foreach (var child in Children())
            {
                if (child == _draggedItem || child == _placeholder) continue;
                _childMidYs.Add(child.layout.y + child.layout.height * 0.5f);
            }
        }

        private int CalculateTargetIndexByHalfHeight(Vector2 mouseWorld)
        {
            if (_childMidYs.Count == 0) CacheChildMidYPositions();
            var local = (Vector2)mouseWorld - worldBound.position;
            var mouseY = local.y;

            // 边缘磁吸：靠近容器顶部/底部时直接吸附到首/尾索引，避免“首尾难换位”
            if (mouseY <= EdgeMagnetPx)
                return 0;
            if (mouseY >= contentRect.height - EdgeMagnetPx)
                return childCount;

            // 依据半高中线决定插入位置：鼠标超过某行中线，则插入到它之后
            int insertion = 0;
            int scanIndex = 0;
            for (int i = 0; i < childCount; i++)
            {
                var c = ElementAt(i);
                if (c == _draggedItem || c == _placeholder) continue;
                var mid = _childMidYs[scanIndex++];
                if (mouseY > mid) insertion = i + 1;
            }

            // 死区：若靠近最近中线过近，保持当前占位索引，减少抖动
            float closest = float.MaxValue;
            foreach (var mid in _childMidYs)
            {
                var d = Mathf.Abs(mouseY - mid);
                if (d < closest) closest = d;
            }
            if (_placeholderIndex >= 0 && closest <= IndexSwitchDeadZonePx)
                return _placeholderIndex;

            return Mathf.Clamp(insertion, 0, childCount);
        }

        // 改为以“被拖拽元素的中心线”作为触发依据，而非鼠标位置
        private int CalculateTargetIndexByGhostCenter()
        {
            if (_childMidYs.Count == 0) CacheChildMidYPositions();

            // 幽灵中心线（容器局部）
            float ghostH = 0f;
            if (_draggedItem != null)
            {
                ghostH = _draggedItem.resolvedStyle.height > 0 ? _draggedItem.resolvedStyle.height : _draggedItem.layout.height;
            }
            var ghostCenter = _ghostCurrentTop + ghostH * 0.5f;
            // 低通平滑中心线，减少中线来回切换造成的抖动
            if (_smoothedGhostCenter <= 0f)
                _smoothedGhostCenter = ghostCenter;
            else
                _smoothedGhostCenter = Mathf.Lerp(_smoothedGhostCenter, ghostCenter, CenterSmoothAlpha);
            var center = _smoothedGhostCenter;

            // 边缘磁吸：靠近容器顶部/底部时直接吸附到首/尾索引
            if (center <= EdgeMagnetPx)
                return 0;
            if (center >= contentRect.height - EdgeMagnetPx)
                return childCount;

            // 依据半高中线决定插入位置：幽灵中心超过某行中线，则插入到它之后
            int insertion = 0;
            int scanIndex = 0;
            for (int i = 0; i < childCount; i++)
            {
                var c = ElementAt(i);
                if (c == _draggedItem || c == _placeholder) continue;
                var mid = _childMidYs[scanIndex++];
                if (center > mid) insertion = i + 1;
            }

            // 死区：若靠近最近中线过近，保持当前占位索引，减少抖动
            float closest = float.MaxValue;
            float nearestHeight = 0f;
            scanIndex = 0;
            for (int i = 0; i < childCount; i++)
            {
                var c = ElementAt(i);
                if (c == _draggedItem || c == _placeholder) continue;
                var mid = _childMidYs[scanIndex++];
                var d = Mathf.Abs(center - mid);
                if (d < closest)
                {
                    closest = d;
                    nearestHeight = c.layout.height;
                }
            }
            var dynamicDeadZone = Mathf.Max(12f, nearestHeight > 0 ? nearestHeight * 0.25f : (ghostH > 0 ? ghostH * 0.25f : IndexSwitchDeadZonePx));
            if (_placeholderIndex >= 0 && closest <= dynamicDeadZone)
                return _placeholderIndex;

            return Mathf.Clamp(insertion, 0, childCount);
        }

        private void MovePlaceholder(int targetIndex)
        {
            targetIndex = Mathf.Clamp(targetIndex, 0, childCount);
            if (_placeholder.parent != this)
            {
                _placeholder.style.display = DisplayStyle.Flex;
                Insert(targetIndex, _placeholder);
            }
            else
            {
                var cur = IndexOf(_placeholder);
                if (cur != targetIndex)
                {
                    // 直接 Insert 即可将已存在的子元素移动到目标索引，避免 Remove+Insert 的双操作闪烁
                    Insert(targetIndex, _placeholder);
                }
            }
            _placeholderIndex = IndexOf(_placeholder);
        }

        private void SetPlaceholderSpacing(float height)
        {
            // 让位总高度 = 单蓝线 + 间隙，应与目标元素高度匹配
            // resolvedStyle.height 在首帧可能为 0，这里回退为 USS 设定的 2px
            var topH = _topLine?.resolvedStyle.height > 0 ? _topLine.resolvedStyle.height : 2f;
            var spacingH = Mathf.Max(0f, height - topH);
            _spacing.style.height = spacingH;
        }

        private float GetReferenceHeightForIndex(int insertionIndex)
        {
            // 优先使用插入点“后侧”的元素高度；若不存在，取前侧；最后退回为拖拽项高度
            for (int i = insertionIndex; i < childCount; i++)
            {
                var c = ElementAt(i);
                if (c == _draggedItem || c == _placeholder) continue;
                return c.layout.height;
            }
            for (int i = insertionIndex - 1; i >= 0; i--)
            {
                var c = ElementAt(i);
                if (c == _draggedItem || c == _placeholder) continue;
                return c.layout.height;
            }
            return _draggedItem?.layout.height ?? 0f;
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
            _swapScheduled = false;

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
            _childMidYs.Clear();
            _pendingTargetIndex = -1;
            _swapScheduled = false;
            _ghostCurrentTop = 0f;
            _smoothedGhostCenter = 0f;
            ClearDropMarginTarget();
            RemoveFromClassList("dragging-active");
        }

        // --- DropContainer 风格：计算并应用边距撑开插入点 ---
        private void UpdateDropMarginTarget(Vector2 pointerWorld)
        {
            var visible = this.Children().Where(c => c != _draggedItem && c != _placeholder).ToList();
            VisualElement closest = null;
            float closestDist = float.MaxValue;

            foreach (var child in visible)
            {
                float dist = Mathf.Abs(pointerWorld.y - child.worldBound.center.y);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = child;
                }
            }

            VisualElement newTarget = null;
            int newIndex = -1;
            bool newAbove = false;
            float spaceSize = _draggedItem != null
                ? (_draggedItem.resolvedStyle.height > 0 ? _draggedItem.resolvedStyle.height : _draggedItem.layout.height)
                : 0f;

            if (closest != null)
            {
                float childMidY = closest.worldBound.yMin + closest.worldBound.height * 0.5f;
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
                    newAbove = false;
                }
            }
            else if (visible.Count > 0)
            {
                var first = visible[0];
                var last = visible[visible.Count - 1];
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
                    newAbove = false;
                }
            }
            else
            {
                newIndex = 0;
                newTarget = null;
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

            if (_marginTarget != null)
            {
                if (_isMarginAbove)
                {
                    _marginTarget.style.marginTop = new Length(spaceSize, LengthUnit.Pixel);
                }
                else
                {
                    _marginTarget.style.marginBottom = new Length(spaceSize, LengthUnit.Pixel);
                }
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
    }
}
