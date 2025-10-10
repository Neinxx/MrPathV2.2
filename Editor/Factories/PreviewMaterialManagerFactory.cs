using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 预览材质管理器工厂抽象基类，用于创建 PreviewMaterialManager 实例。
    /// </summary>
    public abstract class PreviewMaterialManagerFactory : ScriptableObject
    {
        public abstract PreviewMaterialManager Create();
    }
}