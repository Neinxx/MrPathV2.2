// PathStrategyRegistry.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor;
using MrPathV2; // 我们需要用到编辑器API来查找这个资产




/// <summary>
/// 【第四步：万法总纲】
/// 路径策略注册中心。这是一个全局唯一的 ScriptableObject 单例。
/// 
/// 它的职责是维护 CurveType 枚举与具体的 PathStrategy 资产之间的映射关系。
/// 这使得我们可以在用户界面中使用便捷的下拉菜单，同时在底层使用强大的策略资产。
/// </summary>
[CreateAssetMenu(fileName = "PathStrategyRegistry", menuName = "MrPath/Strategy Registry")]
public class PathStrategyRegistry : ScriptableObject
{
    /// <summary>
    /// 用于在Inspector中方便地设置映射关系。
    /// </summary>
    [System.Serializable]
    public struct StrategyMapping
    {
        public CurveType type;
        public PathStrategy strategy;
    }

    [SerializeField]
    private List<StrategyMapping> strategyMappings = new();

    // --- 单例模式实现 ---
    private static PathStrategyRegistry _instance;
    public static PathStrategyRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                // 这段代码只在编辑器环境下有效，用于自动查找资产
#if UNITY_EDITOR
                string[] guids = AssetDatabase.FindAssets($"t:{nameof(PathStrategyRegistry)}");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _instance = AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(path);
                }
                else
                {
                    // 如果找不到，可以提供一个更友好的错误或自动创建的选项
                    Debug.LogError("[MrPath] 关键错误: 未在项目中找到 PathStrategyRegistry.asset 文件！请在项目中创建一个。");
                }
#endif
            }
            return _instance;
        }
    }

    // --- 高效查询实现 ---
    private Dictionary<CurveType, PathStrategy> _lookup;

    // 当资产被加载或Inspector中的值被修改时调用
    private void OnEnable()
    {
        // 将列表转换为字典，以实现O(1)复杂度的快速查找
        _lookup = strategyMappings.ToDictionary(m => m.type, m => m.strategy);
    }

    /// <summary>
    /// 根据给定的曲线类型枚举，获取对应的策略资产。
    /// </summary>
    public PathStrategy GetStrategy(CurveType type)
    {
        // OnEnable 在非编辑器模式下可能不会被及时调用，这里加一个保险
        if (_lookup == null)
        {
            OnEnable();
        }

        if (_lookup.TryGetValue(type, out PathStrategy strategy))
        {
            return strategy;
        }

        Debug.LogError($"[PathStrategyRegistry] 未找到与类型 '{type}' 关联的策略资产。请检查注册中心资产的设置。");
        return null;
    }
}