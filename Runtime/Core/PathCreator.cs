// PathCreator.cs
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【最终步：终极执行者】
    /// 
    /// 这是我们新架构的核心驱动者。它的职责被简化到了极致，从而变得异常强大和稳固。
    /// - 它持有"数据容器"(PathData)。
    /// - 它引用"配置文件"(PathProfile)来了解用户的意图。
    /// - 它通过"注册中心"(PathStrategyRegistry)来获取正确的"法则"(PathStrategy)。
    /// - 它将数据和法则结合，完成所有路径操作。
    /// 
    /// 注意，这个类中不再有任何复杂的切换逻辑。大道至简。
    /// </summary>
    [DisallowMultipleComponent]
    public class PathCreator : MonoBehaviour
    {
        public event System.Action<PathChangeCommand> PathModified;

        [Tooltip("决定路径一切外观与行为的剖面资产")]

        public PathProfile profile;

        [Tooltip("路径的核心数据容器")]
        [SerializeField]
        public PathData pathData = new();

        public int NumPoints => pathData?.KnotCount ?? 0;
        public int NumSegments => pathData?.SegmentCount ?? 0;

        /// <summary>
        /// 一个便捷的私有属性，用于获取当前应执行的"法则"。
        /// 这是连接用户选择和底层逻辑的桥梁。
        /// </summary>
        private PathStrategy CurrentStrategy
        {
            get
            {
                if (profile == null)
                {
                    this.LogWarning("Profile is null. Please assign a PathProfile.", "PathCreator");
                    return null;
                }

                var registry = PathStrategyRegistry.Instance;
                if (registry == null)
                {
                    this.LogError("PathStrategyRegistry instance is null. Please ensure the registry asset exists in Resources folder.", "PathCreator");
                    return null;
                }

                var strategy = registry.GetStrategy(profile.curveType);
                if (strategy == null)
                {
                    this.LogWarning($"No strategy found for curve type '{profile.curveType}'. Please configure the strategy in PathStrategyRegistry.", "PathCreator");
                }

                return strategy;
            }
        }
        
        public PathStrategy GetCurrentStratgy()
        {
            return CurrentStrategy;
        }

        /// <summary>
        /// 验证组件状态是否有效
        /// </summary>
        public bool IsValidState()
        {
            if (pathData == null)
            {
                this.LogError("PathData is null. This should not happen.", "PathCreator");
                return false;
            }

            if (profile == null)
            {
                this.LogWarning("PathProfile is not assigned.", "PathCreator");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 当Inspector中的值发生变化时调用。
        /// 我们在这里简单地触发一个事件，让关心变化的系统（如编辑器UI）知道需要刷新。
        /// </summary>
        private void OnValidate()
        {
            // 确保pathData不为null
            if (pathData == null)
            {
                pathData = new PathData();
            }

            // 保证中心点位于第一个节点
            EnsurePivotAtFirstPoint();

            // OnValidate 触发一个通用的 BulkUpdateCommand (或null)，通知UI刷新
            PathModified?.Invoke(null);
        }

        private void EnsurePivotAtFirstPoint()
        {
            if (pathData == null || pathData.KnotCount == 0) return;

            // 当前第一个节点的本地坐标
            Vector3 firstLocal = pathData.GetPosition(0);
            if (firstLocal != Vector3.zero)
            {
                // 需要将 transform 移动到世界空间的第一个节点位置
                Vector3 worldFirst = transform.TransformPoint(firstLocal);

                Vector3 deltaWorld = worldFirst - transform.position;

                // 将 transform.position 移动到 worldFirst
                transform.position = worldFirst;

                // 将所有路径点整体平移相反方向，使得第一个点本地坐标为零
                Vector3 deltaLocal = -firstLocal;
                pathData.ShiftAllPositions(deltaLocal);
            }
        }

        private void Awake()
        {
            // 确保pathData在运行时不为null
            if (pathData == null)
            {
                pathData = new PathData();
                this.LogWarning("PathData was null, created new instance.", "PathCreator");
            }
        }

        #region Public API (供编辑器或其他脚本调用)
        /// <summary>
        /// 【已修正】获取曲线上某一点的世界坐标。
        /// 这是坐标转换的唯一出口。
        /// </summary>
        public Vector3 GetPointAt(float t)
        {
            // 参数验证
            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                this.LogWarning($"Invalid parameter t={t} in GetPointAt. Using t=0.", "PathCreator");
                t = 0f;
            }

            // 将t限制在有效范围内
            t = Mathf.Clamp(t, 0f, NumSegments);

            if (!IsValidState())
            {
                return transform.position;
            }

            var strategy = CurrentStrategy;
            if (strategy != null && NumPoints > 0)
            {
                return ErrorHandler.SafeExecute(() =>
                {
                    // 1. 从策略层获取纯粹的、未经转换的"本地坐标"
                    Vector3 localPoint = strategy.GetPointAt(t, pathData);

                    // 验证返回的点是否有效
                    if (float.IsNaN(localPoint.x) || float.IsNaN(localPoint.y) || float.IsNaN(localPoint.z) ||
                        float.IsInfinity(localPoint.x) || float.IsInfinity(localPoint.y) || float.IsInfinity(localPoint.z))
                    {
                        this.LogWarning($"Strategy returned invalid point {localPoint} for t={t}. Using fallback.", "PathCreator");
                        return transform.position;
                    }

                    // 2. 在这里，由 PathCreator 亲自完成到世界空间的转换
                    return transform.TransformPoint(localPoint);
                }, transform.position, "PathCreator.GetPointAt", this);
            }
            return transform.position;
        }

        /// <summary>
        /// 【已修正】获取曲线上某一点的本地坐标。
        /// 这个方法现在变得极其高效，因为它直接返回策略层的计算结果。
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
            if (strategy != null && NumPoints > 0)
            {
                return ErrorHandler.SafeExecute(() =>
                {
                    // 直接返回策略层在本地空间计算的结果，没有任何多余转换
                    Vector3 localPoint = strategy.GetPointAt(t, pathData);

                    // 验证返回的点是否有效
                    if (float.IsNaN(localPoint.x) || float.IsNaN(localPoint.y) || float.IsNaN(localPoint.z) ||
                        float.IsInfinity(localPoint.x) || float.IsInfinity(localPoint.y) || float.IsInfinity(localPoint.z))
                    {
                        this.LogWarning($"Strategy returned invalid local point {localPoint} for t={t}. Using fallback.", "PathCreator");
                        return Vector3.zero;
                    }

                    return localPoint;
                }, Vector3.zero, "PathCreator.GetPointAtLocal", this);
            }
            return Vector3.zero;
        }

        /// <summary>
        /// 统一的敕令执行入口。
        /// 所有对路径的修改，都必须通过此方法。
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

                // 2. 将此敕令作为"事件"，广播给所有关心此变化的系统
                PathModified?.Invoke(command);
            }, "PathCreator.ExecuteCommand", this);
        }

        public void NotifyProfileModified()
        {
            ErrorHandler.SafeExecute(() =>
            {
                // 广播一个通用的"路径已修改"事件，内容为 null 表示是批量或未知类型的更新。
                // 所有监听者（如场景编辑器或预览系统）都应响应该事件并刷新自身状态。
                PathModified?.Invoke(null);
            }, "PathCreator.NotifyProfileModified", this);
        }
        #endregion
    }
}