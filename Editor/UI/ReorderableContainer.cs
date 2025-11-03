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
        private const int c_TransitionTimeMs = 30; // 动画时长 (需匹配 USS)

        private bool m_IsDragging = false;
        private VisualElement m_DraggedItem;
        private int m_PlaceholderIndex = -1;

        // 拖动定位相关
        private Vector2 m_DragStartOffset; // 鼠标点击点到 m_DraggedItem 左上角的偏移 (容器局部坐标)
        private List<float> m_ChildMidYPositions; // 缓存子项的中线Y坐标 (容器局部坐标)
        private ScrollView m_ScrollView; // 所在滚动视图 (用于自动滚动与滚轮抑制)
        private float m_AutoScrollZonePx = 24f; // 触发自动滚动的边缘区域像素
        private float m_AutoScrollSpeedPx = 6f; // 自动滚动速度 (像素/帧)
        private float m_ScrollPrevOffsetY = 0f; // 拖拽前的滚动位置（用于恢复）
        private float m_IndexSwitchDeadZonePx = 8f; // 插入位置切换死区，减少来回跳动

        // --- 占位符元素 (双蓝线+间距) ---
        private VisualElement m_PlaceholderContainer;
        private VisualElement m_TopLine;
        private VisualElement m_BottomLine;

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
            m_PlaceholderContainer = new VisualElement { name = "placeholder-container" };
            m_PlaceholderContainer.style.flexDirection = FlexDirection.Column;
            m_PlaceholderContainer.style.display = DisplayStyle.None;

            // 样式定义 (简化为 C#，推荐在 USS 中定义)
            m_TopLine = CreateLineElement(true);
            m_BottomLine = CreateLineElement(false);

            // 组装占位容器 (TopLine + Spacing [运行时添加] + BottomLine)
            m_PlaceholderContainer.Add(m_TopLine);
            m_PlaceholderContainer.Add(m_BottomLine);

            Add(m_PlaceholderContainer);
        }

        private VisualElement CreateLineElement(bool isTop)
        {
            var line = new VisualElement();
            line.style.height = 4;
            line.style.backgroundColor = new Color(0.1f, 0.5f, 0.9f); // 蓝色
            line.style.marginLeft = 2;
            line.style.marginRight = 2;

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
            // 提前返回：如果正在拖动或不是有效的重排项
            if (m_IsDragging) return;

            VisualElement draggedItem = DragAndDrop.GetGenericData("ReorderableItem") as VisualElement;

            // 提前返回：如果不是本容器内的重排项
            if (draggedItem == null || draggedItem.parent != this)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.None;
                return;
            }

            // 1. 初始化状态
            m_DraggedItem = draggedItem;
            m_IsDragging = true;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            // 2. 记录初始偏移量和布局数据
            m_DragStartOffset = WorldToLocal(evt.mousePosition) - m_DraggedItem.layout.position;
            CacheChildMidYPositions(); // 缓存所有子项中线Y坐标
            // 2.1 关联 ScrollView 并抑制滚轮事件传播 (拖拽中避免滚动导致抖动)
            m_ScrollView = this.GetFirstAncestorOfType<ScrollView>();
            RegisterCallback<WheelEvent>(OnWheelWhileDragging, TrickleDown.TrickleDown);
            // 2.2 记录进入拖拽时的滚动位置，便于结束后恢复
            if (m_ScrollView != null) m_ScrollPrevOffsetY = m_ScrollView.scrollOffset.y;
            else m_ScrollPrevOffsetY = 0f;

            // 2.3 拖拽期间禁用过渡动画（通过类名，交由 USS 控制）
            AddToClassList("dragging-active");

            // 3. 配置幽灵元素 (脱离布局流)
            ConfigureDraggedItemAsGhost();

            // 4. 初始化并插入占位符
            m_PlaceholderIndex = IndexOf(m_DraggedItem);
            SetPlaceholderSpacing(m_DraggedItem.layout.height);
            Insert(m_PlaceholderIndex, m_PlaceholderContainer);
            m_PlaceholderContainer.style.display = DisplayStyle.Flex;

            // 5. 初始移动幽灵到鼠标位置
            UpdateGhostPosition(evt.mousePosition);

            evt.StopPropagation();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            // 提前返回：非拖动状态
            if (!m_IsDragging || m_DraggedItem == null) return;

            // 保持拖拽视觉为“移动”，避免出现禁止图标
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            // 1. 实时更新幽灵元素位置 (确保跟随鼠标)
            UpdateGhostPosition(evt.mousePosition);

            // 1.1 边缘自动滚动 (便于跨越视口插入)
            MaybeAutoScroll(evt.mousePosition);

            // 2. 计算目标索引并移动占位符
            int targetIndex = CalculateTargetIndex(evt);
            if (targetIndex != m_PlaceholderIndex)
            {
                SwapPlaceholderPosition(targetIndex);
                // 不再在占位符移动时重算中线，避免阈值随布局变动导致来回切换
            }

            evt.StopPropagation();
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
            if (m_IsDragging)
            {
                StartFinalizeAnimation();
            }
        }

        // --- 辅助方法 ---

        private void ConfigureDraggedItemAsGhost()
        {
            m_DraggedItem.style.position = Position.Absolute;
            m_DraggedItem.style.width = m_DraggedItem.layout.width;
            m_DraggedItem.style.height = m_DraggedItem.layout.height;
            m_DraggedItem.pickingMode = PickingMode.Ignore; // 忽略鼠标事件
            m_DraggedItem.BringToFront();
            m_DraggedItem.AddToClassList("dragging"); // 应用 USS 动画样式
        }

        private void CacheChildMidYPositions()
        {
            m_ChildMidYPositions = new List<float>();

            // 缓存所有非拖动子项的中线Y坐标 (容器局部坐标)
            foreach (var child in Children().Where(c => c != m_DraggedItem && c != m_PlaceholderContainer))
            {
                float midY = child.layout.y + child.layout.height / 2f;
                m_ChildMidYPositions.Add(midY);
            }
        }

        private void UpdateGhostPosition(Vector2 worldMousePos)
        {
            // 将鼠标世界坐标转换为容器的局部坐标
            Vector2 containerLocalPos = WorldToLocal(worldMousePos);

            // 设置 Ghost 的绝对位置 (实现鼠标点击点跟随)
            // 修复：限制 top 在容器范围内，left 固定为 0，避免滚动条因布局外溢跳动
            float desiredTop = containerLocalPos.y - m_DragStartOffset.y;
            float maxTop = layout.height - m_DraggedItem.layout.height;
            if (maxTop < 0) maxTop = 0;
            m_DraggedItem.style.top = Mathf.Clamp(desiredTop, 0, maxTop);
            m_DraggedItem.style.left = 0;
        }

        private int CalculateTargetIndex(DragUpdatedEvent evt)
        {
            // 使用“被拖动元素的中心线”作为换位判定依据（更贴近原生规则）
            Vector2 containerLocalMousePos = WorldToLocal(evt.mousePosition);
            float ghostTopY = containerLocalMousePos.y - m_DragStartOffset.y; // 幽灵项顶部Y（容器局部）
            float ghostCenterY = ghostTopY + (m_DraggedItem?.layout.height ?? 0f) / 2f; // 中心线Y

            int insertionIndex = 0;
            float closestBoundaryDist = float.MaxValue;

            // 遍历缓存的中线Y坐标，找到插入点
            // insertionIndex 是在**不包含 Ghost 元素**的逻辑列表中的索引
            for (int i = 0; i < m_ChildMidYPositions.Count; i++)
            {
                if (ghostCenterY > m_ChildMidYPositions[i])
                {
                    insertionIndex = i + 1;
                }
                else
                {
                    break;
                }
                // 记录与最近边界的距离，用于插入死区判断
                float dist = Mathf.Abs(ghostCenterY - m_ChildMidYPositions[i]);
                if (dist < closestBoundaryDist) closestBoundaryDist = dist;
            }

            // 在边界附近设置死区，减少来回跳动（保留当前占位索引）
            if (m_PlaceholderIndex >= 0 && closestBoundaryDist <= m_IndexSwitchDeadZonePx)
            {
                return m_PlaceholderIndex;
            }

            // 实际插入到 Hierarchy 中的索引就是 insertionIndex
            return insertionIndex;
        }

        private void SetPlaceholderSpacing(float height)
        {
            // 1. 如果已经有间距元素，先移除
            if (m_PlaceholderContainer.childCount == 3)
                m_PlaceholderContainer.RemoveAt(1);

            // 2. 创建新的间距元素
            var spacing = new VisualElement();
            spacing.AddToClassList("placeholder-spacing-element");

            // 3. 直接设置为目标高度：避免 height 过渡动画导致 ScrollView 高度变化而产生跳动
            spacing.style.height = height;

            // 4. 将其插入到占位容器
            m_PlaceholderContainer.Insert(1, spacing);
        }

        private void SwapPlaceholderPosition(int targetIndex)
        {
            // 确保 targetIndex 范围有效
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > childCount) targetIndex = childCount;
            // 将“非拖动子项索引”映射为真实层级索引
            int draggedIndex = IndexOf(m_DraggedItem);
            int actualIndex = (targetIndex <= draggedIndex) ? targetIndex : targetIndex + 1;

            Remove(m_PlaceholderContainer);
            Insert(actualIndex, m_PlaceholderContainer);
            // 用目标的“非拖动子项索引”作为占位符逻辑索引，避免受拖动项影响出现抖动
            m_PlaceholderIndex = targetIndex;
        }

        // --- 动画与清理 ---

        private void StartFinalizeAnimation()
        {
            // 提前返回
            if (!m_IsDragging || m_DraggedItem == null) return;

            // 1. 计算 Ghost 最终目标位置：占位符当前的布局位置
            Vector2 targetLayoutPos = m_PlaceholderContainer.layout.position;

            // 2. 保留占位符的间距，临时隐藏上下蓝线，避免高度突变造成滚动条上跳
            m_TopLine.style.display = DisplayStyle.None;
            m_BottomLine.style.display = DisplayStyle.None;

            // 3. 设置 Ghost 的目标位置 (触发 Ghost 飞入动画)
            m_DraggedItem.style.top = targetLayoutPos.y;
            m_DraggedItem.style.left = targetLayoutPos.x;

            // 4. 安排任务，在动画结束后执行 Hierarchy 插入和样式清理
            this.schedule.Execute(FinalizeDrag).StartingIn(c_TransitionTimeMs);

        }

        private void FinalizeDrag()
        {
            // 提前返回
            if (!m_IsDragging || m_DraggedItem == null) return;

            // 1. 插入到最终位置
            if (m_PlaceholderIndex != -1)
            {
                // 占位符已经被移除，m_PlaceholderIndex 为“非拖动子项”的逻辑索引
                // 需要将其映射为真实层级索引（包含拖动项本身）
                int draggedIndex = IndexOf(m_DraggedItem);
                int actualIndex = (m_PlaceholderIndex <= draggedIndex) ? m_PlaceholderIndex : m_PlaceholderIndex + 1;
                // 插入前设置淡入动画的初始透明度
                m_DraggedItem.style.opacity = 0f;
                Insert(actualIndex, m_DraggedItem);
            }

            // 2. 清理样式 (恢复到布局流中)
            m_DraggedItem.style.position = Position.Relative;
            m_DraggedItem.style.width = StyleKeyword.Auto;
            m_DraggedItem.style.height = StyleKeyword.Auto;
            m_DraggedItem.style.top = StyleKeyword.Auto;
            m_DraggedItem.style.left = StyleKeyword.Auto;
            m_DraggedItem.pickingMode = PickingMode.Position;
            m_DraggedItem.RemoveFromClassList("dragging");
            // 应用插入后淡入效果（交由 USS 控制），并在下一帧提升到不透明
            var fadeItem = m_DraggedItem; // 捕获局部引用，避免后续清理将其置空
            fadeItem?.AddToClassList("reordered-fade-in");
            this.schedule.Execute(() =>
            {
                if (fadeItem != null)
                {
                    fadeItem.style.opacity = 1f;
                }
            }).ExecuteLater(1);

            // 2.1 清理占位符与样式
            if (Contains(m_PlaceholderContainer)) Remove(m_PlaceholderContainer);
            m_TopLine.style.display = DisplayStyle.Flex;
            m_BottomLine.style.display = DisplayStyle.Flex;
            RemoveFromClassList("dragging-active");

            // 2.2 恢复滚动位置，避免拖拽结束时滚动条上移
            if (m_ScrollView != null)
            {
                float viewportH = m_ScrollView.contentViewport.layout.height;
                float contentH = m_ScrollView.contentContainer.layout.height;
                float max = Mathf.Max(0f, contentH - viewportH);
                var offset = m_ScrollView.scrollOffset;
                offset.y = Mathf.Clamp(m_ScrollPrevOffsetY, 0f, max);
                m_ScrollView.scrollOffset = offset;
            }

            // 3. 清理状态
            m_IsDragging = false;
            m_DraggedItem = null;
            m_PlaceholderIndex = -1;
            // 3.1 解除滚轮事件抑制，清空 ScrollView 引用
            UnregisterCallback<WheelEvent>(OnWheelWhileDragging, TrickleDown.TrickleDown);
            m_ScrollView = null;
        }

        // --- 事件与自动滚动 ---

        private void OnWheelWhileDragging(WheelEvent evt)
        {
            if (!m_IsDragging) return;
            // 抑制滚轮事件，避免拖拽中视图滚动造成抖动和误插入
            evt.StopPropagation();
        }

        private void MaybeAutoScroll(Vector2 worldMousePos)
        {
            if (m_ScrollView == null) return;

            Rect svWorld = m_ScrollView.worldBound;
            float zone = m_AutoScrollZonePx;

            // 计算滚动目标
            var offset = m_ScrollView.scrollOffset;
            float viewportH = m_ScrollView.contentViewport.layout.height;
            float contentH = m_ScrollView.contentContainer.layout.height;
            float max = Mathf.Max(0f, contentH - viewportH);

            bool scrolled = false;

            if (worldMousePos.y < svWorld.yMin + zone)
            {
                offset.y = Mathf.Max(0f, offset.y - m_AutoScrollSpeedPx);
                scrolled = true;
            }
            else if (worldMousePos.y > svWorld.yMax - zone)
            {
                offset.y = Mathf.Min(max, offset.y + m_AutoScrollSpeedPx);
                scrolled = true;
            }

            if (scrolled)
            {
                m_ScrollView.scrollOffset = offset;
            }
        }
    }
}
