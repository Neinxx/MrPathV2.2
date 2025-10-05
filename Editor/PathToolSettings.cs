// 文件路径: Editor/Settings/PathToolSettings.cs
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【重铸版】使用 ScriptableObject 承载工具的全局设置。
/// 这种方法能更好地处理对其他资产（如PathProfile）的引用。
/// </summary>
public class PathToolSettings : ScriptableObject
{
    #region 静态实例管理 (Singleton Access)

    private static PathToolSettings s_Instance;
    public static PathToolSettings Instance
    {
        get
        {
            if (s_Instance == null)
            {
                // 尝试在项目中寻找设置文件
                string[] guids = AssetDatabase.FindAssets ("t:PathToolSettings");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath (guids[0]);
                    s_Instance = AssetDatabase.LoadAssetAtPath<PathToolSettings> (path);
                }
                else
                {
                    // 如果找不到，则在默认位置创建一个新的
                    s_Instance = CreateInstance<PathToolSettings> ();
                    string settingsDir = "Assets/Editor/Settings";
                    if (!Directory.Exists (settingsDir))
                    {
                        Directory.CreateDirectory (settingsDir);
                    }
                    AssetDatabase.CreateAsset (s_Instance, Path.Combine (settingsDir, "PathToolSettings.asset"));
                    AssetDatabase.SaveAssets ();
                    Debug.Log ("PathToolSettings.asset created at " + settingsDir);
                }
            }
            return s_Instance;
        }
    }

    #endregion

    #region 默认创建设置 (Default Creation Settings)

    [Header ("默认创建设置")]

    [Tooltip ("新路径对象的默认名称")]
    public string defaultObjectName = "New Path";

    [Tooltip ("新路径的默认长度")]
    public float defaultLineLength = 10f;

    [Tooltip ("新路径的默认曲线类型")]
    public PathCreator.CurveType defaultCurveType = PathCreator.CurveType.Bezier;

    [Tooltip ("【核心修改】创建新路径时应用的默认外观配置")]
    public PathProfile defaultPathProfile;

    // [废弃] public Material terrainPreviewTemplate;

    #endregion
}
