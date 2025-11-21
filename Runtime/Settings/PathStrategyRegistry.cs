using System;
using System.Collections.Generic;
using System.Linq;
using MrPathV2.Runtime.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using MrPathV2.Runtime.Core.Resources;
using UnityEngine.Serialization;

// 保留以兼容可能的 Task 用法（若不需要可后续移除）

namespace MrPathV2.Runtime.Settings
{
    /// <summary>
    ///     路径策略注册中心：负责管理CurveType与PathStrategy的映射关系
    ///     采用单例模式确保全局唯一访问点，支持数据驱动配置
    /// </summary>
    [CreateAssetMenu(fileName = "PathStrategyRegistry", menuName = "MrPath/Path Strategy Registry", order = 100)]
    public class PathStrategyRegistry : ScriptableObject
    {
        private static PathStrategyRegistry m_Instance;
        private static bool m_InitializationAttempted;

        [FormerlySerializedAs("_strategyEntries")]
        [Header("策略映射配置")]
        [Tooltip("曲线类型与策略的映射列表")]
        [SerializeField] private List<StrategyEntry> strategyEntries = new List<StrategyEntry>();

        /// <summary>
        ///     获取指定曲线类型的策略
        /// </summary>
        /// <returns>对应的路径策略，若未找到则返回null</returns>
        // 添加一个字段用于跟踪已警告过的曲线类型，避免重复输出相同警告
        private readonly HashSet<CurveType> _loggedMissingStrategies = new HashSet<CurveType>();

        // 缓存策略映射，提高查询性能
        private Dictionary<CurveType, PathStrategy> _strategyCache;

        /// <summary>
        ///     全局唯一实例
        /// </summary>
        public static PathStrategyRegistry Instance
        {
            get
            {
                if (!m_Instance && !m_InitializationAttempted)
                {
                    InitializeInstance();
                }
                return m_Instance;
            }
        }

        private void OnEnable()
        {
            ErrorHandler.SafeExecute(() =>
            {
                // 防止资源重新加载时实例丢失
                if (!m_Instance)
                {
                    m_Instance = this;
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
        ///     初始化实例
        /// </summary>
        private static void InitializeInstance()
        {
            m_InitializationAttempted = true;

            ErrorHandler.SafeExecute(() =>
            {
                // 尝试通过Resources加载实例
                m_Instance = LoadFromResources();

                // 如果Resources加载失败，尝试在编辑器中查找
                if (!m_Instance)
                {
                    m_Instance = FindInEditor();
                }

                // 处理加载结果
                HandleLoadResult();
            }, "PathStrategyRegistry.InitializeInstance");
        }

        /// <summary>
        ///     从Resources目录加载PathStrategyRegistry实例
        /// </summary>
        /// <returns>加载的实例，如果失败则返回null</returns>
        private static PathStrategyRegistry LoadFromResources() => ResourceProvider.LoadAsset<PathStrategyRegistry>("PathStrategyRegistry");

        /// <summary>
        ///     在编辑器中查找PathStrategyRegistry实例
        /// </summary>
        /// <returns>找到的实例，如果失败或不在编辑器中则返回null</returns>
        private static PathStrategyRegistry FindInEditor()
        {
        #if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets($"t:{nameof(PathStrategyRegistry)}");
            if (guids?.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                if (!string.IsNullOrEmpty(path))
                {
                    return AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(path);
                }
            }
        #endif
            return null;
        }

        /// <summary>
        ///     处理加载结果
        /// </summary>
        private static void HandleLoadResult()
        {
            if (m_Instance)
            {
                // 初始化缓存
                m_Instance.InitializeCache();
            }
            else
            {
                ErrorHandler.LogError("Failed to load PathStrategyRegistry asset. Please ensure it exists in a Resources folder or create one using the menu: Assets > Create > MrPath > Path Strategy Registry", "PathStrategyRegistry");
            }
        }

        /// <summary>
        ///     初始化策略缓存
        /// </summary>
        private void InitializeCache()
        {
            ErrorHandler.SafeExecute(() =>
            {
                _strategyCache = new Dictionary<CurveType, PathStrategy>();

                // 提前返回：如果策略条目为空，初始化空列表并返回
                if (strategyEntries == null)
                {
                    InitializeEmptyStrategyEntries();
                    return;
                }

                // 处理策略条目
                ProcessStrategyEntries();

                // 报告缓存初始化结果
                LogInitializationResult();

                // 验证配置完整性
                ValidateConfiguration();
            }, "PathStrategyRegistry.InitializeCache");
        }

        /// <summary>
        ///     初始化空的策略条目列表
        /// </summary>
        private void InitializeEmptyStrategyEntries()
        {
            ErrorHandler.LogWarning("Strategy entries list is null, initializing empty list.", "PathStrategyRegistry");
            strategyEntries = new List<StrategyEntry>();
        }

        /// <summary>
        ///     处理策略条目列表
        /// </summary>
        private void ProcessStrategyEntries()
        {
            var duplicateTypes = new HashSet<CurveType>();
            var processedTypes = new HashSet<CurveType>();

            foreach (var entry in strategyEntries)
            {
                // 跳过无效条目
                if (!ValidateEntry(entry, processedTypes, duplicateTypes))
                    continue;

                // 处理有效条目
                ProcessValidEntry(entry, processedTypes);
            }
        }

        /// <summary>
        ///     验证条目是否有效
        /// </summary>
        /// <param name="entry">策略条目</param>
        /// <param name="processedTypes">已处理的类型集合</param>
        /// <param name="duplicateTypes">重复的类型集合</param>
        /// <returns>条目是否有效</returns>
        private static bool ValidateEntry(StrategyEntry entry, HashSet<CurveType> processedTypes, HashSet<CurveType> duplicateTypes)
        {
            // 检查条目是否有效
            if (!entry.IsValid)
            {
                ErrorHandler.LogWarning($"Invalid strategy entry for curve type '{entry.type}' - strategy is null.", "PathStrategyRegistry");
                return false;
            }

            // 检查是否已处理过该类型
            if (processedTypes.Contains(entry.type))
            {
                duplicateTypes.Add(entry.type);
                ErrorHandler.LogWarning($"Duplicate strategy entry found for curve type '{entry.type}'. Only the first valid entry will be used.", "PathStrategyRegistry");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     处理有效的策略条目
        /// </summary>
        /// <param name="entry">策略条目</param>
        /// <param name="processedTypes">已处理的类型集合</param>
        private void ProcessValidEntry(StrategyEntry entry, HashSet<CurveType> processedTypes)
        {
            ErrorHandler.SafeExecute(() =>
                {
                    EnsureDefaultStyle(entry.strategy);
                    _strategyCache[entry.type] = entry.strategy;
                    processedTypes.Add(entry.type);
                }, $"PathStrategyRegistry.InitializeCache.ProcessEntry({entry.type})");
        }

        /// <summary>
        ///     记录初始化结果
        /// </summary>
        private void LogInitializationResult()
        {
            //ErrorHandler.LogInfo($"Cache initialized with {_strategyCache.Count} strategies.", "PathStrategyRegistry");
            if (_strategyCache.Count == 0)
            {
                ErrorHandler.LogWarning("No valid strategy entries found. Check the strategyEntries list in the inspector.", "PathStrategyRegistry");
            }
        }

        public PathStrategy GetStrategy(CurveType type)
        {
            // 快速路径：缓存存在且包含有效策略时直接返回
            if (TryGetCachedStrategy(type, out var cachedStrategy))
            {
                return cachedStrategy;
            }

            return ErrorHandler.SafeExecute(() =>
            {
                // 确保缓存已初始化
                if (!EnsureCacheInitialized())
                {
                    return null;
                }

                // 尝试从缓存获取策略
                if (TryGetStrategyFromCache(type, out var strategy))
                {
                    return strategy;
                }

                // 记录缺失的策略
                LogMissingStrategy(type);

                return null;
            }, null, "PathStrategyRegistry.GetStrategy");
        }

        /// <summary>
        ///     尝试从缓存中获取策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <param name="strategy">获取到的策略</param>
        /// <returns>是否成功获取策略</returns>
        private bool TryGetCachedStrategy(CurveType type, out PathStrategy strategy)
        {
            strategy = null;

            if (_strategyCache != null &&
                _strategyCache.TryGetValue(type, out strategy) &&
                strategy)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     确保缓存已初始化
        /// </summary>
        /// <returns>缓存是否初始化成功</returns>
        private bool EnsureCacheInitialized()
        {
            // 如果缓存为空，尝试初始化
            if (_strategyCache == null)
            {
                ErrorHandler.LogWarning("Strategy cache is null, attempting to reinitialize.", "PathStrategyRegistry");
                InitializeCache();

                if (_strategyCache == null)
                {
                    ErrorHandler.LogError("Failed to initialize strategy cache.", "PathStrategyRegistry");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     尝试从缓存中获取策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <param name="strategy">获取到的策略</param>
        /// <returns>是否成功获取策略</returns>
        private bool TryGetStrategyFromCache(CurveType type, out PathStrategy strategy)
        {
            strategy = null;

            // 再次检查缓存（初始化后可能已有值）
            if (_strategyCache.TryGetValue(type, out strategy))
            {
                if (strategy)
                    return true;

                // 移除无效缓存项（只做一次）
                _strategyCache.Remove(type);
            }

            return false;
        }

        /// <summary>
        ///     记录缺失的策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        private void LogMissingStrategy(CurveType type)
        {
            // 只对每种缺失类型警告一次，减少日志开销
            if (!_loggedMissingStrategies.Contains(type))
            {
                ErrorHandler.LogWarning($"No strategy found for curve type '{type}'. Please configure it in the registry.", "PathStrategyRegistry");
                _loggedMissingStrategies.Add(type);
            }
        }

        /// <summary>
        ///     检查指定曲线类型是否有可用的策略
        /// </summary>
        /// <param name="type">曲线类型</param>
        /// <returns>如果有可用策略返回true，否则返回false</returns>
        private bool HasStrategy(CurveType type) => GetStrategy(type);

        /// <summary>
        ///     获取所有已配置的曲线类型
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
        ///     确保策略拥有默认样式
        /// </summary>
        private static void EnsureDefaultStyle(PathStrategy strategy)
        {
            if (!strategy)
            {
                ErrorHandler.LogWarning("[PathStrategyRegistry] Cannot ensure default style for null strategy.");
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
            catch (Exception ex)
            {
                ErrorHandler.LogError($"[PathStrategyRegistry] Exception while ensuring default style for strategy '{strategy.name}': {ex.Message}");
            }
        }

        /// <summary>
        ///     验证注册表配置的完整性
        /// </summary>
        /// <returns>如果配置有效返回true，否则返回false</returns>
        public bool ValidateConfiguration()
        {
            return ErrorHandler.SafeExecute(() =>
            {
                if (strategyEntries == null || strategyEntries.Count == 0)
                {
                    ErrorHandler.LogWarning("No strategy entries configured.", "PathStrategyRegistry");
                    return false;
                }

                var isValid = true;
                var allCurveTypes = Enum.GetValues(typeof(CurveType)).Cast<CurveType>();

                foreach (var curveType in allCurveTypes)
                {
                    if (HasStrategy(curveType)) continue;
                    ErrorHandler.LogWarning($"Missing strategy for curve type '{curveType}'.", "PathStrategyRegistry");
                    isValid = false;
                }

                return isValid;
            }, false, "PathStrategyRegistry.ValidateConfiguration");
        }

        [Serializable]
        public struct StrategyEntry : IEquatable<StrategyEntry>
        {
            [Tooltip("曲线类型")]
            public CurveType type;

            [Tooltip("对应的路径策略实例")]
            public PathStrategy strategy;

            public bool Equals(StrategyEntry other) => type == other.type && strategy && strategy && other.strategy && strategy == other.strategy;

            public override bool Equals(object obj) => obj is StrategyEntry other && Equals(other);

            public override int GetHashCode() => HashCode.Combine((int)type, strategy);

            /// <summary>
            ///     验证策略条目是否有效
            /// </summary>
            public bool IsValid => strategy;
        }
    }
}
