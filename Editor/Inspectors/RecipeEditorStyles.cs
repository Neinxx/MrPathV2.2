// ***** RecipeEditorStyles.cs *****

using UnityEditor;
using UnityEngine;

/// <summary>
/// 存储 StylizedRoadRecipeEditor 的共享UI样式、图标和本地化文本。
/// </summary>
internal static class RecipeEditorStyles
{
    // --- 文本与标签 ---
    public static readonly string Title = "风格化道路配方";
    public static readonly string InfoHelpBox = "此配方通过层叠混合来定义道路的最终材质。每个图层由一个地形层(颜色)和一个遮罩(形状)组成，它们自下而上进行混合。";
    public static readonly string LayersHeader = "混合图层 (Passes)";
    public static readonly string PreviewHeader = "最终效果预览";
    public static readonly string PreviewHelpBoxChannels = "白线为道路中心。RGBA通道分别代表前四个图层的权重分布。";
    public static readonly string PreviewHelpBoxCombined = "白线为道路中心。此模式展示了所有图层颜色与遮罩混合后的最终效果。";

    // --- 图标 ---
    public static readonly GUIContent[] ChannelIcons;
    public static readonly GUIContent[] PreviewModeIcons;

    // --- 样式 ---
    public static readonly GUIStyle headerLabelStyle;
    public static readonly GUIStyle previewBoxStyle;

    static RecipeEditorStyles()
    {
        headerLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            margin = new RectOffset(5, 5, 5, 10)
        };
        
        previewBoxStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 10, 10)
        };

        // 初始化工具栏图标
        PreviewModeIcons = new GUIContent[]
        {
            EditorGUIUtility.IconContent("CustomSorting", "通道模式|分别预览前4个图层的权重"),
            EditorGUIUtility.IconContent("PreTextureRGB", "合并模式|预览所有图层混合后的最终颜色")
        };

        ChannelIcons = new GUIContent[]
        {
            EditorGUIUtility.IconContent("SceneViewRGB", "RGB|预览所有通道的权重"),
            EditorGUI_IconContent_WithText("SceneViewRed", "R"),
            EditorGUI_IconContent_WithText("SceneViewGreen", "G"),
            EditorGUI_IconContent_WithText("SceneViewBlue", "B"),
            EditorGUI_IconContent_WithText("SceneViewAlpha", "A")
        };
    }
    
    // 辅助方法，因为Unity默认的IconContent会忽略文本，我们手动创建一个带文本的
    private static GUIContent EditorGUI_IconContent_WithText(string name, string text)
    {
        var content = EditorGUIUtility.IconContent(name);
        content.text = text;
        return content;
    }
}