using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace MrPathV2.Editor.UI
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

            // 1. 实时更新幽灵元素位置 (确保跟随鼠标)
            UpdateGhostPosition(evt.mousePosition);

            // 2. 计算目标索引并移动占位符
            int targetIndex = CalculateTargetIndex(evt);
            if (targetIndex != m_PlaceholderIndex)
            {
                SwapPlaceholderPosition(targetIndex);
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
            foreach (var child in Children().Where(c => c != m_DraggedItem))
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
            m_DraggedItem.style.top = containerLocalPos.y - m_DragStartOffset.y;
            m_DraggedItem.style.left = containerLocalPos.x - m_DragStartOffset.x;
        }

        private int CalculateTargetIndex(DragUpdatedEvent evt)
        {
            // 将鼠标位置转换为容器局部坐标
            Vector2 containerLocalMousePos = WorldToLocal(evt.mousePosition);
            float mouseY = containerLocalMousePos.y;

            int insertionIndex = 0;

            // 遍历缓存的中线Y坐标，找到插入点
            // insertionIndex 是在**不包含 Ghost 元素**的逻辑列表中的索引
            for (int i = 0; i < m_ChildMidYPositions.Count; i++)
            {
                if (mouseY > m_ChildMidYPositions[i])
                {
                    insertionIndex = i + 1;
                }
                else
                {
                    break;
                }
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
            spacing.AddToClassList("placeholder-spacing-element"); // 💥 确保添加了类名

            // 3. 将其初始高度设置为 0，实现“收缩”到 0 的效果
            spacing.style.height = 0;

            // 4. 将其插入到占位容器
            m_PlaceholderContainer.Insert(1, spacing);

            // 5. 安排一帧后，将高度设置为目标高度 (这会触发 USS 的 height 过渡动画)
            // 必须使用 schedule.Execute 延迟执行，否则 style.height = 0 会被立即覆盖，过渡不生效。
            spacing.schedule.Execute(() =>
            {
                spacing.style.height = height;
            }).ExecuteLater(1); // 延迟一帧执行
            
        }

        private void SwapPlaceholderPosition(int targetIndex)
        {
            // 确保 targetIndex 范围有效
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > childCount) targetIndex = childCount;

            Remove(m_PlaceholderContainer);
            Insert(targetIndex, m_PlaceholderContainer);
            m_PlaceholderIndex = targetIndex;
        }

        // --- 动画与清理 ---

        private void StartFinalizeAnimation()
        {
            // 提前返回
            if (!m_IsDragging || m_DraggedItem == null) return;

            // 1. 计算 Ghost 最终目标位置：占位符当前的布局位置
            Vector2 targetLayoutPos = m_PlaceholderContainer.layout.position;

            // 2. 移除占位符 (触发列表项的布局动画)
            if (Contains(m_PlaceholderContainer))
            {
                Remove(m_PlaceholderContainer);
            }

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
                // 占位符已经被移除，直接将 Ghost 插入到占位符原有的索引位置
                Insert(m_PlaceholderIndex, m_DraggedItem);
            }

            // 2. 清理样式 (恢复到布局流中)
            m_DraggedItem.style.position = Position.Relative;
            m_DraggedItem.style.width = StyleKeyword.Auto;
            m_DraggedItem.style.height = StyleKeyword.Auto;
            m_DraggedItem.style.top = StyleKeyword.Auto;
            m_DraggedItem.style.left = StyleKeyword.Auto;
            m_DraggedItem.pickingMode = PickingMode.Position;
            m_DraggedItem.RemoveFromClassList("dragging");

            // 3. 清理状态
            m_IsDragging = false;
            m_DraggedItem = null;
            m_PlaceholderIndex = -1;
        }
    }
}