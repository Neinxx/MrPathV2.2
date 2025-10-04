using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景中路径对象的唯一组件，是路径系统的核心“管理者”。
/// 负责管理当前的曲线数据(IPath)和外观配置(PathProfile)。
/// </summary>
[DisallowMultipleComponent]
public class PathCreator : MonoBehaviour
{
    public event System.Action OnPathChanged;

    #region Public Fields

    /// <summary>
    /// 定义路径应采用的曲线算法。
    /// </summary>
    public enum CurveType { Bezier, CatmullRom }

    [Tooltip ("选择路径使用的曲线算法")]
    public CurveType curveType;

    /// <summary>
    /// 决定路径外观和行为的剖面资产。
    /// </summary>
    [Tooltip ("决定路径外观和行为的剖面资产")]
    public PathProfile profile;

    [Header ("Snapping Settings")]
    [Tooltip ("启用后，路径将尝试吸附到下方的地形")]
    public bool snapToTerrain = true;

    [Tooltip ("路径吸附到地形的强度。0 = 完全不吸附 (空中直线), 1 = 完全贴合地形")]
    [Range (0, 1)]
    public float snapStrength = 1f;

    #endregion

    #region Public Properties

    /// <summary>
    /// 对当前路径策略的引用 (只读)。
    /// [SerializeReference] 允许Unity序列化接口的具体实例。
    /// </summary>
    [SerializeReference]
    public IPath Path;

    /// <summary>
    /// 获取当前路径的段数。
    /// </summary>
    public int NumSegments => Path?.NumSegments ?? 0;

    /// <summary>
    /// 获取当前路径数据点的总数。
    /// </summary>
    public int NumPoints => Path?.Points.Count ?? 0;

    #endregion

    #region Unity Methods

    /// <summary>
    /// 在对象加载时调用，用于进行防御性初始化。
    /// </summary>
    private void Awake ()
    {
        // 确保在任何时候Path对象都存在，防止因序列化问题导致其为null
        EnsurePathExists ();
    }
    /// <summary>
    /// 确保Path实例存在。如果不存在，则根据当前curveType创建一个新的。
    /// </summary>
    private void EnsurePathExists ()
    {
        System.Type targetType = (curveType == CurveType.Bezier) ? typeof (BezierPath) : typeof (CatmullRomPath);
        if (Path == null || Path.GetType () != targetType)
        {
            Path = (IPath) System.Activator.CreateInstance (targetType);
            OnPathChanged?.Invoke ();
        }
    }
    /// <summary>
    /// 当脚本在Inspector中被修改时调用。
    /// 核心职责：确保Path对象存在，并在曲线类型切换时无损转换数据。
    /// </summary>
    private void OnValidate ()
    {
        // 确定目标类型
        System.Type targetType = (curveType == CurveType.Bezier) ? typeof (BezierPath) : typeof (CatmullRomPath);

        // 如果Path为空或类型不匹配，则进行切换
        if (Path == null || Path.GetType () != targetType)
        {
            // 1. 保存旧数据 (如果存在)
            List<Vector3> currentPoints = Path?.Points;

            // 2. 创建新实例
            Path = (IPath) System.Activator.CreateInstance (targetType);

            // 3. 尝试恢复旧数据
            if (currentPoints != null && currentPoints.Count > 0)
            {
                Path.Points = currentPoints;
                // 注意：这里的点转换是“天真”的。例如，从Bézier转到Catmull-Rom会保留所有控制点，
                // 这不是理想情况，但确保了数据不会丢失。未来可以实现更智能的转换逻辑。
            }
        }
    }

    #endregion

    #region Public API Methods

    // --- 为了让外部代码(如EditorTool)保持简洁，我们将常用功能作为转发方法 ---

    /// <summary>
    /// 在世界空间坐标处为路径添加一个新的段落。
    /// </summary>
    public void AddSegment (Vector3 worldPos)
    {
        Path?.AddSegment (worldPos, transform);
        OnPathChanged?.Invoke ();
    }

    /// <summary>
    /// 移动路径上的一个点（锚点或控制点）。
    /// </summary>
    public void MovePoint (int index, Vector3 worldPos)
    {
        Path?.MovePoint (index, worldPos, transform);
        OnPathChanged?.Invoke ();
    }

    /// <summary>
    /// 根据一个0到NumSegments之间的t值，获取曲线上精确的世界坐标点。
    /// </summary>
    public Vector3 GetPointAt (float t)
    {
        // 如果Path为空，返回一个安全值 (原点)
        return Path?.GetPointAt (t, transform) ?? transform.position;
    }

    #endregion

}
