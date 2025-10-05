using System.Collections.Generic;
using UnityEngine;

// --- 这两个类定义应放在单独的文件中，但为方便查阅暂留此处 ---
// 描述路径发生了何种具体的变化
public enum PathChangeType
{
    PointAdded, // 添加了点
    PointRemoved, // 删除了点
    PointMoved, // 移动了点
    BulkUpdate, // 多个点或结构性发生了变化
    ProfileAssigned // 变更了 Profile 资产
}

// 传递事件所用的参数
public class PathChangedEventArgs : System.EventArgs
{
    public readonly PathChangeType Type;
    public readonly int Index; // 发生变化的点的索引 (如果适用)

    public PathChangedEventArgs (PathChangeType type, int index = -1)
    {
        Type = type;
        Index = index;
    }
}

/// <summary>
/// 【重排版】场景中路径对象的唯一组件，是路径系统的核心“管理者”。
/// 负责管理当前的曲线数据(IPath)和外观配置(PathProfile)。
/// </summary>
[DisallowMultipleComponent]
public class PathCreator : MonoBehaviour
{
    #region 事件 (Events)

    /// <summary>
    /// 当路径数据发生任何变化时触发。
    /// 其他系统（如预览网格控制器）应订阅此事件以作出响应。
    /// </summary>
    public event System.EventHandler<PathChangedEventArgs> PathChanged;

    #endregion

    #region 公共字段 (Public Fields)

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

    #region 公共属性 (Public Properties)

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
    public int NumPoints => Path?.NumPoints ?? 0;

    #endregion

    #region Unity生命周期方法 (Unity Lifecycle Methods)

    private void Awake ()
    {
        EnsurePathExists ();
    }

    /// <summary>
    /// 当脚本在Inspector中被修改时调用。
    /// 核心职责：确保Path对象存在，并在曲线类型切换时无损转换数据。
    /// </summary>
    private void OnValidate ()
    {
        System.Type targetType = (curveType == CurveType.Bezier) ? typeof (BezierPath) : typeof (CatmullRomPath);

        if (Path == null || Path.GetType () != targetType)
        {
            List<Vector3> currentPoints = Path?.Points;

            Path = (IPath) System.Activator.CreateInstance (targetType);

            if (currentPoints != null && currentPoints.Count > 0)
            {
                Path.Points = currentPoints;
            }

            // 切换类型是一种“整体更新”
            PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.BulkUpdate));
        }
    }

    #endregion

    #region 公共接口 (Public API)

    /// <summary>
    /// 在路径末尾添加一个新的段落。
    /// </summary>
    public void AddSegment (Vector3 worldPos)
    {
        Path?.AddSegment (worldPos, transform);
        PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.PointAdded, NumPoints - 1));
    }

    /// <summary>
    /// 移动路径上的一个点（锚点或控制点）。
    /// </summary>
    public void MovePoint (int index, Vector3 worldPos)
    {
        Path?.MovePoint (index, worldPos, transform);
        // [修正] 广播正确的事件类型和索引
        PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.PointMoved, index));
    }

    /// <summary>
    /// 在指定的分段后插入一个新的点。
    /// </summary>
    public void InsertSegment (int segmentIndex, Vector3 worldPos)
    {
        Path?.InsertSegment (segmentIndex, worldPos, transform);
        // [补全] 插入也是一种“添加”，广播事件
        PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.PointAdded));
    }

    /// <summary>
    /// 删除路径上的一个点。
    /// </summary>
    public void DeleteSegment (int index)
    {
        // [补全] 调用接口的实现
        Path?.DeleteSegment (index);
        PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.PointRemoved, index));
    }

    /// <summary>
    /// 根据一个0到NumSegments之间的t值，获取曲线上精确的世界坐标点。
    /// </summary>
    public Vector3 GetPointAt (float t)
    {
        return Path?.GetPointAt (t, transform) ?? transform.position;
    }

    #endregion

    #region 内部逻辑 (Internal Logic)

    /// <summary>
    /// 确保Path实例存在。如果不存在，则根据当前curveType创建一个新的。
    /// </summary>
    private void EnsurePathExists ()
    {
        if (Path == null)
        {
            System.Type targetType = (curveType == CurveType.Bezier) ? typeof (BezierPath) : typeof (CatmullRomPath);
            Path = (IPath) System.Activator.CreateInstance (targetType);
            PathChanged?.Invoke (this, new PathChangedEventArgs (PathChangeType.BulkUpdate));
        }
    }

    #endregion
}
