// PathCommands.cs
using System.Collections.Generic;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【第一步：铸造敕令】
    /// 定义了所有路径修改操作的“敕令”基类和具体实现。
    /// 每个敕令都是一个包含了所有必要信息，并懂得如何执行自己的对象。
    /// </summary>
    public abstract class PathChangeCommand
    {
        public abstract void Execute(PathCreator creator);
    }

    public class AddPointCommand : PathChangeCommand
    {
        public readonly Vector3 Position;
        private const float MinDistanceSq = 0.01f; // 最小距离阈值的平方 (0.1 * 0.1)

        public AddPointCommand(Vector3 position) { Position = position; }

        public override void Execute(PathCreator creator)
        {
            // 【新增】在添加前进行距离检查
            if (creator.pathData.KnotCount > 0)
            {
                Vector3 lastPoint = creator.transform.TransformPoint(creator.pathData.GetPosition(creator.pathData.KnotCount - 1));
                if ((Position - lastPoint).sqrMagnitude < MinDistanceSq)
                {
                    return; // 距离太近，取消添加
                }
            }
            creator.pathData.AddKnot(creator.transform.InverseTransformPoint(Position), Vector3.zero, Vector3.zero);
        }
    }

    public class InsertPointCommand : PathChangeCommand
    {
        public readonly int SegmentIndex;
        public readonly Vector3 Position;
        public InsertPointCommand(int segmentIndex, Vector3 position) { SegmentIndex = segmentIndex; Position = position; }
        public override void Execute(PathCreator creator)
        {
            // 具体的插入逻辑应由Strategy来执行，以处理不同曲线的切线计算
            var strategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
            strategy?.InsertSegment(SegmentIndex, Position, creator.pathData, creator.transform);
        }
    }

    public class DeletePointCommand : PathChangeCommand
    {
        public readonly int PointFlatIndex;
        public DeletePointCommand(int pointFlatIndex) { PointFlatIndex = pointFlatIndex; }
        public override void Execute(PathCreator creator)
        {
            var strategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
            strategy?.DeleteSegment(PointFlatIndex, creator.pathData);
        }
    }

    public class MovePointCommand : PathChangeCommand
    {
        public readonly int PointFlatIndex;
        public readonly Vector3 NewPosition; // 纯粹的最终位置数据

        public MovePointCommand(int pointFlatIndex, Vector3 newPosition)
        {
            PointFlatIndex = pointFlatIndex;
            NewPosition = newPosition;
        }

        public override void Execute(PathCreator creator)
        {
            // 命令仅负责应用最终数据，不参与外部服务逻辑
            var strategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
            strategy?.MovePoint(PointFlatIndex, NewPosition, creator.pathData, creator.transform);
        }
    }

    public class ClearPointsCommand : PathChangeCommand
    {
        public override void Execute(PathCreator creator)
        {
            // 如果点数大于2，则清空所有点并创建新的两点路径
            if (creator.pathData.KnotCount > 2)
            {
                // 保存第一个点的位置
                Vector3 firstPointPosition = creator.pathData.GetPosition(0);

                // 清空所有点
                creator.pathData.Clear();

                // 添加两个默认点：第一个点保持原位置，第二个点在第一个点前方5米
                creator.pathData.AddKnot(firstPointPosition, Vector3.zero, Vector3.zero);
                creator.pathData.AddKnot(firstPointPosition + Vector3.forward * 5f, Vector3.zero, Vector3.zero);
            }
            // 如果只有两个或更少的点，则不执行任何操作，保持最少两个点
        }
    }

    /// <summary>
    /// 【高阶法门：复合敕令】
    /// 一个可以包含并依次执行一系列其他敕令的“敕令卷轴”。
    /// 这允许我们将多个操作打包成一个单一的、原子性的事务。
    /// </summary>
    public class BatchCommand : PathChangeCommand
    {
        private readonly IReadOnlyList<PathChangeCommand> _commands;

        public BatchCommand(IReadOnlyList<PathChangeCommand> commands)
        {
            _commands = commands;
        }

        public override void Execute(PathCreator creator)
        {
            // 依次执行卷轴中记载的所有敕令
            foreach (var command in _commands)
            {
                // 注意：这里我们直接调用 command.Execute，而不是 creator.ExecuteCommand
                // 因为我们希望整个“卷轴”只触发一次最终的 PathModified 事件。
                command.Execute(creator);
            }
        }
    }
}