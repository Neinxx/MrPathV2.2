// MaskAssetService.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MrPathV2.Runtime.Core.BlendMasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
// 假设 BlendMaskBase 位于此命名空间

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    ///     遮罩资产服务：管理 BlendMaskBase 资产的 CURD、加载、缓存和类型发现。
    /// </summary>
    public class LayerMaskAssetService
    {
        public const string DefaultMaskName = "NewMask";

        private readonly List<BlendMaskBase> _allMasks = new List<BlendMaskBase>();
        private readonly Dictionary<int, Texture2D> _iconCache = new Dictionary<int, Texture2D>();
        private Type[] _availableMaskTypes = Array.Empty<Type>();
        private Texture2D _nullIcon;

        // --- 公开属性 ---
        public IReadOnlyList<BlendMaskBase> AllMasks => _allMasks;
        public IReadOnlyList<Type> AvailableMaskTypes => _availableMaskTypes;

        // Null 遮罩的占位图标（缓存）
        public Texture2D NullIcon => _nullIcon ??= CreateNullIcon();

        // --- 初始化与加载 ---

        public void Initialize()
        {
            ClearData();
            FindAvailableMaskTypes();
            ReloadMasksAndCache();
        }

        private void ClearData()
        {
            _allMasks.Clear();
            _iconCache.Clear();
        }

        public void ReloadMasksAndCache()
        {
            ClearData(); // 清空旧数据
            LoadBlendMasks();
        }

        private void LoadBlendMasks()
        {
            var guids = AssetDatabase.FindAssets("t:BlendMaskBase");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mask = AssetDatabase.LoadAssetAtPath<BlendMaskBase>(path);

                if (mask == null) continue;

                _allMasks.Add(mask);
                // 缩略图缓存
                _iconCache[mask.GetInstanceID()] = AssetPreview.GetMiniThumbnail(mask);
            }

            // 统一排序
            _allMasks.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        }

        // --- 资产 CURD ---

        public BlendMaskBase CreateNewMaskAsset(Type maskType, string nameHint)
        {
            if (!IsMaskTypeValid(maskType)) return null;

            var cleanName = string.IsNullOrWhiteSpace(nameHint) ? maskType.Name : nameHint.Trim();
            // 确保生成的名称在所有资产中是唯一的
            var uniqueName = ObjectNames.GetUniqueName(_allMasks.Select(x => x.name).ToArray(), cleanName);

            var instance = ScriptableObject.CreateInstance(maskType) as BlendMaskBase;
            if (instance == null) return null;

            instance.name = uniqueName;

            var defaultStorePath = GetMasksFolder();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{defaultStorePath}/{uniqueName}.asset");

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // 刷新资产数据库

            return instance; // 返回新创建的实例，列表管理器会调用 ReloadMasksAndCache
        }

        public BlendMaskBase DuplicateMaskAsset(BlendMaskBase source)
        {
            if (source == null) return null;

            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath)) return null;

            var folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            var baseName = source.name + " Copy";
            var newName = ObjectNames.GetUniqueName(_allMasks.Select(x => x.name).ToArray(), baseName);

            var newObj = Object.Instantiate(source);
            newObj.name = newName;

            var newPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{newName}.asset");

            AssetDatabase.CreateAsset(newObj, newPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return newObj;
        }

        public void RenameMaskAsset(BlendMaskBase mask, string newName)
        {
            if (mask == null || string.IsNullOrWhiteSpace(newName)) return;

            var oldPath = AssetDatabase.GetAssetPath(mask);
            if (string.IsNullOrEmpty(oldPath))
            {
                Debug.LogError($"[MaskAssetService] 无法获取资产 '{mask.name}' 的路径，重命名失败。");
                return;
            }

            var trimmedName = newName.Trim();

            // 卫语句：如果名称相同，提前返回
            if (mask.name == trimmedName)
            {
                return;
            }

            // 使用 Unity API 直接重命名文件
            var renameError = AssetDatabase.RenameAsset(oldPath, trimmedName);

            if (string.IsNullOrEmpty(renameError)) return;
            // 如果重命名失败，向用户弹出对话框并报告错误
            Debug.LogError($"[MaskAssetService] 重命名资产文件 '{mask.name}' 到 '{trimmedName}' 失败: {renameError}");

            EditorUtility.DisplayDialog("重命名失败",
                $"无法将资产重命名为 '{trimmedName}'。\n" +
                "原因：该名称已存在或包含无效字符。请使用唯一的名称。",
                "确定");
            // 如果成功，AssetDatabase.RenameAsset 会自动更新 mask.name
        }

        public void DeleteMaskAssets(IEnumerable<BlendMaskBase> masksToDelete)
        {
            var paths = masksToDelete
                .Where(m => m != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            foreach (var path in paths)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // --- 缩略图与类型发现 ---

        public Texture2D GetMaskThumbnail(BlendMaskBase mask)
        {
            if (mask == null) return NullIcon;

            var id = mask.GetInstanceID();

            if (_iconCache.TryGetValue(id, out var thumbnail) && thumbnail != null)
                return thumbnail;

            // 重新获取并缓存
            thumbnail = AssetPreview.GetMiniThumbnail(mask);
            _iconCache[id] = thumbnail;
            return thumbnail;
        }

        public void SetFallbackMaskType()
        {
            _availableMaskTypes = new[]
            {
                typeof(BlendMaskBase)
            };
        }

        private void FindAvailableMaskTypes()
        {
            _availableMaskTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetTypesFromAssembly)
                .Where(IsMaskTypeValid)
                .ToArray();
        }

        private Type[] GetTypesFromAssembly(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch
            {
                return Type.EmptyTypes;
            }
        }

        private bool IsMaskTypeValid(Type type) => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(BlendMaskBase));

        // --- 路径工具 ---

        private string GetMasksFolder()
        {
            // 简化的路径查找逻辑 (可重用原代码中的 GetPluginRootFolder 和 EnsureFolderPath)
            var root = GetPluginRootFolder();
            var masks = $"{root}/Settings/Masks";
            EnsureFolderPath(masks);
            return masks;
        }

        // 解析插件根目录（包含 "MrPathV2" 的文件夹），支持插件位于 Assets 下任意层级
        private string GetPluginRootFolder()
        {
            try
            {
                // 1. 明确我们要找的类型（用 typeof 替代原来的类型引用）
                var targetType = typeof(LayerMaskAssetService);

                // 2. 查找 AssetDatabase 中所有与该类型名匹配的 MonoScript 资产
                //    t:MonoScript 确保只搜索脚本文件
                var guids = AssetDatabase.FindAssets($"t:MonoScript {targetType.Name}");

                string scriptPath = null;

                // 3. 遍历并验证找到的脚本是否正是目标类
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                    // 安全检查：确保加载的脚本类与目标类型匹配
                    if (script == null || script.GetClass() != targetType) continue;
                    scriptPath = path;
                    break; // 找到即退出
                }

                // 卫语句：如果脚本路径为空，提前返回默认值
                if (string.IsNullOrEmpty(scriptPath))
                    return "Assets/MrPathV2";

                // 4. 执行原有的路径解析逻辑
                scriptPath = scriptPath.Replace('\\', '/');
                var parts = scriptPath.Split('/');

                // 查找名为 "MrPathV2" 的部分，并返回到该部分为止的路径
                for (var i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i], "MrPathV2", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Join("/", parts.Take(i + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"无法定位插件根目录，使用默认值。错误: {ex.Message}");
            }

            // Fallback：默认返回 Assets/MrPathV2
            return "Assets/MrPathV2";
        }
        private static void EnsureFolderPath(string unityFolderPath)
        {
            if (string.IsNullOrEmpty(unityFolderPath))
                return;

            unityFolderPath = unityFolderPath.Replace('\\', '/');
            var parts = unityFolderPath.Split(new[]
            {
                '/'
            }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return;

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
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

        // --- 辅助图标创建 ---

        private static Texture2D CreateNullIcon()
        {
            const int size = 32;
            var nullIcon = new Texture2D(size, size);
            var nullColor = new Color32(0, 0, 0, 42);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = nullColor;
            nullIcon.SetPixels32(pixels);
            nullIcon.Apply();
            return nullIcon;
        }
    }
}
