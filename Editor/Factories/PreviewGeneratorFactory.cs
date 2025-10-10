using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 预览生成器工厂抽象基类，用于创建 IPreviewGenerator 实例。
    /// 设计为 ScriptableObject，以便在项目设置中引用资产。
    /// </summary>
    public abstract class PreviewGeneratorFactory : ScriptableObject
    {
        public abstract IPreviewGenerator Create();
    }
}