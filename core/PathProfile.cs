
using System.Collections.Generic;
using MrPathV2;
using UnityEngine;


/// <summary>
/// 【第五步：确认并解析】
/// 路径配置文件：定义路径的整体行为、生成规则和外观样式。
/// (大师确认：此脚本结构完美，已符合最终架构要求。)
/// </summary>
[CreateAssetMenu(
    fileName = "NewPathProfile",
    menuName = "MrPath/Path Profile",
    order = 50)]
public class PathProfile : ScriptableObject
{
    [Header("曲线与精度")]
    [Tooltip("路径曲线的计算方式")]
    // (大师确认：使用枚举提供便捷的下拉菜单，正确。)
    public CurveType curveType = CurveType.Bezier;

    [Tooltip("路径采样精度（单位：米），值越小曲线越平滑但性能消耗越高")]
    [Range(0.1f, 10f)] public float generationPrecision = 1f;
    [Header("地形修改设置 (Terrain Modification)")]


    [Header("网格生成设置 (Mesh Generation)")]
    [Tooltip("保持路面水平，不允许左右倾斜。适用于公路、铁路等。")]
    public bool forceHorizontal = true; // 【新增】防倾斜开关
    [Header("地形吸附")]
    [Tooltip("是否让路径自动贴合下方地形高度")]
    public bool snapToTerrain = true;
    [Tooltip("路面边缘与原始地形混合的过渡带宽度(米)。")]
    [Range(0f, 10f)]
    public float edgeFalloffWidth = 1.5f; // 【新增】边缘衰减宽度
                                          // 【新增/修改】以下是路径吸附时的平滑参数
    [Tooltip("路径高度平滑的计算范围（单位：顶点数）。值越大，平滑范围越广。")]
    [Range(0, 10)]
    public int heightSmoothRange = 2; // 对应之前的 heightSmoothness

    [Tooltip("地形高度的影响权重（0=完全不吸附，1=完全贴合）")]
    [Range(0f, 1f)] public float snapStrength = 1f;

    [Tooltip("路径高度的平滑程度（值越大越平滑，0=完全保留地形细节）")]
    [Range(0, 50)] public int heightSmoothness = 5;

    [Header("图层配置")]
    [Tooltip("构成路径的渲染图层列表（按顺序从上到下渲染）")]
    public List<PathLayer> layers = new();

    #region 编辑器辅助（提升配置安全性）
    // (大师赞许：优秀的编辑器安全校验，体现了专业工具的素养。)


    /// <summary>
    /// 初始化默认图层（新建资产时调用）
    /// </summary>
    private void OnEnable()
    {
        layers ??= new List<PathLayer>();
        if (layers.Count == 0)
        {
            var defaultLayer = new PathLayer { name = "Base Layer", width = 2.0f };
            layers.Add(defaultLayer);
        }
    }


    /// <summary>
    /// 验证配置有效性（保存时触发）
    /// </summary>
    private void OnValidate()
    {
        generationPrecision = Mathf.Clamp(generationPrecision, 0.1f, 10f);
        layers.RemoveAll(layer => layer == null);

        if (layers.Count == 0)
        {
            layers.Add(new PathLayer { name = "Base Layer" });
            Debug.LogWarning($"[{name}] 图层列表为空，已自动添加默认图层", this);
        }
    }
    #endregion
}