using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2.Editor.UI
{
    public class DraggableInsertItem : VisualElement
    {
        // --- UXML/工厂定义 ---
        public new class UxmlFactory : UxmlFactory<DraggableInsertItem, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        // --- 静态属性 (用于全局状态跟踪) ---
        public static DraggableInsertItem CurrentlyDraggedItem { get; private set; }

        // --- 实例字段 ---
        private DropContainer m_Container;           // 父级容器
        private int m_StartIndex = -1;               // 拖动前的原始索引
        private bool m_IsDragging = false;           // 拖动状态

        // 拖动定位相关
        private Vector2 m_OriginalLayoutPos;         // 拖动开始时，元素的布局起始位置
        private Vector2 m_PointerOffset;             // 鼠标点击点到元素左上角的局部偏移
        private Translate m_StartTranslate = Translate.None(); // 动画返回原点的 Translate (通常为 None)

        // 布局计算相关
        public float FullHeight { get; private set; } // 元素总高度 (内容 + margin)

        // --- 构造函数与初始化 ---
        public DraggableInsertItem()
        {
            AddToClassList("draggable-item");

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // 计算总高度 (包含 margin)，用于容器撑开空间
            float height = resolvedStyle.height;
            float marginTop = resolvedStyle.marginTop;
            float marginBottom = resolvedStyle.marginBottom;
            FullHeight = height + marginTop + marginBottom;
        }

        // --- 核心手势处理 ---

        private void OnPointerDown(PointerDownEvent evt)
        {
            // 提前返回：如果已有元素在拖动或不是左键
            if (CurrentlyDraggedItem != null || evt.button != 0) return;

            // 1. 状态初始化
            m_IsDragging = true;
            CurrentlyDraggedItem = this;

            // 2. 查找并验证容器
            m_Container = FindContainer();
            if (m_Container == null) return;

            // 3. 记录初始数据
            m_StartIndex = m_Container.IndexOf(this);
            m_OriginalLayoutPos = this.layout.position;
            m_PointerOffset = evt.localPosition; // 鼠标到元素左上角的局部偏移

            // 4. 应用拖动样式 (脱离布局流并隐藏占位)
            AddToClassList("draggable-item--dragging");
            // style.display = DisplayStyle.None;
            style.position = Position.Absolute;
            // 5. 设置初始拖动位置 (实现鼠标点击点跟随元素)
            Vector2 pointerPos2D = new(evt.position.x, evt.position.y);

            // Translate = 鼠标世界位置 - 鼠标偏移量 - 元素原始布局位置
            Vector2 translateDelta = pointerPos2D - m_PointerOffset - m_OriginalLayoutPos;
            style.translate = new Translate(translateDelta.x, translateDelta.y, 0);

            // 6. 捕获手势
            this.CapturePointer(evt.pointerId);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            evt.StopPropagation();
        }

        private DropContainer FindContainer()
        {
            VisualElement parent = this.parent;
            while (parent != null && !(parent is DropContainer))
            {
                parent = parent.parent;
            }
            if (parent == null)
            {
                Debug.LogError("DraggableInsertItem 必须放在 DropContainer 中!");
                return null;
            }
            return parent as DropContainer;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            // 提前返回：非拖动状态
            if (!m_IsDragging) return;

            // 1. 实时计算 Translate (保持点击点对齐)
            Vector2 pointerPos2D = new Vector2(evt.position.x, evt.position.y);
            Vector2 translateDelta = pointerPos2D - m_PointerOffset - m_OriginalLayoutPos;

            style.translate = new Translate(translateDelta.x, translateDelta.y, 0);

            // 2. 通知容器更新插入点
            m_Container?.UpdateDropTarget(this, evt.position);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            // 提前返回：非当前拖动元素
            if (!m_IsDragging || CurrentlyDraggedItem != this) return;

            HandleDrop(false); // 正常释放
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // 提前返回：非拖动状态
            if (!m_IsDragging) return;

            HandleDrop(true); // 丢失焦点，当作取消处理
        }

        // --- 放置逻辑 ---

        private void HandleDrop(bool cancel)
        {
            m_IsDragging = false;
            UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            this.ReleasePointer(PointerId.mousePointerId);

            bool droppedSuccessfully = false;

            if (!cancel)
            {
                // 尝试通知容器处理放置 (如果成功，容器将负责 ResetStylesAfterDrop)
                droppedSuccessfully = m_Container.HandleDrop(this, FullHeight);
            }

            if (!droppedSuccessfully)
            {
                // 放置失败或取消：动画返回原位并立即清理样式
                style.translate = m_StartTranslate;
                ResetStylesAfterDrop();
            }

            // 清理全局状态和容器占位
            CurrentlyDraggedItem = null;
            m_Container?.ClearDropTarget();
        }

        // --- 样式清理 (由 DropContainer 在动画完成后调用) ---

        /// <summary>
        /// 当放置动画完成后，重置所有样式，使其回到布局流中。
        /// </summary>
        public void ResetStylesAfterDrop()
        {
            RemoveFromClassList("draggable-item--dragging");
            style.translate = Translate.None();
            style.position = Position.Relative;
            style.display = DisplayStyle.Flex; // 恢复显示，回到布局流
        }
    }
}