// PathDrawingStyle.cs
using UnityEngine;

/// <summary>
/// 【第一步：铸造风骨】
/// 定义了一个可序列化的、独立的Handle样式。
/// </summary>
[System.Serializable]
public class HandleStyle
{
    [Tooltip("Handle的填充颜色")]
    public Color fillColor = Color.white;
    [Tooltip("Handle的边框颜色")]
    public Color borderColor = Color.black;
    [Tooltip("Handle在场景中的基础大小")]
    [Range(0.01f, 0.5f)]
    public float size = 0.1f;
}

/// <summary>
/// 定义了一条曲线在编辑器中绘制时所需的完整样式集。
/// 它将被直接包含在 PathStrategy 资产中。
/// </summary>
[System.Serializable]
public class PathDrawingStyle
{
    [Header("曲线样式")]
    [Tooltip("曲线的常规颜色")]
    public Color curveColor = Color.white;
    [Tooltip("鼠标悬停在曲线上时的颜色")]
    public Color curveHoverColor = Color.cyan;
    [Tooltip("曲线的粗细")]
    [Range(1f, 10f)]
    public float curveThickness = 4.5f;

    [Header("Handle 样式")]
    [Tooltip("主节点（锚点）的样式")]
    public HandleStyle knotStyle;
    [Tooltip("切线控制点（卫星点）的样式")]
    public HandleStyle tangentStyle;
    [Tooltip("鼠标悬停在任何Handle上时的通用样式")]
    public HandleStyle hoverStyle;
    [Tooltip("按住Shift键预览插入点时的样式")]
    public HandleStyle insertionPreviewStyle;

    [Header("辅助线样式")]
    [Tooltip("连接主节点和切线控制点的虚线颜色")]
    public Color bezierControlLineColor = new Color(1, 1, 1, 0.4f);
}