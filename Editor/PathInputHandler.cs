// 请用此完整代码替换你的 PathInputHandler.cs

using UnityEditor;
using UnityEngine;

/// <summary>
/// 【不动明王之掌 • 终极版】
/// 专职处理用户输入的“护法”。其心不动，洞悉万般意图；其掌既出，招式分明。
/// 采用“意图驱动”设计，将输入事件的“解析”与“执行”分离，清净优雅。
/// </summary>
public class PathInputHandler
{
    // 定义用户的输入意图
    private enum ActionType { None, AddPoint, InsertPoint, DeletePoint }
    private struct InputResult
    {
        public readonly ActionType type;
        public readonly Vector3 position;
        public readonly int index;

        private InputResult(ActionType type, Vector3 position = default, int index = -1)
        {
            this.type = type;
            this.position = position;
            this.index = index;
        }

        public static InputResult None() => new InputResult(ActionType.None);
        public static InputResult Add(Vector3 pos) => new InputResult(ActionType.AddPoint, pos);
        public static InputResult Insert(int segIdx, Vector3 pos) => new InputResult(ActionType.InsertPoint, pos, segIdx);
        public static InputResult Delete(int pointIdx) => new InputResult(ActionType.DeletePoint, default, pointIdx);
    }

    /// <summary>
    /// 处理所有输入事件，并根据解析出的意图，对PathCreator执行操作。
    /// </summary>
    public void HandleInputEvents(Event e, PathCreator creator, int hoveredPointIndex, float hoveredPathT)
    {
        // 确保工具能捕获到场景事件
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlID);
        }

        InputResult result = InputResult.None();

        // 只在MouseDown事件中“辨意”
        if (e.type == EventType.MouseDown)
        {
            result = ParseMouseDown(e, creator, hoveredPointIndex, hoveredPathT);
        }

        // 统一“出招”，执行辨识出的意图
        if (result.type != ActionType.None)
        {
            GUIUtility.hotControl = controlID; // 锁定控制权，防止其他工具干扰
            switch (result.type)
            {
                case ActionType.AddPoint:
                    creator.AddSegment(result.position);
                    break;
                case ActionType.InsertPoint:
                    creator.InsertSegment(result.index, result.position);
                    break;
                case ActionType.DeletePoint:
                    creator.DeleteSegment(result.index);
                    break;
            }
            e.Use(); // 吞掉事件，表示我们已处理
        }

        // 释放控制权
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
        {
            GUIUtility.hotControl = 0;
            e.Use();
        }
    }

    /// <summary>
    /// 解析MouseDown事件，返回一个明确的“意图”。
    /// 此方法纯净无副作用，只负责“思考”，不负责“动手”。
    /// </summary>
    private InputResult ParseMouseDown(Event e, PathCreator creator, int hoveredPointIndex, float hoveredPathT)
    {
        // 左键操作
        if (e.button == 0)
        {
            // Shift优先：插入点
            if (e.shift && hoveredPathT > -1)
            {
                int segmentIndex = Mathf.FloorToInt(hoveredPathT);
                // 【言行合一】直接在精准的t值处获取世界坐标
                Vector3 pointToInsert = creator.GetPointAt(hoveredPathT);
                return InputResult.Insert(segmentIndex, pointToInsert);
            }
            // Ctrl其次：在末尾添加点
            if (e.control)
            {
                if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hit))
                {
                    return InputResult.Add(hit.point);
                }
            }
        }
        // 右键操作：删除点
        else if (e.button == 1 && hoveredPointIndex != -1)
        {
            return InputResult.Delete(hoveredPointIndex);
        }

        return InputResult.None();
    }
}