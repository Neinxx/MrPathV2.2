#if UNITY_EDITOR
using System.Collections.Generic;
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
            recentLayers.RemoveAll(l => l == null || l == layer);
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
            var folder = "Assets/MrPathV2/Editor/Stores";
            EnsureFolderExists("Assets/MrPathV2");
            EnsureFolderExists("Assets/MrPathV2/Editor");
            EnsureFolderExists(folder);
            return folder + "/TerrainLayerPickerStore.asset";
        }

        private static void EnsureFolderExists(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var segments = folder.Split('/');
                var current = segments[0];
                for (var i = 1; i < segments.Length; i++)
                {
                    var next = current + "/" + segments[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, segments[i]);
                    }
                    current = next;
                }
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