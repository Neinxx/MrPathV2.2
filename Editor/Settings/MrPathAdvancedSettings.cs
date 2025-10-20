

using __temp.MrPathV2._2.Runtime.Core;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Settings
{
    /// <summary>
    /// 将工厂注入、策略覆盖等不常用但重要地设置隔离存放。
    /// </summary>
    public class MrPathAdvancedSettings : ScriptableObject
    {
        [Header("策略设置 (Strategy Settings)")]
        [Tooltip("默认路径策略，用于新创建的路径")]
        public PathStrategy defaultStrategy;

        [Tooltip("所有可用的路径策略列表")]
        public PathStrategy[] availableStrategies;
    }
}