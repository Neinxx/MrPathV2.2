using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace __temp.MrPathV2.Editor.UI
{
    public class ReorderableItem : VisualElement
    {
        // --- UXML/工厂定义 ---
        public new class UxmlFactory : UxmlFactory<ReorderableItem, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        // --- 拖拽句柄控制 ---
        private VisualElement _mDragHandle;   // 指定拖拽句柄
        private bool _mHandleOnly;    // 仅在句柄上允许拖动

        // --- 构造函数 ---

        public ReorderableItem()
        {
            // 设置可拖拽光标
            style.cursor = new StyleCursor(Cursorer.DefaultCursor(Cursorer.CursorType.MoveArrow));
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            // 给元素添加类名，以便应用 USS 样式
            AddToClassList("reorderable-item"); 
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || parent is not ReorderableContainer)
            {
                return;
            }
            // 仅句柄拖动限制：允许句柄及其子元素触发
            if (_mHandleOnly && _mDragHandle != null)
            {
                var targetVe = evt.target as VisualElement;
                var onHandleOrDescendant = targetVe != null && (targetVe == _mDragHandle || _mDragHandle.Contains(targetVe));
                if (!onHandleOrDescendant)
                {
                    return;
                }
            }
            
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("ReorderableItem", this);
            DragAndDrop.StartDrag("Reordering");

            evt.StopPropagation();
        }

        /// <summary>
        /// 指定拖拽句柄；当 handleOnly 为 true 时，只有句柄接收的 PointerDown 触发拖动。
        /// </summary>
        public void SetDragHandle(VisualElement handle, bool handleOnly = true)
        {
            _mDragHandle = handle;
            _mHandleOnly = handleOnly;

            if (_mDragHandle != null)
            {
                _mDragHandle.style.cursor = new StyleCursor(Cursorer.DefaultCursor(Cursorer.CursorType.MoveArrow));
            }

            if (_mHandleOnly)
            {
                // 句柄接管事件，避免整个项触发拖拽
                UnregisterCallback<PointerDownEvent>(OnPointerDown);
                _mDragHandle?.RegisterCallback<PointerDownEvent>(OnPointerDown);
            }
            else
            {
                // 保持默认：整项也可以拖拽
                RegisterCallback<PointerDownEvent>(OnPointerDown);
            }
        }
        
        // --- Cursorer 辅助类 (保持不变) ---
        public static class Cursorer
        {
            // ... (内部反射逻辑和 CursorType 枚举保持不变)
            public static Cursor DefaultCursor(CursorType cursorType)
            {
                var ret = (object)new Cursor();
                DefaultCursorId.SetValue(ret, (int)cursorType);
                return (Cursor)ret;
            }

            private static PropertyInfo s_DefaultCursorId;
            private static PropertyInfo DefaultCursorId
            {
                get
                {
                    if (s_DefaultCursorId != null) return s_DefaultCursorId;
                    s_DefaultCursorId = typeof(Cursor).GetProperty("defaultCursorId",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    return s_DefaultCursorId;
                }
            }

            public enum CursorType
            {
                Arrow = 0, Text = 1, ResizeVertical = 2, ResizeHorizontal = 3, Link = 4,
                SlideArrow = 5, ResizeUpRight = 6, ResizeUpLeft = 7, MoveArrow = 8,
                RotateArrow = 9, ScaleArrow = 10, ArrowPlus = 11, ArrowMinus = 12,
                Pan = 13, Orbit = 14, Zoom = 15, FPS = 16, CustomCursor = 17,
                SplitResizeUpDown = 18, SplitResizeLeftRight = 19
            }
        }
    }
}
