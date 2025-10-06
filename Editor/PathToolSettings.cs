using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【返璞归真版】MrPath 工具的全局配置类。
/// 简化了单例模式，专注于作为数据容器的核心职责。
/// </summary>
[CreateAssetMenu(fileName = "MrPathSettings", menuName = "MrPath/Global Settings")]
public class PathToolSettings : ScriptableObject
{
    #region 静态实例管理

    private static PathToolSettings _instance;
    public static PathToolSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                // 优先从项目中加载
                string[] guids = AssetDatabase.FindAssets($"t:{nameof(PathToolSettings)}");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _instance = AssetDatabase.LoadAssetAtPath<PathToolSettings>(path);
                }

                // 加载失败或不存在，则创建新实例
                if (_instance == null)
                {
                    _instance = CreateInstance<PathToolSettings>();
                    string assetPath = "Assets/Editor/MrPath/Settings/MrPathSettings.asset";
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                    AssetDatabase.CreateAsset(_instance, assetPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[MrPath] 未找到设置文件，已在 {assetPath} 创建。");
                }
            }
            return _instance;
        }
    }

    #endregion

    #region 配置字段

    [Header("默认创建设置")]
    [Tooltip("新创建路径对象的默认名称")]
    public string defaultObjectName = "New Path";

    [Tooltip("新创建路径的初始线段长度（单位：米）")]
    [Min(0.1f)]
    public float defaultLineLength = 10f;

    [Header("默认外观配置")]
    [Tooltip("新创建路径将使用的默认外观配置文件")]
    public PathProfile defaultPathProfile;

    #endregion

    #region 初始化与校验

    private void OnValidate()
    {
        // OnValidate 是校验和修正数据的最佳场所
        if (string.IsNullOrEmpty(defaultObjectName))
        {
            defaultObjectName = "New Path";
        }
        if (defaultLineLength < 0.1f)
        {
            defaultLineLength = 0.1f;
        }
    }

    #endregion
}