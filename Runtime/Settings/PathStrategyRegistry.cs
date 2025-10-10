using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // 保留以兼容可能的 Task 用法（若不需要可后续移除）

namespace MrPathV2
{
    /// <summary>
    /// 路径策略注册中心：负责管理CurveType与PathStrategy的映射关系
    /// 采用单例模式确保全局唯一访问点，支持数据驱动配置
    /// </summary>
    [CreateAssetMenu(fileName = "PathStrategyRegistry", menuName = "MrPath/Path Strategy Registry", order = 100)]
    public class PathStrategyRegistry : ScriptableObject
    {
        private static PathStrategyRegistry _instance;

        /// <summary>
        /// 全局唯一实例
        /// </summary>
        public static PathStrategyRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    InitializeInstance();
                }
                return _instance;
            }
        }

        [Serializable]
        public struct StrategyEntry : IEquatable<StrategyEntry>
        {
            [Tooltip("曲线类型")]
            public CurveType type;

            [Tooltip("对应的路径策略实例")]
            public PathStrategy strategy;

            public bool Equals(StrategyEntry other)
            {
                return type == other.type && Equals(strategy, other.strategy);
            }

            public override bool Equals(object obj)
            {
                return obj is StrategyEntry other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine((int)type, strategy);
            }
        }

        [Header("策略映射配置")]
        [Tooltip("曲线类型与策略的映射列表")]
        [SerializeField] private List<StrategyEntry> _strategyEntries = new List<StrategyEntry>();

        // 缓存策略映射，提高查询性能
        private Dictionary<CurveType, PathStrategy> _strategyCache;

        /// <summary>
        /// 初始化实例
        /// </summary>
        private static void InitializeInstance()
        {
            // 使用 Resources 同步加载作为唯一初始化路径。
            _instance = Resources.Load<PathStrategyRegistry>("PathStrategyRegistry");

            // 编辑器下尝试查找现有资源
#if UNITY_EDITOR
            if (_instance == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(PathStrategyRegistry)}");
                if (guids?.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(path);
                }
            }
#endif

            // 未找到资产则保持为 null，由调用方处理提示与阻止。
            if (_instance != null)
            {
                // 初始化缓存
                _instance.InitializeCache();
            }
        }

        // 采用 Resources 同步加载作为唯一初始化路径。

        // 默认映射初始化逻辑已移除；未配置时应显式提示并阻止。

        /// <summary>
        /// 创建默认策略实例并初始化样式
        /// </summary>
        private T CreateDefaultStrategy<T>() where T : PathStrategy
        {
            var strategy = ScriptableObject.CreateInstance<T>();
            EnsureDefaultStyle(strategy);
            return strategy;
        }

        /// <summary>
        /// 初始化策略缓存
        /// </summary>
        private void InitializeCache()
        {
            _strategyCache = new Dictionary<CurveType, PathStrategy>();

            foreach (var entry in _strategyEntries)
            {
                if (entry.strategy != null && !_strategyCache.ContainsKey(entry.type))
                {
                    EnsureDefaultStyle(entry.strategy);
                    _strategyCache[entry.type] = entry.strategy;
                }
            }
        }

        /// <summary>
        /// 获取指定曲线类型的策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <returns>对应的路径策略，若未找到则返回null</returns>
        public PathStrategy GetStrategy(CurveType type)
        {
            if (_strategyCache.TryGetValue(type, out var strategy))
            {
                return strategy;
            }

            // 未配置时返回 null，由上层 UI 与调用方负责提示与阻止
            return null;
        }

        /// <summary>
        /// 确保策略拥有默认样式
        /// </summary>
        private void EnsureDefaultStyle(PathStrategy strategy)
        {
            if (strategy == null) return;

            strategy.drawingStyle ??= new PathDrawingStyle();

            strategy.drawingStyle.knotStyle ??= new HandleStyle
            {
                fillColor = Color.white,
                borderColor = Color.black,
                size = 0.12f
            };

            strategy.drawingStyle.tangentStyle ??= new HandleStyle
            {
                fillColor = new Color(1f, 0.6f, 0f, 1f),
                borderColor = new Color(0.8f, 0.4f, 0f, 1f),
                size = 0.08f
            };

            strategy.drawingStyle.hoverStyle ??= new HandleStyle
            {
                fillColor = Color.yellow,
                borderColor = Color.black,
                size = 0.12f
            };

            strategy.drawingStyle.insertionPreviewStyle ??= new HandleStyle
            {
                fillColor = Color.cyan,
                borderColor = new Color(0f, 0.6f, 1f, 1f),
                size = 0.10f
            };
        }

        private void OnEnable()
        {
            // 防止资源重新加载时实例丢失
            if (_instance == null)
            {
                _instance = this;
            }

            InitializeCache();
        }

        private void OnValidate()
        {
            // 编辑器下数据变更时更新缓存
            InitializeCache();
        }
    }
}