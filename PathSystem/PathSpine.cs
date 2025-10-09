using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 【最终纯净版】路径骨架数据结构。
    /// 包含了从IPath曲线采样后，用于生成网格所需的所有几何信息。
    /// 这是一个纯粹、凝练的数据容器。
    /// </summary>
    public readonly struct PathSpine
    {
        #region 数据字段 (Data Fields)

        /// <summary>
        /// 骨架上所有点的位置（已吸附地形）。
        /// </summary>
        public readonly Vector3[] points;

        /// <summary>
        /// 骨架上每个点处的切线方向（前进方向）。
        /// </summary>
        public readonly Vector3[] tangents;

        /// <summary>
        /// 骨架上每个点下方的地形表面法线（上方向）。
        /// </summary>
        public readonly Vector3[] surfaceNormals;

        /// <summary>
        /// 骨架上每个点对应的归一化时间戳（0到1）。
        /// </summary>
        public readonly float[] timestamps;

        #endregion

        #region 属性与构造 (Properties & Constructor)

        /// <summary>
        /// 骨架中的顶点总数。
        /// </summary>
        public readonly int VertexCount => points?.Length ?? 0;

        /// <summary>
        /// 唯一的构造函数，用于创建一个完整的路径骨架实例。
        /// </summary>
        public PathSpine(Vector3[] points, Vector3[] tangents, Vector3[] surfaceNormals, float[] timestamps)
        {
            this.points = points;
            this.tangents = tangents;
            this.surfaceNormals = surfaceNormals;
            this.timestamps = timestamps;
        }

        #endregion
    }
}