// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathSceneUISettings.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 专门管理场景视图中工具窗口的布局和行为。
    /// </summary>
    public class MrPathSceneUISettings : ScriptableObject
    {
        [Header("场景UI窗口设置")]
        [Tooltip("工具窗口宽度")]
        public float sceneUiWindowWidth = 180f;

        [Tooltip("工具窗口高度")]
        public float sceneUiWindowHeight = 110f;

        [Tooltip("Scene视图右侧边距")]
        public float sceneUiRightMargin = 15f;

        [Tooltip("Scene视图底部边距")]
        public float sceneUiBottomMargin = 40f;

        [Header("快捷键设置")]
        [Tooltip("是否启用默认快捷键 Ctrl+W/Ctrl+P（若自定义操作，可关闭）")]
        public bool enableDefaultShortcuts = true;
    }
}