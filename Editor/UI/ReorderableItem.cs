using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace MrPathV2.Editor.UI
{
    public class ReorderableItem : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<ReorderableItem, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits { }

        public ReorderableItem()
        {
            // 设置可拖拽的光标
            style.cursor = new StyleCursor(Cursorer.DefaultCursor(Cursorer.CursorType.Arrow));
            
         // 3. 注册鼠标按下事件来开始拖动
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
          if (evt.button != 0 || !(parent is ReorderableContainer))
            {
                return;
            }
            
            // 准备拖放
            DragAndDrop.PrepareStartDrag();
            // 存储对此项的引用，以便容器可以识别它
            DragAndDrop.SetGenericData("ReorderableItem", this);
            // 开始拖动
            DragAndDrop.StartDrag("Reordering");

            evt.StopPropagation();
        }

        public static class Cursorer
        {

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
                Arrow = 0,
                Text = 1,
                ResizeVertical = 2,
                ResizeHorizontal = 3,
                Link = 4,
                SlideArrow = 5,
                ResizeUpRight = 6,
                ResizeUpLeft = 7,
                MoveArrow = 8,
                RotateArrow = 9,
                ScaleArrow = 10,
                ArrowPlus = 11,
                ArrowMinus = 12,
                Pan = 13,
                Orbit = 14,
                Zoom = 15,
                FPS = 16,
                CustomCursor = 17,
                SplitResizeUpDown = 18,
                SplitResizeLeftRight = 19
            }
        }
    }
}