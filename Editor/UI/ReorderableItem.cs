using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace MrPathV2.Editor.UI
{
    public class ReorderableItem : VisualElement
    {
        // --- UXML/工厂定义 ---
        public new class UxmlFactory : UxmlFactory<ReorderableItem, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

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
            if (evt.button != 0 || !(parent is ReorderableContainer))
            {
                return;
            }
            
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("ReorderableItem", this);
            DragAndDrop.StartDrag("Reordering");

            evt.StopPropagation();
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

            private static PropertyInfo _defaultCursorId;
            private static PropertyInfo DefaultCursorId
            {
                get
                {
                    if (_defaultCursorId != null) return _defaultCursorId;
                    _defaultCursorId = typeof(Cursor).GetProperty("defaultCursorId",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    return _defaultCursorId;
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