

/// <summary>
/// 描述路径发生了何种具体的变化。
/// </summary>
public enum PathChangeType
{
    PointAdded,
    PointRemoved,
    PointMoved,
    BulkUpdate,
    ProfileAssigned
}

/// <summary>
/// 传递路径变化事件所用的参数。
/// </summary>
public class PathChangedEventArgs : System.EventArgs
{
    public readonly PathChangeType Type;
    public readonly int Index;

    public PathChangedEventArgs(PathChangeType type, int index = -1)
    {
        Type = type;
        Index = index;
    }
}