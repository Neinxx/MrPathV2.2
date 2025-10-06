// PathCreator.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PathData = MrPath.PathData;

[DisallowMultipleComponent]
public class PathCreator : MonoBehaviour
{
    public event System.EventHandler<PathChangedEventArgs> PathChanged;

    [Tooltip("决定路径一切外观与行为的剖面资产")]
    public PathProfile profile;

    [SerializeReference]
    public IPath Path;

    public int NumSegments => Path?.NumSegments ?? 0;
    public int NumPoints => Path?.NumPoints ?? 0;

    // --- 【【【 核心架构变更 】】】 ---
    // 不再有IPath接口，而是直接持有通用数据容器
    // [SerializeField]
    // public PathData pathData = new PathData();

    // // 当前使用的曲线“法则”（由Profile决定）
    // public CurveType curveType { get; private set; }

    // public int NumPoints => pathData.KnotCount;
    // public int NumSegments => pathData.SegmentCount;
    private void OnValidate()
    {
        EnsurePathImplementationMatchesProfile(false);
    }

    // 【大师重构版】GetPoint现在变得极其纯粹，不再需要关心Path的具体类型
    public Vector3 GetPoint(int i) => Path?.GetPoint(i, transform) ?? transform.position;

    public void AddSegment(Vector3 worldPos)
    {
        EnsurePathImplementationMatchesProfile();
        Path?.AddSegment(worldPos, transform);
        NotifyPathChanged(PathChangeType.PointAdded, NumPoints - 1);
    }

    public void MovePoint(int index, Vector3 worldPos)
    {
        Path?.MovePoint(index, worldPos, transform);
        NotifyPathChanged(PathChangeType.PointMoved, index);
    }

    public void InsertSegment(int segmentIndex, Vector3 worldPos)
    {
        Path?.InsertSegment(segmentIndex, worldPos, transform);
        NotifyPathChanged(PathChangeType.BulkUpdate);
    }

    public void DeleteSegment(int index)
    {
        Path?.DeleteSegment(index);
        NotifyPathChanged(PathChangeType.PointRemoved, index);
    }

    public void ClearSegments()
    {
        Path?.ClearSegments();
        NotifyPathChanged(PathChangeType.BulkUpdate);
    }

    public Vector3 GetPointAt(float t) => Path?.GetPointAt(t, transform) ?? transform.position;

    public void NotifyPathChanged(PathChangeType type, int index = -1)
    {
        PathChanged?.Invoke(this, new PathChangedEventArgs(type, index));
    }

    /// <summary>
    /// 【大师重构版】确保Path实例存在，并且其类型与Profile中定义的类型一致。
    /// </summary>
    /// <param name="notify">是否在路径被重建时，发出全局通知。</param>
    /// <summary>
    /// 【【【 最终核心修正：使用正确的迁移通道 】】】
    /// 确保Path实例存在，并且其类型与Profile中定义的类型一致。
    /// </summary>
    public void EnsurePathImplementationMatchesProfile(bool notify = true)
    {
        if (profile == null)
        {
            if (Path != null) Path = null;
            return;
        }

        System.Type targetType = (profile.curveType == PathTool.Data.CurveType.CatmullRom)
            ? typeof(CatmullRomPath)
            : typeof(BezierPath);

        if (Path == null || Path.GetType() != targetType)
        {
            // --- 关键修正：不再使用旧的 .Points 属性 ---
            // 1. 调用“通用导出”接口，获取纯净的锚点列表
            List<Vector3> currentPoints = Path?.GetPointsForMigration();

            // 2. 创建新的路径实例
            Path = (IPath)System.Activator.CreateInstance(targetType);

            // 3. 调用“通用导入”接口，让新路径根据锚点列表智能地重建自身结构
            if (currentPoints != null && currentPoints.Count > 0)
            {
                Path.SetPointsFromMigration(currentPoints);
            }

            EditorUtility.SetDirty(this);

            if (notify)
            {
                NotifyPathChanged(PathChangeType.BulkUpdate);
            }
        }
    }
}