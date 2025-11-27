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
            var current = EditorPrefs.GetBool(PrefKey, false);
            var newValue = EditorGUILayout.ToggleLeft("启用 GPU 实时预览 (实验特性)", current);

            // 提前返回：值未变化则不做任何操作
            if (newValue == current)
            {
                return;
            }

            // 写入并同步到运行时静态开关
            EditorPrefs.SetBool(PrefKey, newValue);
            PreviewMaterialManager.EnableGpuPreview = newValue;
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
            // 读取持久化开关并应用
            var enabled = EditorPrefs.GetBool(GpuPreviewSettingsPrefKey, false);
            PreviewMaterialManager.EnableGpuPreview = enabled;
        }
    }


// 初始化时同步 EditorPrefs 到运行时开关
    [InitializeOnLoad]
    public static class GpuPreview
    {
        private const string PrefKey = "MrPath_EnableGpuPreview";

        static GpuPreview()
        {
            var enabled = EditorPrefs.GetBool(PrefKey, false);
            PreviewMaterialManager.EnableGpuPreview = enabled;
        }

        [MenuItem("MrPath/Enable GPU Preview", priority = 2000)]
        private static void ToggleGpuPreview()
        {
            var newValue = !PreviewMaterialManager.EnableGpuPreview;
            PreviewMaterialManager.EnableGpuPreview = newValue;
            EditorPrefs.SetBool(PrefKey, newValue);
            var state = newValue ? "ON" : "OFF";
            Debug.Log($"[GpuPreviewSettings] GPU Preview toggled: {state}");
        }

        [MenuItem("MrPath/Enable GPU Preview", validate = true)]
        private static bool ToggleGpuPreviewValidate()
        {
            // 在菜单前显示勾选状态
            Menu.SetChecked("MrPath/Enable GPU Preview", PreviewMaterialManager.EnableGpuPreview);
            return true;
        }
    }
}
