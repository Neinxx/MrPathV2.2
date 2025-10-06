using Unity.Collections;
using UnityEngine;

/// <summary>
/// 一个Burst兼容的数据结构，用于将PathSpine的核心数据传递给Job。
/// 这个结构体现在是独立的，可以被任何程序集访问。
/// </summary>
public struct PathSpineForJob
{
    [ReadOnly] public NativeArray<Vector3> points;
    [ReadOnly] public NativeArray<Vector3> tangents;
    [ReadOnly] public NativeArray<Vector3> surfaceNormals;
    [ReadOnly] public NativeArray<float> timestamps;

    public PathSpineForJob(NativeArray<Vector3> p, NativeArray<Vector3> t, NativeArray<Vector3> n, NativeArray<float> ts)
    {
        points = p;
        tangents = t;
        surfaceNormals = n;
        timestamps = ts;
    }
}

/// <summary>
/// 一个Burst兼容的数据结构，用于将Profile分段数据传递给Job。
/// </summary>
public struct ProfileSegmentData
{
    public float width;
    public float horizontalOffset;
    public float verticalOffset;
}
