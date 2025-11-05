#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Stores
{
    [CreateAssetMenu(fileName = "TerrainLayerPickerStore", menuName = "MrPath/Editor/TerrainLayerPickerStore")] 
    public class TerrainLayerPickerStore : ScriptableObject
    {
        private const int MaxRecent = 30;
        private const int MaxFavorites = 60;

        [SerializeField] private List<TerrainLayer> recentLayers = new List<TerrainLayer>();
        [SerializeField] private List<TerrainLayer> favoriteLayers = new List<TerrainLayer>();

        public IReadOnlyList<TerrainLayer> Recent => recentLayers;
        public IReadOnlyList<TerrainLayer> Favorites => favoriteLayers;

        public static TerrainLayerPickerStore GetOrCreate()
        {
            // 尝试查找已存在的存储资产
            var guids = AssetDatabase.FindAssets("t:TerrainLayerPickerStore");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var store = AssetDatabase.LoadAssetAtPath<TerrainLayerPickerStore>(path);
                if (store)
                {
                    store.PruneNulls();
                    return store;
                }
            }

            // 创建新的资产
            var assetPath = EnsureDefaultStorePath();
            var instance = CreateInstance<TerrainLayerPickerStore>();
            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return instance;
        }

        public void AddRecent(TerrainLayer layer)
        {
            if (!layer) return; // 早退
            recentLayers.RemoveAll(l => !l || l == layer);
            recentLayers.Insert(0, layer);
            if (recentLayers.Count > MaxRecent) recentLayers.RemoveRange(MaxRecent, recentLayers.Count - MaxRecent);
            MarkDirty();
        }

        public void ToggleFavorite(TerrainLayer layer)
        {
            if (!layer) return; // 早退

            var idx = favoriteLayers.FindIndex(l => l == layer);
            if (idx >= 0)
            {
                favoriteLayers.RemoveAt(idx);
            }
            else
            {
                // 去重并添加
                favoriteLayers.RemoveAll(l => l == null || l == layer);
                favoriteLayers.Insert(0, layer);
                if (favoriteLayers.Count > MaxFavorites) favoriteLayers.RemoveRange(MaxFavorites, favoriteLayers.Count - MaxFavorites);
            }
            MarkDirty();
        }

        public bool IsFavorite(TerrainLayer layer)
        {
            if (!layer) return false;
            return favoriteLayers.Any(l => l == layer);
        }

        private void PruneNulls()
        {
            recentLayers.RemoveAll(l => l == null);
            favoriteLayers.RemoveAll(l => l == null);
        }

        private static string EnsureDefaultStorePath()
        {
            // 动态定位 MrPathV2 根目录，并确保 Settings 目录存在
            var root = GetPluginRootFolder();
            var settings = $"{root}/Settings";
            EnsureFolderPath(settings);
            return $"{settings}/TerrainLayerPickerStore.asset";
        }

        // 解析插件根目录（包含 "MrPathV2" 的文件夹），支持插件位于 Assets 下任意层级
        private static string GetPluginRootFolder()
        {
            try
            {
                var temp = CreateInstance<TerrainLayerPickerStore>();
                var ms = MonoScript.FromScriptableObject(temp);
                DestroyImmediate(temp);
                var scriptPath = AssetDatabase.GetAssetPath(ms);
                if (string.IsNullOrEmpty(scriptPath)) return "Assets/MrPathV2";
                scriptPath = scriptPath.Replace('\\', '/');
                var parts = scriptPath.Split('/');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i], "MrPathV2", System.StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Join("/", parts.Take(i + 1));
                    }
                }
            }
            catch { }
            // Fallback：默认返回 Assets/MrPathV2
            return "Assets/MrPathV2";
        }

        // 确保形如 "Assets/AAA/BBB" 的 Unity 相对路径存在
        private static void EnsureFolderPath(string unityFolderPath)
        {
            if (string.IsNullOrEmpty(unityFolderPath)) return;
            unityFolderPath = unityFolderPath.Replace('\\', '/');
            var parts = unityFolderPath.Split(new[] {'/'}, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = parts[i];
                var candidate = $"{current}/{next}";
                if (!AssetDatabase.IsValidFolder(candidate))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = candidate;
            }
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
