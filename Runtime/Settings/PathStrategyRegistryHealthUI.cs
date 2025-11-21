using System.IO;
using UnityEngine;

namespace MrPathV2.Runtime.Settings
{
    public sealed class PathStrategyRegistryHealthUI : MonoBehaviour
    {
        private bool _show;

        private void OnEnable()
        {
            _show = PathStrategyRegistry.Instance == null;
        }

        private void Update()
        {
            if (_show) return;
            _show = PathStrategyRegistry.Instance == null;
        }

        private void OnGUI()
        {
            if (!_show) return;
            const int w = 460;
            const int h = 120;
            var rect = new Rect(16, 16, w, h);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("PathStrategyRegistry 资产缺失或未加载。运行时功能可能受限。");
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新加载"))
            {
                typeof(PathStrategyRegistry).GetProperty("Instance")?.GetValue(null);
                _show = PathStrategyRegistry.Instance == null;
            }
            GUILayout.FlexibleSpace();
            #if UNITY_EDITOR
            if (GUILayout.Button("一键创建到 Resources"))
            {
                CreateRegistryAsset();
                _show = PathStrategyRegistry.Instance == null;
            }
            #else
            GUILayout.Label("请在编辑器中创建资产：Resources/PathStrategyRegistry.asset");
            #endif
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        #if UNITY_EDITOR
        private static void CreateRegistryAsset()
        {
            var resourcesDir = Path.Combine("Assets", "Resources");
            if (!Directory.Exists(resourcesDir)) Directory.CreateDirectory(resourcesDir);
            var assetPath = Path.Combine(resourcesDir, "PathStrategyRegistry.asset");
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<PathStrategyRegistry>(assetPath);
            if (existing == null)
            {
                var asset = ScriptableObject.CreateInstance<PathStrategyRegistry>();
                UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
            }
        }
        #endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            if (PathStrategyRegistry.Instance != null) return;
            var go = new GameObject("PathStrategyRegistryHealthUI");
            DontDestroyOnLoad(go);
            go.AddComponent<PathStrategyRegistryHealthUI>();
        }
    }
}
