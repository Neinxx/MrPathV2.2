// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathAdvancedSettings.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 将工厂注入、策略覆盖等不常用但重要的设置隔离存放。
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