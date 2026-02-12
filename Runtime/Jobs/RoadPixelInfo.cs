namespace MrPathV2.Runtime.Jobs
{
    /// <summary>
    ///     缓存每个地形像素与路径相关的信息，用于两阶段 Job
    /// </summary>
    public struct RoadPixelInfo
    {
        public float NormalizedDist; // 0..1 (中心到边缘)
        public float PathProgress; // 0..1 (起点到终点)
        public bool IsInside; // 是否在道路轮廓内
    }
}
