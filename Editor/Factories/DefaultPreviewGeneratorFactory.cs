using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 默认实现：使用 PreviewMeshControllerAdapter 包装 PreviewMeshController。
    /// </summary>
    [CreateAssetMenu(fileName = "Default Preview Generator Factory", menuName = "MrPath/Factories/Preview Generator Factory")]
    public class DefaultPreviewGeneratorFactory : PreviewGeneratorFactory
    {
        public override IPreviewGenerator Create()
        {
            return new PreviewMeshControllerAdapter(new PreviewMeshController());
        }
    }
}