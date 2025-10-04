using System.IO;
using UnityEditor;
using UnityEngine;

// 为了让JsonUtility能够序列化枚举，我们需要一个辅助类
[System.Serializable]
public class PathToolSettings
{
    // --- 可配置的参数 ---
    public float defaultLineLength = 5f;
    public PathCreator.CurveType defaultCurveType = PathCreator.CurveType.Bezier;
    public string defaultObjectName = "MrPath";

    public Material terrainPreviewTemplate;

    // --- 加载与保存逻辑 ---
    private static readonly string settingsPath = "ProjectSettings/PathToolSettings.json";
    private static PathToolSettings s_instance;

    public static PathToolSettings Instance
    {
        get
        {
            if (s_instance == null)
            {
                Load ();
            }
            return s_instance;
        }
    }

    public static void Save ()
    {
        if (s_instance == null) return;
        string json = JsonUtility.ToJson (s_instance, true);
        File.WriteAllText (settingsPath, json);
    }

    private static void Load ()
    {
        if (File.Exists (settingsPath))
        {
            string json = File.ReadAllText (settingsPath);
            s_instance = JsonUtility.FromJson<PathToolSettings> (json);
        }
        else
        {
            // 如果文件不存在，则创建一个默认实例
            s_instance = new PathToolSettings ();
        }
    }
}
