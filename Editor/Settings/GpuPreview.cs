using System.Collections.Generic;
using MrPathV2.Editor.Preview;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Settings
{
    /// <summary>
    ///     项目设置：GPU 预览开关
    ///     - 单一职责：仅负责开关的读取/写入与UI展示
    ///     - 提前返回：在GUI中避免不必要的状态更新
    ///     - 使用Unity API：SettingsProvider、EditorPrefs
    /// </summary>
    static class GpuPreviewSettings
    {
        private const string PrefKey = "MrPath_EnableGpuPreview";

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            // 路径放在项目设置树下，便于发现
            var provider = new SettingsProvider("Project/MrPath/GPU Preview", SettingsScope.Project)
            {
                label = "GPU Preview",
                guiHandler = DrawGUI,
                keywords = new HashSet<string>(new[]
                {
                    "GPU", "Preview", "MrPath"
                })
            };
            return provider;
        }

        /// <summary>
        ///     绘制设置面板GUI
        /// </summary>
        private static void DrawGUI(string searchContext)
        {
            // 从 EditorPrefs 读取当前值
            var current = false; // 强制关闭，保持UI与状态一致
            EditorGUILayout.ToggleLeft("启用 GPU 实时预览 (未开放)", current);
            EditorGUILayout.HelpBox("GPU预览未开放，当前版本仅支持CPU预览。", MessageType.Info);
            // 清理偏好键（若存在旧值）
            if (EditorPrefs.GetBool(PrefKey, false))
                EditorPrefs.SetBool(PrefKey, false);
        }
    }

    /// <summary>
    ///     编辑器启动时同步 GPU 预览开关
    /// </summary>
    [InitializeOnLoad]
    static class GpuPreviewBootstrap
    {

        // 将键名集中到一个地方，避免魔法字符串散落
        private const string GpuPreviewSettingsPrefKey = "MrPath_EnableGpuPreview";
        static GpuPreviewBootstrap()
        {
            // GPU预览未开放：强制关闭并清理偏好
            if (EditorPrefs.GetBool(GpuPreviewSettingsPrefKey, false))
                EditorPrefs.SetBool(GpuPreviewSettingsPrefKey, false);
        }
    }


// 初始化时同步 EditorPrefs 到运行时开关
    [InitializeOnLoad]
    public static class GpuPreview
    {
        private const string PrefKey = "MrPath_EnableGpuPreview";

        static GpuPreview()
        {
            // 初始化时强制关闭
            if (EditorPrefs.GetBool(PrefKey, false))
                EditorPrefs.SetBool(PrefKey, false);
        }
        // 移除菜单项：避免误导操作。
    }
}
