using UnityEngine;
namespace MrPathV2
{

    /// <summary>
    /// 路径预览网格生成器接口。用于解耦具体实现与工具逻辑。
    /// </summary>
    public interface IPreviewGenerator
    {
        /// <summary>
        /// 获取当前已生成的预览网格。
        /// </summary>
        Mesh PreviewMesh { get; }

        /// <summary>
        /// 启动异步网格生成流程。
        /// </summary>
        /// <param name="spine">路径脊线采样数据</param>
        /// <param name="profile">路径外观配置</param>
        void StartMeshGeneration(PathSpine spine, PathProfile profile);

        /// <summary>
        /// 尝试完成生成并返回是否成功完成。
        /// </summary>
        /// <returns>是否完成并已更新 PreviewMesh</returns>
        bool TryFinalizeMesh();

        /// <summary>
        /// 释放内部资源（如Job与Mesh）。
        /// </summary>
        void Dispose();
    }
}