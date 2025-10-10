using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 默认实现：创建新的 PreviewMaterialManager。
    /// </summary>
    [CreateAssetMenu(fileName = "Default Preview Material Manager Factory", menuName = "MrPath/Factories/Preview Material Manager Factory")]
    public class DefaultPreviewMaterialManagerFactory : PreviewMaterialManagerFactory
    {
        public override PreviewMaterialManager Create()
        {
            return new PreviewMaterialManager();
        }
    }
}