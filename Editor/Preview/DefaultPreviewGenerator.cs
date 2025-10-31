using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;
using __temp.MrPathV2.Runtime.Preview;
using UnityEngine;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    ///     默认预览生成器实现，直接使用PreviewMeshController作为底层实现
    /// </summary>
    public class DefaultPreviewGenerator : IPreviewGenerator
    {
        private readonly GeneratorPreviewMeshController m_Controller;

        public DefaultPreviewGenerator()
        {
            m_Controller = new GeneratorPreviewMeshController();
        }

        public DefaultPreviewGenerator(PreviewMaterialManager materialManager)
        {
            m_Controller = new GeneratorPreviewMeshController();
        }

        /// <summary>
        ///     获取当前已生成的预览网格
        /// </summary>
        public Mesh PreviewMesh => m_Controller.PreviewMesh;

        /// <summary>
        ///     启动异步网格生成流程
        /// </summary>
        /// <param name="spine">路径脊线采样数据</param>
        /// <param name="profile">路径外观配置</param>
        public void StartMeshGeneration(PathSpine spine, PathProfile profile)
        {
            m_Controller.StartMeshGeneration(spine, profile);
        }

        /// <summary>
        ///     尝试完成生成并返回是否成功完成
        /// </summary>
        /// <returns>是否完成并已更新 PreviewMesh</returns>
        public bool TryFinalizeMesh() => m_Controller.TryFinalizeMesh();

        /// <summary>
        ///     强制等待Job完成并完成网格生成
        /// </summary>
        /// <returns>是否成功完成网格生成</returns>
        public bool ForceFinalizeMesh() => m_Controller.ForceFinalizeMesh();

        /// <summary>
        ///     释放内部资源（如Job与Mesh）
        /// </summary>
        public void Dispose()
        {
            m_Controller?.Dispose();
        }
    }
}
