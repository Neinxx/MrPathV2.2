using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 高度提供者工厂抽象基类，用于创建 IHeightProvider 实例。
    /// </summary>
    public abstract class HeightProviderFactory : ScriptableObject
    {
        public abstract IHeightProvider Create();
    }
}