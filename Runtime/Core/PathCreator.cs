// PathCreator.cs
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【最终步：终极执行者】
    /// 
    /// 这是我们新架构的核心驱动者。它的职责被简化到了极致，从而变得异常强大和稳固。
    /// - 它持有“数据容器”(PathData)。
    /// - 它引用“配置文件”(PathProfile)来了解用户的意图。
    /// - 它通过“注册中心”(PathStrategyRegistry)来获取正确的“法则”(PathStrategy)。
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

        public int NumPoints => pathData.KnotCount;
        public int NumSegments => pathData.SegmentCount;

        /// <summary>
        /// 一个便捷的私有属性，用于获取当前应执行的“法则”。
        /// 这是连接用户选择和底层逻辑的桥梁。
        /// </summary>
        private PathStrategy CurrentStrategy
        {
            get
            {
                if (profile == null) return null;

                // 向“万法总纲”查询当前 Profile 中所选枚举对应的法则资产
                return PathStrategyRegistry.Instance?.GetStrategy(profile.curveType);
            }
        }

        /// <summary>
        /// 当Inspector中的值发生变化时调用。
        /// 我们在这里简单地触发一个事件，让关心变化的系统（如编辑器UI）知道需要刷新。
        /// </summary>
        private void OnValidate()
        {
            // OnValidate 触发一个通用的 BulkUpdateCommand (或null)，通知UI刷新
            PathModified?.Invoke(null);
        }

        #region Public API (供编辑器或其他脚本调用)
        /// <summary>
        /// 【已修正】获取曲线上某一点的世界坐标。
        /// 这是坐标转换的唯一出口。
        /// </summary>
        public Vector3 GetPointAt(float t)
        {
            var strategy = CurrentStrategy;
            if (strategy != null && NumPoints > 0)
            {
                // 1. 从策略层获取纯粹的、未经转换的“本地坐标”
                Vector3 localPoint = strategy.GetPointAt(t, pathData);

                // 2. 在这里，由 PathCreator 亲自完成到世界空间的转换
                return transform.TransformPoint(localPoint);
            }
            return transform.position;
        }

        /// <summary>
        /// 【已修正】获取曲线上某一点的本地坐标。
        /// 这个方法现在变得极其高效，因为它直接返回策略层的计算结果。
        /// </summary>
        public Vector3 GetPointAtLocal(float t)
        {
            var strategy = CurrentStrategy;
            if (strategy != null && NumPoints > 0)
            {
                // 直接返回策略层在本地空间计算的结果，没有任何多余转换
                return strategy.GetPointAt(t, pathData);
            }
            return Vector3.zero;
        }

        /// <summary>
        /// 统一的敕令执行入口。
        /// 所有对路径的修改，都必须通过此方法。
        /// </summary>
        public void ExecuteCommand(PathChangeCommand command)
        {
            if (command == null) return;

            // 1. 执行敕令中定义的操作
            command.Execute(this);

            // 2. 将此敕令作为“事件”，广播给所有关心此变化的系统
            PathModified?.Invoke(command);
        }

        public void NotifyProfileModified()
        {
            // 广播一个通用的“路径已修改”事件，内容为 null 表示是批量或未知类型的更新。
            // 所有监听者（如场景编辑器或预览系统）都应响应该事件并刷新自身状态。
            PathModified?.Invoke(null);
        }
        #endregion






    }
}