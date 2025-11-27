using System;
using MrPathV2.Runtime.Components;
using MrPathV2.Runtime.Settings;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     - 它持有"数据容器"(PathData)。
    ///     - 它引用"配置文件"(PathProfile)来了解用户的意图。
    ///     - 它通过"注册中心"(PathStrategyRegistry)来获取正确的"法则"(PathStrategy)。
    ///     - 它将数据和法则结合，完成所有路径操作。
    /// </summary>
    [DisallowMultipleComponent]
    public class PathCreator : MonoBehaviour
    {

        [Tooltip("决定路径一切外观与行为的剖面资产")]
        [RequiredField(ErrorMessage = "请分配一个PathProfile以定义路径属性")]
        public PathProfile profile;

        [Tooltip("路径的核心数据容器")]
        [SerializeField]
        public PathData pathData = new PathData();

        // --- 用于跟踪已订阅的 Profile，并在其修改时回调 ---
        [NonSerialized]
        private PathProfile _subscribedProfile;

        public int NumPoints => pathData?.KnotCount ?? 0;
        public int NumSegments => pathData?.SegmentCount ?? 0;

        /// <summary>
        ///     一个便捷的私有属性，用于获取当前应执行的"法则"。
        ///     这是连接用户选择和底层逻辑的桥梁。
        /// </summary>
        private PathStrategy CurrentStrategy
        {
            get
            {
                if (!profile)
                {
                    this.LogWarning("Profile is null. Please assign a PathProfile.", "PathCreator");
                    return null;
                }

                var registry = PathStrategyRegistry.Instance;
                if (!registry)
                {
                    this.LogError("PathStrategyRegistry instance is null. Please ensure the registry asset exists in Resources folder.", "PathCreator");
                    return null;
                }

                var strategy = registry.GetStrategy(profile.curveType);
                if (!strategy)
                {
                    this.LogWarning($"No strategy found for curve type '{profile.curveType}'. Please configure the strategy in PathStrategyRegistry.", "PathCreator");
                }

                return strategy;
            }
        }

        private void Awake()
        {

            if (pathData != null) return;
            pathData = new PathData();
            this.LogWarning("PathData was null, created new instance.", "PathCreator");
        }

        /// <summary>
        ///     当Inspector中的值发生变化时调用。
        ///     让关心变化的系统（如编辑器UI）知道需要刷新。
        /// </summary>
        private void OnValidate()
        {

            pathData ??= new PathData();
            EnsurePivotAtFirstPoint();
            // 仅触发外观变化事件，避免重采样
            AppearanceChanged?.Invoke();
        }
        public event Action<PathChangeCommand> PathModified;
        public event Action CurveDefinitionChanged;
        public event Action AppearanceChanged;

        /// <summary>
        ///     验证组件状态是否有效
        /// </summary>
        public bool IsValidState()
        {
            if (pathData != null) return profile;
            this.LogError("PathData is null. This should not happen.", "PathCreator");
            return false;

            // this.LogWarning("PathProfile is not assigned.", "PathCreator");
        }

        private void EnsurePivotAtFirstPoint()
        {
            if (pathData == null || pathData.KnotCount == 0) return;

            var firstLocal = pathData.GetPosition(0);
            if (firstLocal == Vector3.zero) return;

            // 将物体移动到第一个节点的世界位置
            transform.position += transform.TransformVector(firstLocal);

            // 将所有路径点反向偏移，使第一个点回到原点
            pathData.ShiftAllPositions(-firstLocal);
        }

        #region Public API (供编辑器或其他脚本调用)

        /// <summary>
        ///     获取曲线上某一点的世界坐标。
        ///     这是坐标转换的唯一出口。
        /// </summary>
        public Vector3 GetPointAt(float t)
        {
            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                this.LogWarning($"Invalid parameter t={t} in GetPointAt. Using t=0.", "PathCreator");
                t = 0f;
            }

            t = Mathf.Clamp(t, 0f, NumSegments);

            if (!IsValidState())
            {
                return transform.position;
            }

            var strategy = CurrentStrategy;
            if (strategy && NumPoints > 0)
            {
                return ErrorHandler.SafeExecute(() =>
                {
                    // 1. 从策略层获取纯粹的、未经转换的"本地坐标"
                    var localPoint = strategy.GetPointAt(t, pathData);

                    // 验证返回的点是否有效
                    if (!float.IsNaN(localPoint.x) && !float.IsNaN(localPoint.y) && !float.IsNaN(localPoint.z) &&
                        !float.IsInfinity(localPoint.x) && !float.IsInfinity(localPoint.y) && !float.IsInfinity(localPoint.z)) return transform.TransformPoint(localPoint);
                    this.LogWarning($"Strategy returned invalid point {localPoint} for t={t}. Using fallback.", "PathCreator");
                    return transform.position;

                    // 2. 由 PathCreator 亲自完成到世界空间的转换
                }, transform.position, "PathCreator.GetPointAt", this);
            }
            return transform.position;
        }

        /// <summary>
        ///     获取曲线上某一点的本地坐标。
        ///     这个方法现在变得极其高效，因为它直接返回策略层的计算结果。
        /// </summary>
        public Vector3 GetPointAtLocal(float t)
        {
            // 参数验证
            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                this.LogWarning($"Invalid parameter t={t} in GetPointAtLocal. Using t=0.", "PathCreator");
                t = 0f;
            }

            // 将t限制在有效范围内
            t = Mathf.Clamp(t, 0f, NumSegments);

            if (!IsValidState())
            {
                return Vector3.zero;
            }

            var strategy = CurrentStrategy;
            if (strategy && NumPoints > 0)
            {
                return ErrorHandler.SafeExecute(() =>
                {
                    // 直接返回策略层在本地空间计算的结果，没有任何多余转换
                    var localPoint = strategy.GetPointAt(t, pathData);

                    // 验证返回的点是否有效
                    if (!float.IsNaN(localPoint.x) && !float.IsNaN(localPoint.y) && !float.IsNaN(localPoint.z) &&
                        !float.IsInfinity(localPoint.x) && !float.IsInfinity(localPoint.y) && !float.IsInfinity(localPoint.z)) return localPoint;
                    this.LogWarning($"Strategy returned invalid local point {localPoint} for t={t}. Using fallback.", "PathCreator");
                    return Vector3.zero;

                }, Vector3.zero, "PathCreator.GetPointAtLocal", this);
            }
            return Vector3.zero;
        }

        /// <summary>
        ///     统一的敕令执行入口。
        ///     所有对路径的修改，都必须通过此方法。
        /// </summary>
        public void ExecuteCommand(PathChangeCommand command)
        {
            if (command == null)
            {
                this.LogWarning("Attempted to execute null command.", "PathCreator");
                return;
            }

            if (!IsValidState())
            {
                this.LogError($"Cannot execute command '{command.GetType().Name}' - invalid state.", "PathCreator");
                return;
            }

            ErrorHandler.SafeExecute(() =>
            {
                // 1. 执行敕令中定义的操作
                command.Execute(this);

                // 1.5 确保中心点始终位于第一个节点（在顶点数量或位置变化后自动调整）
                EnsurePivotAtFirstPoint();

                // 2. 将此敕令作为"事件"，广播给所有关心此变化的系统
                PathModified?.Invoke(command);

                switch (command)
                {
                    // 根据命令类型触发更具体的事件
                    case AddPointCommand:
                    case MovePointCommand:
                    case InsertPointCommand:
                    case DeletePointCommand:
                    case ClearPointsCommand:
                    // batch命令，保守地认为曲线定义可能变化
                    case BatchCommand:
                        CurveDefinitionChanged?.Invoke();
                        break;
                    default:
                        AppearanceChanged?.Invoke();
                        break;
                }
            }, "PathCreator.ExecuteCommand", this);
        }

        public void NotifyProfileModified()
        {
            ErrorHandler.SafeExecute(() =>
            {
                // 根据修改内容假设为外观变化
                AppearanceChanged?.Invoke();
                // 同时保持旧事件
                PathModified?.Invoke(null);
            }, "PathCreator.NotifyProfileModified", this);
        }
        /// <summary>
        ///     获取路径的总长度（世界坐标系）
        ///     直接利用PathSampler中已计算的totalPathDistance，避免重复计算
        /// </summary>
        /// <returns>路径的总长度（米）</returns>
        public float GetPathLength()
        {
            if (!IsValidState() || NumPoints < 2)
            {
                return 0f;
            }
            // 纯数学曲线：采用细采样估算整条路径弧长，不依赖配置参数
            return EstimateTotalPathLength();
        }

        /// <summary>
        ///     细采样估算整条路径弧长（本地空间），后换算到世界尺度
        /// </summary>
        private float EstimateTotalPathLength()
        {
            if (NumPoints < 2) return 0f;

            var prev = GetPointAtLocal(0);
            var totalPathDistance = 0f;
            var step = Mathf.Max(1f / (NumSegments * 40f), 0.005f);
            for (var t = step; t <= NumSegments; t += step)
            {
                var p = GetPointAtLocal(t);
                var d = Vector3.Distance(prev, p);
                if (d > 0.0001f)
                {
                    totalPathDistance += d;
                    prev = p;
                }
            }

            // 转换到世界坐标系
            var worldScale = transform.lossyScale;
            var averageScale = (worldScale.x + worldScale.z) / 2f;

            return totalPathDistance * averageScale;
        }

        #endregion
    }
}
