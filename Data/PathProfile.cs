using System.Collections.Generic;
using UnityEngine;
using PathTool.Data;
/// <summary>
/// 路径配置文件：定义路径的整体行为、生成规则和外观样式
/// </summary>
[CreateAssetMenu(
    fileName = "NewPathProfile",
    menuName = "MrPath/Path Profile",
    order = 50)]
public class PathProfile : ScriptableObject
{
    [Header("曲线与精度")]
    [Tooltip("路径曲线的计算方式")]
    public CurveType curveType = CurveType.Bezier;

    [Tooltip("路径采样精度（单位：米），值越小曲线越平滑但性能消耗越高")]
    [Range(0.1f, 10f)] public float generationPrecision = 1f;

    [Header("地形吸附")]
    [Tooltip("是否让路径自动贴合下方地形高度")]
    public bool snapToTerrain = true;

    [Tooltip("地形高度的影响权重（0=完全不吸附，1=完全贴合）")]
    [Range(0f, 1f)] public float snapStrength = 1f;

    [Tooltip("路径高度的平滑程度（值越大越平滑，0=完全保留地形细节）")]
    [Range(0, 50)] public int heightSmoothness = 5;

    [Header("图层配置")]
    [Tooltip("构成路径的渲染图层列表（按顺序从上到下渲染）")]
    public List<PathLayer> layers = new();

    #region 编辑器辅助（提升配置安全性）
    /// <summary>
    /// 初始化默认图层（新建资产时调用）
    /// </summary>
    private void OnEnable()
    {
        // OnEnable 在多种情况下被调用，包括首次创建和每次加载/克隆
        // 我们只希望在列表确实为空时才添加（例如首次创建）
        // 这比每次都添加更为健壮
        if (layers == null)
        {
            layers = new List<PathLayer>();
        }
        if (layers.Count == 0)
        {
            layers.Add(new PathLayer { name = "Base Layer" });
        }
    }

    /// <summary>
    /// 验证配置有效性（保存时触发）
    /// </summary>
    private void OnValidate()
    {
        // 确保精度为有效值
        generationPrecision = Mathf.Clamp(generationPrecision, 0.1f, 10f);

        // 清理空图层
        layers.RemoveAll(layer => layer == null);

        // 确保至少有一个图层
        if (layers.Count == 0)
        {
            layers.Add(new PathLayer { name = "Base Layer" });
            Debug.LogWarning($"[{name}] 图层列表为空，已自动添加默认图层", this);
        }
    }
    #endregion
}