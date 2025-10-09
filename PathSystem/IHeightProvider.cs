using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 抽象地形高度/法线提供者接口，用于解耦采样与具体实现。
    /// </summary>
    public interface IHeightProvider:System.IDisposable
    {
        float GetHeight(Vector3 worldPos);
        Vector3 GetNormal(Vector3 worldPos);
    }
}