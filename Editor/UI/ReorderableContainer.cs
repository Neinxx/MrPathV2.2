using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.UI
{
    public class ReorderableContainer : VisualElement
    {
        // --- UXML/工厂定义 ---
        public new class UxmlFactory : UxmlFactory<ReorderableContainer, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        // --- 常量与状态 ---
        private const int CTransitionTimeMs = 30; // 动画时长 (需匹配 USS)

        private bool _mIsDragging;
        private VisualElement _mDraggedItem;
        private int _mPlaceholderIndex = -1;

        // 拖动定位相关
        private Vector2 _mDragStartOffset; // 鼠标点击点到 m_DraggedItem 左上角的偏移 (容器局部坐标)
        private List<float> _mChildMidYPositions; // 缓存子项的中线Y坐标 (容器局部坐标)
        private ScrollView _mScrollView; // 所在滚动视图 (用于自动滚动与滚轮抑制)
        private const float MAutoScrollZonePx = 24f; // 触发自动滚动的边缘区域像素
        private const float MAutoScrollSpeedPx = 6f; // 自动滚动速度 (像素/帧)
        private float _mScrollPrevOffsetY; // 拖拽前的滚动位置（用于恢复）
        private const float MIndexSwitchDeadZonePx = 8f; // 插入位置切换死区，减少来回跳动

        // --- 占位符元素 (双蓝线+间距) ---
        private VisualElement _mPlaceholderContainer;
        private VisualElement _mTopLine;
        private VisualElement _mBottomLine;

        // --- 构造函数与初始化 ---

        public ReorderableContainer()
        {
            style.flexDirection = FlexDirection.Column;
            InitializePlaceholderElements();

            // 注册拖放事件
            RegisterCallback<DragEnterEvent>(OnDragEnter);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragExitedEvent>(OnDragExited);
        }

        private void InitializePlaceholderElements()
        {
            // 占位容器
            _mPlaceholderContainer = new VisualElement { name = "placeholder-container",
                style =
                {
                    flexDirection = FlexDirection.Column,
                    display = DisplayStyle.None
                }
            };

            // 样式定义 (简化为 C#，推荐在 USS 中定义)
            _mTopLine = CreateLineElement(true);
            _mBottomLine = CreateLineElement(false);

            // 组装占位容器 (TopLine + Spacing [运行时添加] + BottomLine)
            _mPlaceholderContainer.Add(_mTopLine);
            _mPlaceholderContainer.Add(_mBottomLine);

            Add(_mPlaceholderContainer);
        }

        private static VisualElement CreateLineElement(bool isTop)
        {
            var line = new VisualElement
            {
                style =
                {
                    height = 4,
                    backgroundColor = new Color(0.1f, 0.5f, 0.9f), // 蓝色
                    marginLeft = 2,
                    marginRight = 2
                }
            };

            // 圆角
            line.style.borderTopLeftRadius = line.style.borderTopRightRadius = isTop ? 2 : 0;
            line.style.borderBottomLeftRadius = line.style.borderBottomRightRadius = isTop ? 0 : 2;
            // 添加类名，便于 USS 控制占位线透明度与过渡
            line.AddToClassList("placeholder-line");
            line.AddToClassList(isTop ? "placeholder-line-top" : "placeholder-line-bottom");
            return line;
        }

        // --- 坐标辅助函数 (避免冗余计算) ---

        private Vector2 WorldToLocal(Vector2 worldPos)
        {
            return worldPos - worldBound.position;
        }

        // --- 拖放事件处理 ---

        private void OnDragEnter(DragEnterEvent evt)
        {
            TryBeginDrag(evt.mousePosition);
            evt.StopPropagation();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            // 若拖拽尚未初始化（例如从容器内部开始拖拽），尝试初始化
            if (!_mIsDragging || _mDraggedItem == null)
            {
                TryBeginDrag(evt.mousePosition);
            }

            // 提前返回：非拖动状态
            if (!_mIsDragging || _mDraggedItem == null) return;

            // 保持拖拽视觉为“移动”，避免出现禁止图标
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            // 1. 实时更新幽灵元素位置 (确保跟随鼠标)
            UpdateGhostPosition(evt.mousePosition);

            // 1.1 边缘自动滚动 (便于跨越视口插入)
            MaybeAutoScroll(evt.mousePosition);

            // 2. 计算目标索引并移动占位符
            var targetIndex = CalculateTargetIndex(evt);
            if (targetIndex != _mPlaceholderIndex)
            {
                SwapPlaceholderPosition(targetIndex);
                // 不再在占位符移动时重算中线，避免阈值随布局变动导致来回切换
            }

            evt.StopPropagation();
        }

        // 封装：尝试初始化拖拽状态（可在 DragEnter/DragUpdated 调用）
        private void TryBeginDrag(Vector2 mousePosition)
        {
            if (_mIsDragging) return;

            // 如果不是本容器内的重排项，退出
            if (DragAndDrop.GetGenericData("ReorderableItem") is not VisualElement draggedItem || draggedItem.parent != this)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.None;
                return;
            }

            // 1. 初始化状态
            _mDraggedItem = draggedItem;
            _mIsDragging = true;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            // 2. 记录初始偏移量和布局数据
            _mDragStartOffset = WorldToLocal(mousePosition) - _mDraggedItem.layout.position;
            CacheChildMidYPositions();
            // 2.1 关联 ScrollView 并抑制滚轮事件传播
            _mScrollView = this.GetFirstAncestorOfType<ScrollView>();
            RegisterCallback<WheelEvent>(OnWheelWhileDragging, TrickleDown.TrickleDown);
            // 2.2 记录进入拖拽时的滚动位置
            _mScrollPrevOffsetY = _mScrollView != null ? _mScrollView.scrollOffset.y : 0f;

            // 2.3 拖拽期间禁用过渡动画
            AddToClassList("dragging-active");

            // 3. 配置幽灵元素
            ConfigureDraggedItemAsGhost();

            // 4. 初始化并插入占位符
            _mPlaceholderIndex = IndexOf(_mDraggedItem);
            SetPlaceholderSpacing(_mDraggedItem.layout.height);
            Insert(_mPlaceholderIndex, _mPlaceholderContainer);
            _mPlaceholderContainer.style.display = DisplayStyle.Flex;

            // 5. 初始移动幽灵到鼠标位置
            UpdateGhostPosition(mousePosition);
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            StartFinalizeAnimation();
            evt.StopPropagation();
        }

        private void OnDragExited(DragExitedEvent evt)
        {
            // 如果拖动退出发生在容器内部，也会触发 FinalizeDrag
            // 如果需要支持外部放置，需要引入更复杂的逻辑
            if (_mIsDragging)
            {
                StartFinalizeAnimation();
            }
        }

        // --- 辅助方法 ---

        private void ConfigureDraggedItemAsGhost()
        {
            _mDraggedItem.style.position = Position.Absolute;
            _mDraggedItem.style.width = _mDraggedItem.layout.width;
            _mDraggedItem.style.height = _mDraggedItem.layout.height;
            _mDraggedItem.pickingMode = PickingMode.Ignore; // 忽略鼠标事件
            _mDraggedItem.BringToFront();
            _mDraggedItem.AddToClassList("dragging"); // 应用 USS 动画样式
        }

        private void CacheChildMidYPositions()
        {
            _mChildMidYPositions = new List<float>();

            // 缓存所有非拖动子项的中线Y坐标 (容器局部坐标)
            foreach (var child in Children().Where(c => c != _mDraggedItem && c != _mPlaceholderContainer))
            {
                var midY = child.layout.y + child.layout.height / 2f;
                _mChildMidYPositions.Add(midY);
            }
        }

        private void UpdateGhostPosition(Vector2 worldMousePos)
        {
            // 将鼠标世界坐标转换为容器的局部坐标
            var containerLocalPos = WorldToLocal(worldMousePos);

            // 设置 Ghost 的绝对位置 (实现鼠标点击点跟随)
            // 修复：限制 top 在容器范围内，left 固定为 0，避免滚动条因布局外溢跳动
            var desiredTop = containerLocalPos.y - _mDragStartOffset.y;
            var maxTop = layout.height - _mDraggedItem.layout.height;
            if (maxTop < 0) maxTop = 0;
            _mDraggedItem.style.top = Mathf.Clamp(desiredTop, 0, maxTop);
            _mDraggedItem.style.left = 0;
        }

        private int CalculateTargetIndex(DragUpdatedEvent evt)
        {
            // 使用“被拖动元素的中心线”作为换位判定依据（更贴近原生规则）
            var containerLocalMousePos = WorldToLocal(evt.mousePosition);
            var ghostTopY = containerLocalMousePos.y - _mDragStartOffset.y; // 幽灵项顶部Y（容器局部）
            var ghostCenterY = ghostTopY + (_mDraggedItem?.layout.height ?? 0f) / 2f; // 中心线Y

            var insertionIndex = 0;
            var closestBoundaryDist = float.MaxValue;

            // 遍历缓存的中线Y坐标，找到插入点
            // insertionIndex 是在**不包含 Ghost 元素**的逻辑列表中的索引
            for (var i = 0; i < _mChildMidYPositions.Count; i++)
            {
                if (ghostCenterY > _mChildMidYPositions[i])
                {
                    insertionIndex = i + 1;
                }
                else
                {
                    break;
                }
                // 记录与最近边界的距离，用于插入死区判断
                var dist = Mathf.Abs(ghostCenterY - _mChildMidYPositions[i]);
                if (dist < closestBoundaryDist) closestBoundaryDist = dist;
            }

            // 在边界附近设置死区，减少来回跳动（保留当前占位索引）
            if (_mPlaceholderIndex >= 0 && closestBoundaryDist <= MIndexSwitchDeadZonePx)
            {
                return _mPlaceholderIndex;
            }

            // 实际插入到 Hierarchy 中的索引就是 insertionIndex
            return insertionIndex;
        }

        private void SetPlaceholderSpacing(float height)
        {
            // 1. 如果已经有间距元素，先移除
            if (_mPlaceholderContainer.childCount == 3)
                _mPlaceholderContainer.RemoveAt(1);

            // 2. 创建新的间距元素
            var spacing = new VisualElement();
            spacing.AddToClassList("placeholder-spacing-element");

            // 3. 直接设置为目标高度：避免 height 过渡动画导致 ScrollView 高度变化而产生跳动
            spacing.style.height = height;

            // 4. 将其插入到占位容器
            _mPlaceholderContainer.Insert(1, spacing);
        }

        private void SwapPlaceholderPosition(int targetIndex)
        {
            // 确保 targetIndex 范围有效
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > childCount) targetIndex = childCount;
            // 将“非拖动子项索引”映射为真实层级索引
            var draggedIndex = IndexOf(_mDraggedItem);
            var actualIndex = (targetIndex <= draggedIndex) ? targetIndex : targetIndex + 1;

            Remove(_mPlaceholderContainer);
            Insert(actualIndex, _mPlaceholderContainer);
            // 用目标的“非拖动子项索引”作为占位符逻辑索引，避免受拖动项影响出现抖动
            _mPlaceholderIndex = targetIndex;
        }

        // --- 动画与清理 ---

        private void StartFinalizeAnimation()
        {
            // 提前返回
            if (!_mIsDragging || _mDraggedItem == null) return;

            // 1. 计算 Ghost 最终目标位置：占位符当前的布局位置
            var targetLayoutPos = _mPlaceholderContainer.layout.position;

            // 2. 保留占位符的间距，临时隐藏上下蓝线，避免高度突变造成滚动条上跳
            _mTopLine.style.display = DisplayStyle.None;
            _mBottomLine.style.display = DisplayStyle.None;

            // 3. 设置 Ghost 的目标位置 (触发 Ghost 飞入动画)
            _mDraggedItem.style.top = targetLayoutPos.y;
            _mDraggedItem.style.left = targetLayoutPos.x;

            // 4. 安排任务，在动画结束后执行 Hierarchy 插入和样式清理
            this.schedule.Execute(FinalizeDrag).StartingIn(CTransitionTimeMs);

        }

        private void FinalizeDrag()
        {
            // 提前返回
            if (!_mIsDragging || _mDraggedItem == null) return;

            // 1. 插入到最终位置
            if (_mPlaceholderIndex != -1)
            {
                // 占位符已经被移除，m_PlaceholderIndex 为“非拖动子项”的逻辑索引
                // 需要将其映射为真实层级索引（包含拖动项本身）
                var draggedIndex = IndexOf(_mDraggedItem);
                var actualIndex = (_mPlaceholderIndex <= draggedIndex) ? _mPlaceholderIndex : _mPlaceholderIndex + 1;
                // 插入前设置淡入动画的初始透明度
                _mDraggedItem.style.opacity = 0f;
                Insert(actualIndex, _mDraggedItem);
            }

            // 2. 清理样式 (恢复到布局流中)
            _mDraggedItem.style.position = Position.Relative;
            _mDraggedItem.style.width = StyleKeyword.Auto;
            _mDraggedItem.style.height = StyleKeyword.Auto;
            _mDraggedItem.style.top = StyleKeyword.Auto;
            _mDraggedItem.style.left = StyleKeyword.Auto;
            _mDraggedItem.pickingMode = PickingMode.Position;
            _mDraggedItem.RemoveFromClassList("dragging");
            // 应用插入后淡入效果（交由 USS 控制），并在下一帧提升到不透明
            var fadeItem = _mDraggedItem; // 捕获局部引用，避免后续清理将其置空
            fadeItem?.AddToClassList("reordered-fade-in");
            this.schedule.Execute(() =>
            {
                if (fadeItem != null)
                {
                    fadeItem.style.opacity = 1f;
                }
            }).ExecuteLater(1);

            // 2.1 清理占位符与样式
            if (Contains(_mPlaceholderContainer)) Remove(_mPlaceholderContainer);
            _mTopLine.style.display = DisplayStyle.Flex;
            _mBottomLine.style.display = DisplayStyle.Flex;
            RemoveFromClassList("dragging-active");

            // 2.2 恢复滚动位置，避免拖拽结束时滚动条上移
            if (_mScrollView != null)
            {
                var viewportH = _mScrollView.contentViewport.layout.height;
                var contentH = _mScrollView.contentContainer.layout.height;
                var max = Mathf.Max(0f, contentH - viewportH);
                var offset = _mScrollView.scrollOffset;
                offset.y = Mathf.Clamp(_mScrollPrevOffsetY, 0f, max);
                _mScrollView.scrollOffset = offset;
            }

            // 3. 清理状态
            _mIsDragging = false;
            _mDraggedItem = null;
            _mPlaceholderIndex = -1;
            // 3.1 解除滚轮事件抑制，清空 ScrollView 引用
            UnregisterCallback<WheelEvent>(OnWheelWhileDragging, TrickleDown.TrickleDown);
            _mScrollView = null;
        }

        // --- 事件与自动滚动 ---

        private void OnWheelWhileDragging(WheelEvent evt)
        {
            if (!_mIsDragging) return;
            // 抑制滚轮事件，避免拖拽中视图滚动造成抖动和误插入
            evt.StopPropagation();
        }

        private void MaybeAutoScroll(Vector2 worldMousePos)
        {
            if (_mScrollView == null) return;

            var svWorld = _mScrollView.worldBound;

            // 计算滚动目标
            var offset = _mScrollView.scrollOffset;
            var viewportH = _mScrollView.contentViewport.layout.height;
            var contentH = _mScrollView.contentContainer.layout.height;
            var max = Mathf.Max(0f, contentH - viewportH);

            var scrolled = false;

            if (worldMousePos.y < svWorld.yMin + MAutoScrollZonePx)
            {
                offset.y = Mathf.Max(0f, offset.y - MAutoScrollSpeedPx);
                scrolled = true;
            }
            else if (worldMousePos.y > svWorld.yMax - MAutoScrollZonePx)
            {
                offset.y = Mathf.Min(max, offset.y + MAutoScrollSpeedPx);
                scrolled = true;
            }

            if (scrolled)
            {
                _mScrollView.scrollOffset = offset;
            }
        }
    }
}
