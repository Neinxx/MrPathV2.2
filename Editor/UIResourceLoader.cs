#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace __temp.MrPathV2._2.Editor
{
    /// <summary>
    /// 自动加载与指定类型同名的 .uxml 和 .uss 资源。
    /// 要求：C# 类名、.uxml 文件名、.uss 文件名三者完全一致（不含扩展名）。
    /// 示例：TerrainOperationsOverlay.cs → TerrainOperationsOverlay.uxml + .uss
    /// 
    /// 使用缓存 + 同目录优先策略，确保正确性与性能。
    /// </summary>
    internal static class UIResourceLoader
    {
        private static readonly Dictionary<string, VisualTreeAsset> UxmlCache = new();
        private static readonly Dictionary<string, StyleSheet> USSCache = new();

        /// <summary>
        /// 加载与 <typeparamref name="T"/> 同名的 .uxml 文件，并可选自动应用同名 .uss。
        /// </summary>
        /// <typeparam name="T">目标 C# 类型（通常为当前类）</typeparam>
        /// <param name="autoApplyUss">是否自动查找并应用同名 .uss 样式表</param>
        /// <returns>克隆后的 VisualElement 根节点</returns>
        public static VisualElement LoadAndClone<T>(bool autoApplyUss = true) where T : class
        {
            var type = typeof(T);
            var uxml = LoadUxml(type);
            if (uxml == null)
            {
                Debug.LogError($"[UIResourceLoader] Failed to load .uxml for type: {type.Name}");
                return new Label($"[Missing UXML: {type.Name}.uxml]");
            }

            var root = uxml.CloneTree();

            if (autoApplyUss)
            {
                var uss = LoadUss(type);
                if (uss != null)
                {
                    root.styleSheets.Add(uss);
                }
                else
                {
                    // 可选：仅在调试时提示，避免噪音
                    // Debug.Log($"[UIResourceLoader] Optional .uss not found for: {type.Name}");
                }
            }

            return root;
        }

        /// <summary>
        /// 加载与指定类型同名的 .uxml 资源（带缓存）
        /// </summary>
        public static VisualTreeAsset LoadUxml(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var key = type.FullName;

            if (key != null && UxmlCache.TryGetValue(key, out var cached))
                return cached;

            var asset = FindAsset<VisualTreeAsset>(type.Name, type);
            if (key != null) UxmlCache[key] = asset;
            return asset;
        }

        /// <summary>
        /// 加载与指定类型同名的 .uss 资源（带缓存）
        /// </summary>
        public static StyleSheet LoadUss(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var key = type.FullName;

            if (key != null && USSCache.TryGetValue(key, out var cached))
                return cached;

            var asset = FindAsset<StyleSheet>(type.Name, type);
            if (key != null) USSCache[key] = asset;
            return asset;
        }

        /// <summary>
        /// 通用资源查找：优先同目录，其次任意位置
        /// </summary>
        private static T FindAsset<T>(string fileName, Type ownerType) where T : UnityEngine.Object
        {
            // Step 1: 获取当前脚本所在目录（用于同目录优先）
            string scriptDir = null;
            try
            {
                var monoScript = MonoScript.FromScriptableObject(ScriptableObject.CreateInstance(ownerType));
                var scriptPath = AssetDatabase.GetAssetPath(monoScript);
                scriptDir = Path.GetDirectoryName(scriptPath);
            }
            catch
            {
                // Fallback: 如果无法获取脚本路径（如泛型类），则跳过同目录匹配
            }

            // Step 2: 搜索所有匹配资源
            var typeFilter = typeof(T) == typeof(StyleSheet) ? "t:ScriptableObject" : $"t:{typeof(T).Name}";
            var guids = AssetDatabase.FindAssets($"{fileName} {typeFilter}");

            if (guids.Length == 0)
                return null;

            // Step 3: 优先选择同目录下的资源
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                // 验证类型（FindAssets 可能返回派生类型）
                if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) != typeof(T))
                    continue;

                // 同目录优先
                if (scriptDir != null)
                {
                    var assetDir = Path.GetDirectoryName(assetPath);
                    if (string.Equals(assetDir, scriptDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return AssetDatabase.LoadAssetAtPath<T>(assetPath);
                    }
                }
            }

            // Step 4: 退化到第一个有效资源
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath) &&
                    AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(T))
                {
                    return AssetDatabase.LoadAssetAtPath<T>(assetPath);
                }
            }

            return null;
        }

        /// <summary>
        /// 仅根据类名加载同名 UXML/USS，不尝试获取脚本目录（适用于非 ScriptableObject 类型，如 SettingsProvider）。
        /// </summary>
        public static VisualElement LoadAndCloneByName(string className, bool autoApplyUss = true)
        {
            var uxml = LoadUxmlByName(className);
            if (uxml == null)
            {
                Debug.LogError($"[UIResourceLoader] Failed to load .uxml for class: {className}");
                return new Label($"[Missing UXML: {className}.uxml]");
            }

            var root = uxml.CloneTree();

            if (!autoApplyUss) return root;
            var uss = LoadUssByName(className);
            if (uss != null)
                root.styleSheets.Add(uss);

            return root;
        }

        private static VisualTreeAsset LoadUxmlByName(string className)
        {
            var guids = AssetDatabase.FindAssets($"{className} t:VisualTreeAsset");
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        private static StyleSheet LoadUssByName(string className)
        {
            var guids = AssetDatabase.FindAssets($"{className} t:ScriptableObject");
            return (from guid in guids select AssetDatabase.GUIDToAssetPath(guid) into path where path.EndsWith(".uss") select AssetDatabase.LoadAssetAtPath<StyleSheet>(path)).FirstOrDefault();
        }

        /// <summary>
        /// 清除缓存（用于 Domain Reload 或测试）
        /// </summary>
        public static void ClearCache()
        {
            UxmlCache.Clear();
            USSCache.Clear();
        }
    }
}
#endif