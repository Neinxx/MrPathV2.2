using UnityEditor;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【不动明王之掌 • 终极版】
    /// 专职处理用户输入的"护法"。其心不动，洞悉万般意图；其掌既出，招式分明。
    /// 采用"意图驱动"设计，将输入事件的"解析"与"执行"分离，清净优雅。
    /// </summary>
    public class PathInputHandler
    {
        private const int ControlId = 654321; // 固定控制ID，避免与其他控件冲突

        /// <summary>
        /// 处理所有输入事件，并根据解析出的意图，对PathCreator执行操作。
        /// </summary>
        public void HandleInputEvents(Event evt, PathCreator creator, float hoveredPathT, int hoveredPointIndex)
        {
            // 注册控件ID以接收输入事件
            if (evt.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(ControlId);
                return;
            }

            // Ctrl+左键的选择拦截：即使后续不产生命令，也要抢占控制并吞掉事件，防止误选其他物体
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                // 预占用热控，阻止场景选择逻辑
                GUIUtility.hotControl = ControlId;
                // 继续尝试解析命令（例如射线命中可添加点），若无命令也吞掉事件
                var preCommand = ResolveLeftClickCommand(evt, creator, hoveredPathT);
                if (preCommand != null)
                {
                    ExecuteCommandWithUndo(creator, preCommand);
                }
                evt.Use();
                return;
            }

            // 解析输入事件并生成操作命令
            var command = ResolveCommandFromEvent(evt, creator, hoveredPathT, hoveredPointIndex);

            // 执行命令并处理撤销逻辑
            if (command != null)
            {
                ExecuteCommandWithUndo(creator, command);
                evt.Use();
                return;
            }

            // 处理鼠标释放，重置热控制状态
            if (evt.type == EventType.MouseUp && GUIUtility.hotControl == ControlId)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        /// <summary>
        /// 从事件中解析并生成对应的路径操作命令
        /// </summary>
        private PathChangeCommand ResolveCommandFromEvent(Event evt, PathCreator creator, float hoveredPathT, int hoveredPointIndex)
        {
            return evt.type switch
            {
                EventType.MouseDown => ResolveMouseDownCommand(evt, creator, hoveredPathT, hoveredPointIndex),
                _ => null // 不处理其他类型事件
            };
        }

        /// <summary>
        /// 解析鼠标按下事件，生成对应的操作命令
        /// </summary>
        private PathChangeCommand ResolveMouseDownCommand(Event evt, PathCreator creator, float hoveredPathT, int hoveredPointIndex)
        {
            return evt.button switch
            {
                0 => ResolveLeftClickCommand(evt, creator, hoveredPathT),    // 左键操作
                1 => ResolveRightClickCommand(evt, creator, hoveredPointIndex), // 右键操作
                _ => null // 忽略中键等其他按键
            };
        }

        /// <summary>
        /// 解析左键点击命令
        /// </summary>
        private PathChangeCommand ResolveLeftClickCommand(Event evt, PathCreator creator, float hoveredPathT)
        {
            // Shift+左键：在路径上插入点
            if (evt.shift && IsValidHoverT(hoveredPathT))
            {
                int segmentIndex = Mathf.FloorToInt(hoveredPathT);
                Vector3 insertionPoint = creator.GetPointAt(hoveredPathT);
                return new InsertPointCommand(segmentIndex, insertionPoint);
            }

            // Ctrl+左键：在射线检测点添加新点
            if (evt.control && TryGetRaycastHitPoint(evt.mousePosition, out Vector3 hitPoint))
            {
                return new AddPointCommand(hitPoint);
            }

            return null;
        }

        /// <summary>
        /// 解析右键点击命令
        /// </summary>
        private PathChangeCommand ResolveRightClickCommand(Event evt, PathCreator creator, int hoveredPointIndex)
        {
            // Ctrl+Shift+右键：清空所有路径点
            if (evt.control && evt.shift)
            {
                return TryCreateClearCommand(creator);
            }

            // 右键点击：删除选中的点
            if (IsValidPointIndex(hoveredPointIndex) && CanDeletePoint(creator))
            {
                return new DeletePointCommand(hoveredPointIndex);
            }

            return null;
        }

        /// <summary>
        /// 执行命令并记录撤销操作
        /// </summary>
        private void ExecuteCommandWithUndo(PathCreator creator, PathChangeCommand command)
        {
            GUIUtility.hotControl = ControlId;
            Undo.RecordObject(creator, command.GetType().Name);
            creator.ExecuteCommand(command);
        }

        /// <summary>
        /// 尝试创建清空路径命令（带确认对话框）
        /// </summary>
        private PathChangeCommand TryCreateClearCommand(PathCreator creator)
        {
            if (creator.pathData.KnotCount == 0) return null;

            // return EditorUtility.DisplayDialog(
            //     "清空路径",
            //     "你确定要删除所有路径点吗？此操作可撤销。",
            //     "确定", "取消")
            //     ? new ClearPointsCommand()
            //     : null;
            return new ClearPointsCommand();
        }

        /// <summary>
        /// 射线检测获取世界坐标点
        /// </summary>
        private bool TryGetRaycastHitPoint(Vector2 screenPos, out Vector3 hitPoint)
        {
            if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(screenPos), out RaycastHit hit))
            {
                hitPoint = hit.point;
                return true;
            }

            hitPoint = Vector3.zero;
            return false;
        }

        // 辅助判断方法，提高可读性
        private bool IsValidHoverT(float hoverT) => hoverT >= 0;
        private bool IsValidPointIndex(int index) => index >= 0;
        private bool CanDeletePoint(PathCreator creator) => creator.pathData.KnotCount > 2;
    }
}