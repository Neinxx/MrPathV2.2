using UnityEngine;
using System.Collections.Generic;
namespace MrPathV2
{
    /// <summary>
    /// 管理路径预览：采样脊线、触发网格生成、更新材质并在场景中绘制。
    /// </summary>
    public class PathPreviewManager : System.IDisposable
    {
        private readonly IPreviewGenerator _generator;
        private readonly PreviewMaterialManager _materialManager;
        private readonly Material _templateMaterial;

        private bool _active = true;
        private bool _dirty = true;

        public PathSpine? LatestSpine { get; private set; }

        public PathPreviewManager(IPreviewGenerator generator, PreviewMaterialManager materialManager, Material templateMaterial)
        {
            _generator = generator;
            _materialManager = materialManager;
            _templateMaterial = templateMaterial;
        }

        public void Dispose()
        {
            _generator?.Dispose();
        }

        public void SetActive(bool active)
        {
            _active = active;
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// 若标记为脏则重采样并启动生成；每帧尝试完成生成并绘制当前预览。
        /// </summary>
        public void RefreshIfDirty(PathCreator creator, IHeightProvider heightProvider)
        {
            if (!_active || creator == null || creator.profile == null) return;

            // 更新材质（始终确保材质与配置同步）
            _materialManager?.UpdateMaterials(creator.profile, _templateMaterial, 0.65f);

            if (_dirty)
            {
                var spine = PathSampler.SamplePath(creator, heightProvider);
                LatestSpine = spine;
                _generator.StartMeshGeneration(spine, creator.profile);
                _dirty = false;
            }

            // 尝试完成 Job，并在场景中绘制当前帧的预览
            _generator.TryFinalizeMesh();
            var mesh = _generator.PreviewMesh;
            if (mesh == null) return;

            var mats = _materialManager?.GetFrameRenderMaterials();
            if (mats == null || mats.Count == 0)
            {
                // 无材质时也允许线框预览（可选：省略）
                return;
            }

            // 直接绘制到场景（世界空间），遵循当前场景视图相机
            foreach (var mat in mats)
            {
                if (mat == null) continue;
                Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
            }
        }

        // 统一通过 IHeightProvider 接口采样，移除不安全的反射解包
    }
}