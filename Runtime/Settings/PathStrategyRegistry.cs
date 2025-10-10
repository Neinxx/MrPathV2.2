// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Runtime/Settings/PathStrategyRegistry.cs
using UnityEngine;
using System;
using System.Collections.Generic;

namespace MrPathV2
{
    /// <summary>
    /// 路径策略注册中心：负责管理CurveType与PathStrategy的映射关系。
    /// 采用单例模式，通过 Resources 文件夹加载，确保全局唯一访问点。
    /// </summary>
    [CreateAssetMenu(fileName = "PathStrategyRegistry", menuName = "MrPath/Path Strategy Registry", order = 100)]
    public class PathStrategyRegistry : ScriptableObject
    {
        private static PathStrategyRegistry _instance;

        public static PathStrategyRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    // 运行时，只通过 Resources.Load 加载。
                    // 编辑器下的创建和维护由 MrPathAdvancedSettingsEditor 负责。
                    _instance = Resources.Load<PathStrategyRegistry>("PathStrategyRegistry");
                    if (_instance != null)
                    {
                        _instance.InitializeCache();
                    }
                }
                return _instance;
            }
        }

        [Serializable]
        public struct StrategyEntry : IEquatable<StrategyEntry>
        {
            public CurveType type;
            public PathStrategy strategy;

            public bool Equals(StrategyEntry other) => type == other.type && Equals(strategy, other.strategy);
            public override int GetHashCode() => HashCode.Combine((int)type, strategy);
            public override bool Equals(object obj) => obj is StrategyEntry other && Equals(other);
        }

        [SerializeField] private List<StrategyEntry> _strategyEntries = new List<StrategyEntry>();

        // 缓存策略映射，提高查询性能
        private Dictionary<CurveType, PathStrategy> _strategyCache;

        private void InitializeCache()
        {
            _strategyCache = new Dictionary<CurveType, PathStrategy>();
            foreach (var entry in _strategyEntries)
            {
                if (entry.strategy != null && !_strategyCache.ContainsKey(entry.type))
                {
                    _strategyCache[entry.type] = entry.strategy;
                }
            }
        }

        /// <summary>
        /// 获取指定曲线类型的策略。
        /// </summary>
        public PathStrategy GetStrategy(CurveType type)
        {
            // 在编辑器模式下，如果缓存为空，尝试重新初始化一次
#if UNITY_EDITOR
            if (_strategyCache == null) InitializeCache();
#endif

            _strategyCache.TryGetValue(type, out var strategy);
            return strategy;
        }

        private void OnEnable()
        {
            // OnEnable 时总是重新构建缓存，以响应编辑器中的修改
            InitializeCache();
        }

        private void OnValidate()
        {
            // 在 Inspector 中修改数据时，也更新缓存
            InitializeCache();
        }
    }
}