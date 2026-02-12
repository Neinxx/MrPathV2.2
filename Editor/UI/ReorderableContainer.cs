using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.UI
{
    public class ReorderableContainer : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<ReorderableContainer, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        private VisualElement m_DraggedItem;
        private VisualElement m_TopLine; // 上方蓝线
        private VisualElement m_BottomLine; // 下方蓝线
        private VisualElement m_PlaceholderContainer; // 线容器，控制间距
        private int m_PlaceholderIndex = -1;
        private Vector2 m_DragStartOffset;
        private bool m_IsDragging = false;

       
        public ReorderableContainer()
        {
            style.flexDirection = FlexDirection.Column;

            // 创建占位容器（控制两条线的间距）
            m_PlaceholderContainer = new VisualElement();
            m_PlaceholderContainer.name = "placeholder-container";
            m_PlaceholderContainer.style.flexDirection = FlexDirection.Column;
            m_PlaceholderContainer.style.display = DisplayStyle.None;

            // 上方蓝线
            m_TopLine = new VisualElement();
            m_TopLine.name = "top-line";
            m_TopLine.style.height = 4;
            m_TopLine.style.backgroundColor = new Color(0.1f, 0.5f, 0.9f);
            m_TopLine.style.borderTopLeftRadius = 2;
            m_TopLine.style.borderTopRightRadius = 2;
            m_TopLine.style.borderBottomLeftRadius = 0;
            m_TopLine.style.borderBottomRightRadius = 0;
            m_TopLine.style.marginLeft = 2;
            m_TopLine.style.marginRight = 2;

            // 下方蓝线
            m_BottomLine = new VisualElement();
            m_BottomLine.name = "bottom-line";
            m_BottomLine.style.height = 4;
            m_BottomLine.style.backgroundColor = new Color(0.1f, 0.5f, 0.9f);
           m_TopLine.style.borderTopLeftRadius = 2;
            m_TopLine.style.borderTopRightRadius = 2;
            m_TopLine.style.borderBottomLeftRadius = 0;
            m_TopLine.style.borderBottomRightRadius = 0;
            m_BottomLine.style.marginLeft = 2;
            m_BottomLine.style.marginRight = 2;

            // 组装占位容器（两条线+中间间距）
            m_PlaceholderContainer.Add(m_TopLine);
            // 中间间距会在拖动时设置为被拖动元素的高度
            m_PlaceholderContainer.Add(m_BottomLine);

            // 添加到容器
            Add(m_PlaceholderContainer);

            // 注册拖动事件
            RegisterCallback<DragEnterEvent>(OnDragEnter);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragExitedEvent>(OnDragExited);
        }

        private Vector2 WorldToLocal(VisualElement element, Vector2 worldPos)
        {
            return worldPos - element.worldBound.position;
        }

        private void OnDragEnter(DragEnterEvent evt)
        {
            if (m_IsDragging) return;

            var draggedItem = DragAndDrop.GetGenericData("ReorderableItem") as VisualElement;
            if (draggedItem != null && draggedItem.parent == this)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                m_DraggedItem = draggedItem;
                m_IsDragging = true;

                // 记录初始偏移量
                Vector2 localMousePos = WorldToLocal(m_DraggedItem, evt.mousePosition);
                m_DragStartOffset = localMousePos;

                // 配置幽灵元素
                m_DraggedItem.style.position = Position.Absolute;
                m_DraggedItem.style.width = m_DraggedItem.layout.width;
                m_DraggedItem.style.height = m_DraggedItem.layout.height;
                m_DraggedItem.pickingMode = PickingMode.Ignore;
                m_DraggedItem.BringToFront();
                m_DraggedItem.AddToClassList("dragging");

                // 初始化双蓝线占位符：设置间距为被拖动元素的高度
                m_PlaceholderIndex = IndexOf(m_DraggedItem);
                // 在两条线之间添加与元素等高的间距
                SetPlaceholderSpacing(m_DraggedItem.layout.height);
                Insert(m_PlaceholderIndex, m_PlaceholderContainer);
                m_PlaceholderContainer.style.display = DisplayStyle.Flex;

                // 初始移动幽灵到鼠标位置
                UpdateGhostPosition(evt.mousePosition);

                evt.StopPropagation();
            }
        }

        // 设置两条线之间的间距（等于元素高度）
        private void SetPlaceholderSpacing(float height)
        {
            // 清除现有间距元素
            if (m_PlaceholderContainer.childCount == 3)
                m_PlaceholderContainer.RemoveAt(1);

            // 添加间距元素（高度等于被拖动元素）
            var spacing = new VisualElement();
            spacing.style.height = height;
            m_PlaceholderContainer.Insert(1, spacing);
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (!m_IsDragging || m_DraggedItem == null) return;

            UpdateGhostPosition(evt.mousePosition);

            int targetIndex = CalculateTargetIndex(evt);
            if (targetIndex != -1 && targetIndex != m_PlaceholderIndex)
            {
                SwapPlaceholderPosition(targetIndex);
            }

            evt.StopPropagation();
        }

        private void UpdateGhostPosition(Vector2 worldMousePos)
        {
            Vector2 containerLocalPos = WorldToLocal(this, worldMousePos);
            m_DraggedItem.style.top = containerLocalPos.y - m_DragStartOffset.y;
            m_DraggedItem.style.left = containerLocalPos.x - m_DragStartOffset.x;
        }

        private int CalculateTargetIndex(DragUpdatedEvent evt)
        {
            float mouseY = WorldToLocal(this, evt.mousePosition).y;
            int currentIndex = m_PlaceholderIndex;

            if (currentIndex > 0)
            {
                VisualElement upperElement = this[currentIndex - 1];
                if (upperElement != m_DraggedItem)
                {
                    Rect upperBounds = upperElement.layout;
                    float upperMidY = upperBounds.y + upperBounds.height / 2f;
                    if (mouseY < upperMidY)
                    {
                        return currentIndex - 1;
                    }
                }
            }

            if (currentIndex < childCount - 1)
            {
                VisualElement lowerElement = this[currentIndex + 1];
                if (lowerElement != m_DraggedItem)
                {
                    Rect lowerBounds = lowerElement.layout;
                    float lowerMidY = lowerBounds.y + lowerBounds.height / 2f;
                    if (mouseY > lowerMidY)
                    {
                        return currentIndex + 1;
                    }
                }
            }

            return currentIndex;
        }

        private void SwapPlaceholderPosition(int targetIndex)
        {
            Remove(m_PlaceholderContainer);
            Insert(targetIndex, m_PlaceholderContainer);
            m_PlaceholderIndex = targetIndex;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            FinalizeDrag();
            evt.StopPropagation();
        }

        private void OnDragExited(DragExitedEvent evt)
        {
            FinalizeDrag();
        }

        private void FinalizeDrag()
        {
            if (!m_IsDragging || m_DraggedItem == null) return;

            m_DraggedItem.style.position = Position.Relative;
            m_DraggedItem.style.width = StyleKeyword.Auto;
            m_DraggedItem.style.height = StyleKeyword.Auto;
            m_DraggedItem.style.top = StyleKeyword.Auto;
            m_DraggedItem.style.left = StyleKeyword.Auto;
            m_DraggedItem.pickingMode = PickingMode.Position;
            m_DraggedItem.RemoveFromClassList("dragging");

            if (m_PlaceholderIndex != -1 && Contains(m_PlaceholderContainer))
            {
                Insert(m_PlaceholderIndex, m_DraggedItem);
            }

            if (Contains(m_PlaceholderContainer))
            {
                Remove(m_PlaceholderContainer);
            }

            m_IsDragging = false;
            m_DraggedItem = null;
            m_PlaceholderIndex = -1;
        }
    }
}
