using UnityEngine;

public interface IPath
{
    // 获取/设置所有数据点 (局部空间)
    System.Collections.Generic.List<Vector3> Points { get; set; }

    // 总段数
    int NumSegments { get; }
    int NumPoints { get; }

    // 在曲线上 t 位置获取一个点 (t 从 0 到 NumSegments)
    Vector3 GetPointAt (float t, Transform owner);

    // 添加一个新的路径段
    void AddSegment (Vector3 newPointWorldPos, Transform owner);

    // 移动一个点
    void MovePoint (int i, Vector3 newPointWorldPos, Transform owner);

    //让曲线类负责绘制自己的编辑器UI
    void DrawEditorHandles (PathCreator creator);

    // (未来可扩展其他方法，如获取切线、法线等)
}
