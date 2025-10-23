#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
// using System.Linq; // <-- 不再需要
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
    /// [优化版] 使用缓存 + 同目录优先策略，查找更健壮、更高效。
    /// </summary>
    internal static class UIResourceLoader
    {
        private static readonly Dictionary<string, VisualTreeAsset> UxmlCache = new();
        private static readonly Dictionary<string, StyleSheet> USSCache = new();

        /// <summary>
        /// [优化] 为 MonoScript 查找添加缓存，避免重复的 AssetDatabase 查询
        /// </summary>
        private static readonly Dictionary<Type, MonoScript> ScriptCache = new();

        /// <summary>
        /// 加载与 <typeparamref name="T"/> 同名的 .uxml 文件，并可选自动应用同名 .uss。
        /// </summary>
        /// <remarks>公共 API 保持不变</remarks>
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
                // else: 找不到可选的 .uss 不是错误
            }

            return root;
        }

        /// <summary>
        /// 加载与指定类型同名的 .uxml 资源（带缓存）
        /// </summary>
        /// <remarks>公共 API 保持不变</remarks>
        public static VisualTreeAsset LoadUxml(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var key = type.FullName;

            if (key != null && UxmlCache.TryGetValue(key, out var cached))
                return cached;

            var asset = FindAsset<VisualTreeAsset>(type.Name, type);
            if (key != null) UxmlCache[key] = asset; // 缓存结果（即使是 null）
            return asset;
        }

        /// <summary>
        /// 加载与指定类型同名的 .uss 资源（带缓存）
        /// </summary>
        /// <remarks>公共 API 保持不变</remarks>
        public static StyleSheet LoadUss(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var key = type.FullName;

            if (key != null && USSCache.TryGetValue(key, out var cached))
                return cached;

            var asset = FindAsset<StyleSheet>(type.Name, type);
            if (key != null) USSCache[key] = asset; // 缓存结果（即使是 null）
            return asset;
        }

        /// <summary>
        /// [优化] 健壮地查找任何类型（MonoBehaviour, ScriptableObject, EditorWindow...）
        /// </summary>
        private static MonoScript FindMonoScriptForType(Type type)
        {
            if (ScriptCache.TryGetValue(type, out var cachedScript))
                return cachedScript;

            var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                // 验证 MonoScript 确实是我们要找的那个类
                if (script != null && script.GetClass() == type)
                {
                    ScriptCache[type] = script;
                    return script;
                }
            }

            ScriptCache[type] = null; // 缓存失败
            return null;
        }

        /// <summary>
        /// [优化] 通用资源查找：
        /// 1. 查找脚本路径更健壮。
        /// 2. 资源类型过滤器更准确 (t:StyleSheet)。
        /// 3. 只迭代 guids 一次，性能更高。
        /// </summary>
        private static T FindAsset<T>(string fileName, Type ownerType) where T : UnityEngine.Object
        {
            // [优化] Step 1: 健壮地获取脚本目录
            string scriptDir = null;
            var scriptAsset = FindMonoScriptForType(ownerType);
            if (scriptAsset != null)
            {
                scriptDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(scriptAsset));
            }

            // [优化] Step 2: 使用正确的类型过滤器搜索
            var typeFilter = $"t:{typeof(T).Name}";
            var guids = AssetDatabase.FindAssets($"{fileName} {typeFilter}");

            if (guids.Length == 0)
                return null;

            // [优化] Step 3: 迭代一次，将路径分为“同目录”和“其他”
            var sameDirPaths = new List<string>();
            var otherPaths = new List<string>();

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                // 同目录优先
                if (scriptDir != null)
                {
                    var assetDir = Path.GetDirectoryName(assetPath);
                    if (string.Equals(assetDir, scriptDir, StringComparison.OrdinalIgnoreCase))
                    {
                        sameDirPaths.Add(assetPath);
                        continue; // 移至优先列表
                    }
                }
                otherPaths.Add(assetPath);
            }

            // Step 4: 优先加载同目录的资源
            // (LoadAssetAtPath<T> 会自动处理类型不匹配并返回 null)
            foreach (var path in sameDirPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }

            // Step 5: 回退到加载其他目录的资源
            foreach (var path in otherPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }

            return null;
        }

        /// <summary>
        /// 仅根据类名加载同名 UXML/USS，不尝试获取脚本目录。
        /// </summary>
        /// <remarks>公共 API 保持不变</remarks>
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

        /// <summary>
        /// [优化] 添加了缓存
        /// </summary>
        private static VisualTreeAsset LoadUxmlByName(string className)
        {
            if (UxmlCache.TryGetValue(className, out var cached))
                return cached;

            var guids = AssetDatabase.FindAssets($"{className} t:VisualTreeAsset");
            var asset = guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;

            UxmlCache[className] = asset; // 缓存结果
            return asset;
        }

        /// <summary>
        /// [优化] 添加了缓存，并修复了类型过滤器
        /// </summary>
        private static StyleSheet LoadUssByName(string className)
        {
            if (USSCache.TryGetValue(className, out var cached))
                return cached;

            // [优化] 直接使用 t:StyleSheet，移除 t:ScriptableObject 和 LINQ
            var guids = AssetDatabase.FindAssets($"{className} t:StyleSheet");
            var asset = guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;

            USSCache[className] = asset; // 缓存结果
            return asset;
        }

        /// <summary>
        /// 清除缓存（用于 Domain Reload 或测试）
        /// </summary>
        /// <remarks>公共 API 保持不变</remarks>
        public static void ClearCache()
        {
            UxmlCache.Clear();
            USSCache.Clear();
            ScriptCache.Clear(); // [优化] 清除脚本缓存
        }
    }
}
#endif