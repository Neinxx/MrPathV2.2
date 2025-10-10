using UnityEngine;
namespace MrPathV2
{

    /// <summary>
    /// 为地形高度采样提供抽象接口，便于替换实现与测试。
    /// </summary>
    public interface IHeightProvider
    {
        /// <summary>
        /// 获取世界坐标点的地形高度。
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <returns>地形高度值</returns>
        float GetHeight(Vector3 worldPos);

        /// <summary>
        /// 获取世界坐标点的地形法线。
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <returns>地形表面法线向量</returns>
        Vector3 GetNormal(Vector3 worldPos);

        /// <summary>
        /// 标记内部缓存为脏，以便在下次访问时重建。
        /// </summary>
        void MarkAsDirty();

        /// <summary>
        /// 释放资源与事件订阅。
        /// </summary>
        void Dispose();
    }
}