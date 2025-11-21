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
            var current = EditorPrefs.GetBool(PrefKey, false);
            var next = EditorGUILayout.ToggleLeft("启用 GPU 实时预览（实验性）", current);
            if (next != current)
            {
                EditorPrefs.SetBool(PrefKey, next);
            }
            EditorGUILayout.HelpBox("在支持 ComputeShader 的环境下启用 GPU 线渲染。失败将自动回退到CPU。", MessageType.Info);
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
        static GpuPreviewBootstrap() { /* 保持默认值，不强制覆盖 */ }
    }


// 初始化时同步 EditorPrefs 到运行时开关
    [InitializeOnLoad]
    public static class GpuPreview
    {
        private const string PrefKey = "MrPath_EnableGpuPreview";
        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }
    }
}
