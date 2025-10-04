using UnityEngine;

/// <summary>
/// 路径骨架数据结构。
/// 包含了从IPath曲线采样后，用于生成网格所需的所有几何信息。
/// 这是一个纯粹的数据容器。
/// </summary>
public struct PathSpine
{
    public readonly Vector3[] points;
    public readonly Vector3[] tangents;
    public readonly Vector3[] normals;
    public readonly float[] timestamps;

    public int VertexCount => points?.Length ?? 0;

    public PathSpine (Vector3[] points, Vector3[] tangents, Vector3[] normals, float[] timestamps)
    {
        this.points = points;
        this.tangents = tangents;
        this.normals = normals;
        this.timestamps = timestamps;
    }
}
