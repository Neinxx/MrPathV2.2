using UnityEditor;
using UnityEngine;

public static class AssetRegistry
{
    private static readonly string ProfilesPath = "Assets/MrPathV2.2/Settings/Profiles/";
    private static readonly string StrategiesPath = "Assets/MrPathV2.2/Settings/Strategies/";
    private static readonly string OperationsPath = "Assets/MrPathV2.2/Settings/Operations/";

    public static T LoadProfile<T>(string assetName) where T : ScriptableObject
    {
        return LoadAsset<T>(ProfilesPath + assetName + ".asset");
    }

    public static T LoadStrategy<T>(string assetName) where T : ScriptableObject
    {
        return LoadAsset<T>(StrategiesPath + assetName + ".asset");
    }

    public static T LoadOperation<T>(string assetName) where T : ScriptableObject
    {
        return LoadAsset<T>(OperationsPath + assetName + ".asset");
    }

    private static T LoadAsset<T>(string path) where T : ScriptableObject
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<T>(path);
#else
        return null; // 或者实现运行时加载逻辑
#endif
    }
}