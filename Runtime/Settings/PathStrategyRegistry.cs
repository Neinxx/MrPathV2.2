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
        private static bool _initializationAttempted = false;

        /// <summary>
        /// 全局唯一实例
        /// </summary>
        public static PathStrategyRegistry Instance
        {
            get
            {
                if (_instance == null && !_initializationAttempted)
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

            /// <summary>
            /// 验证策略条目是否有效
            /// </summary>
            public bool IsValid => strategy != null;
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
            _initializationAttempted = true;

            ErrorHandler.SafeExecute(() =>
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
                        if (!string.IsNullOrEmpty(path))
                        {
                            _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(path);
                        }
                    }
                }
#endif

                // 未找到资产则保持为 null，由调用方处理提示与阻止。
                if (_instance != null)
                {
                    // 初始化缓存
                    _instance.InitializeCache();
                }
                else
                {
                    ErrorHandler.LogError("Failed to load PathStrategyRegistry asset. Please ensure it exists in a Resources folder or create one using the menu: Assets > Create > MrPath > Path Strategy Registry", "PathStrategyRegistry");
                }
            }, "PathStrategyRegistry.InitializeInstance");
        }

        /// <summary>
        /// 创建默认策略实例并初始化样式
        /// </summary>
        private T CreateDefaultStrategy<T>() where T : PathStrategy
        {
            return ErrorHandler.SafeExecute(() =>
            {
                var strategy = ScriptableObject.CreateInstance<T>();
                if (strategy != null)
                {
                    EnsureDefaultStyle(strategy);
                }
                return strategy;
            }, null, "PathStrategyRegistry.CreateDefaultStrategy");
        }

        /// <summary>
        /// 初始化策略缓存
        /// </summary>
        private void InitializeCache()
        {
            ErrorHandler.SafeExecute(() =>
            {
                _strategyCache = new Dictionary<CurveType, PathStrategy>();

                if (_strategyEntries == null)
                {
                    ErrorHandler.LogWarning("Strategy entries list is null, initializing empty list.", "PathStrategyRegistry");
                    _strategyEntries = new List<StrategyEntry>();
                    return;
                }

                var duplicateTypes = new HashSet<CurveType>();
                var processedTypes = new HashSet<CurveType>();

                foreach (var entry in _strategyEntries)
                {
                    if (!entry.IsValid)
                    {
                        ErrorHandler.LogWarning($"Invalid strategy entry for curve type '{entry.type}' - strategy is null.", "PathStrategyRegistry");
                        continue;
                    }

                    if (processedTypes.Contains(entry.type))
                    {
                        duplicateTypes.Add(entry.type);
                        ErrorHandler.LogWarning($"Duplicate strategy entry found for curve type '{entry.type}'. Only the first valid entry will be used.", "PathStrategyRegistry");
                        continue;
                    }

                    ErrorHandler.SafeExecute(() =>
                    {
                        EnsureDefaultStyle(entry.strategy);
                        _strategyCache[entry.type] = entry.strategy;
                        processedTypes.Add(entry.type);
                    }, $"PathStrategyRegistry.InitializeCache.ProcessEntry({entry.type})");
                }

                // 报告缓存初始化结果
                ErrorHandler.LogInfo($"Cache initialized with {_strategyCache.Count} strategies.", "PathStrategyRegistry");
                
                // 验证配置完整性
                ValidateConfiguration();
            }, "PathStrategyRegistry.InitializeCache");
        }

        /// <summary>
        /// 获取指定曲线类型的策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <returns>对应的路径策略，若未找到则返回null</returns>
        public PathStrategy GetStrategy(CurveType type)
        {
            return ErrorHandler.SafeExecute(() =>
            {
                if (_strategyCache == null)
                {
                    ErrorHandler.LogWarning("Strategy cache is null, attempting to reinitialize.", "PathStrategyRegistry");
                    InitializeCache();
                    
                    if (_strategyCache == null)
                    {
                        ErrorHandler.LogError("Failed to initialize strategy cache.", "PathStrategyRegistry");
                        return null;
                    }
                }

                if (_strategyCache.TryGetValue(type, out var strategy))
                {
                    if (strategy == null)
                    {
                        ErrorHandler.LogWarning($"Cached strategy for type '{type}' is null, removing from cache.", "PathStrategyRegistry");
                        _strategyCache.Remove(type);
                        return null;
                    }
                    return strategy;
                }

                // 未配置时返回 null，由上层 UI 与调用方负责提示与阻止
                ErrorHandler.LogWarning($"No strategy found for curve type '{type}'. Please configure it in the registry.", "PathStrategyRegistry");
                return null;
            }, null, "PathStrategyRegistry.GetStrategy");
        }

        /// <summary>
        /// 检查指定曲线类型是否有可用的策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <returns>如果有可用策略返回true，否则返回false</returns>
        public bool HasStrategy(CurveType type)
        {
            return GetStrategy(type) != null;
        }

        /// <summary>
        /// 获取所有已配置的曲线类型
        /// </summary>
        /// <returns>已配置的曲线类型数组</returns>
        public CurveType[] GetConfiguredCurveTypes()
        {
            return ErrorHandler.SafeExecute(() =>
            {
                if (_strategyCache == null)
                {
                    InitializeCache();
                }

                return _strategyCache?.Keys.ToArray() ?? new CurveType[0];
            }, new CurveType[0], "PathStrategyRegistry.GetConfiguredCurveTypes");
        }

        /// <summary>
        /// 确保策略拥有默认样式
        /// </summary>
        private void EnsureDefaultStyle(PathStrategy strategy)
        {
            if (strategy == null) 
            {
                Debug.LogWarning("[PathStrategyRegistry] Cannot ensure default style for null strategy.");
                return;
            }

            try
            {
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
            catch (System.Exception ex)
            {
                Debug.LogError($"[PathStrategyRegistry] Exception while ensuring default style for strategy '{strategy.name}': {ex.Message}");
            }
        }

        private void OnEnable()
        {
            ErrorHandler.SafeExecute(() =>
            {
                // 防止资源重新加载时实例丢失
                if (_instance == null)
                {
                    _instance = this;
                }

                InitializeCache();
            }, "PathStrategyRegistry.OnEnable");
        }

        private void OnValidate()
        {
            ErrorHandler.SafeExecute(() =>
            {
                // 编辑器下数据变更时更新缓存
                InitializeCache();
            }, "PathStrategyRegistry.OnValidate");
        }

        /// <summary>
        /// 验证注册表配置的完整性
        /// </summary>
        /// <returns>如果配置有效返回true，否则返回false</returns>
        public bool ValidateConfiguration()
        {
            return ErrorHandler.SafeExecute(() =>
            {
                if (_strategyEntries == null || _strategyEntries.Count == 0)
                {
                    ErrorHandler.LogWarning("No strategy entries configured.", "PathStrategyRegistry");
                    return false;
                }

                bool isValid = true;
                var allCurveTypes = System.Enum.GetValues(typeof(CurveType)).Cast<CurveType>();

                foreach (var curveType in allCurveTypes)
                {
                    if (!HasStrategy(curveType))
                    {
                        ErrorHandler.LogWarning($"Missing strategy for curve type '{curveType}'.", "PathStrategyRegistry");
                        isValid = false;
                    }
                }

                return isValid;
            }, false, "PathStrategyRegistry.ValidateConfiguration");
        }
    }
}