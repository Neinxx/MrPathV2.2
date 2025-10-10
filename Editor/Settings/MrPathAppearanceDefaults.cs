// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathAppearanceDefaults.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 负责路径的默认视觉表现，包括Profile和预览材质。
    /// </summary>
    public class MrPathAppearanceDefaults : ScriptableObject
    {
        [Header("默认外观配置")]
        [Tooltip("新创建路径将使用的默认外观配置文件")]
        public PathProfile defaultPathProfile;

        [Header("预览设置")]
        [Tooltip("预览使用的材质模板 (URP Shader)")]
        public Material previewMaterialTemplate;

        [Range(0, 1)]
        [Tooltip("预览透明度，避免遮挡场景交互")]
        public float previewAlpha = 0.5f;
    }
}