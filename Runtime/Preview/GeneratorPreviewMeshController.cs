using System;
using MrPathV2.Runtime.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    ///     基于 <see cref="RoadPreviewMeshGenerator" /> 的轻量级预览网格控制器。
    ///     负责：
    ///     1. 持有并调度 RoadPreviewMeshGenerator 进行数据生成;
    ///     2. 将生成的 NativeArray 数据上传到 UnityEngine.Mesh;
    ///     不再自行管理 JobData，确保与数据层解耦。
    /// </summary>
    public sealed class GeneratorPreviewMeshController : IDisposable
    {

        private enum MeshGenerationState
        {
            Idle,
            Generating,
            Ready,
            Failed
        }

        private readonly RoadPreviewMeshGenerator _meshGenerator;
        private readonly TempIndicesManager _tempIndicesManager;
        private bool _disposed;

        public GeneratorPreviewMeshController()
        {
            _meshGenerator = new RoadPreviewMeshGenerator();
            _tempIndicesManager = new TempIndicesManager();

            PreviewMesh = new Mesh
            {
                name = "Path Preview Mesh"
            };
            PreviewMesh.MarkDynamic();
            PreviewMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        private MeshGenerationState State { get; set; } = MeshGenerationState.Idle;

        /// <summary>
        ///     公开 Mesh 供外部渲染
        /// </summary>
        public Mesh PreviewMesh { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            _meshGenerator?.Dispose();
            _tempIndicesManager?.Dispose();
            if (PreviewMesh)
            {
                Object.DestroyImmediate(PreviewMesh);
                PreviewMesh = null;
            }
            _disposed = true;
        }

        private bool ApplyMeshData()
        {
            //  Debug.Log("[GeneratorPreviewMeshController] ApplyMeshData invoked");
            if (_meshGenerator.State != RoadPreviewMeshGenerator.GenerationState.Ready)
            {
                State = MeshGenerationState.Failed;
                return false;
            }

            var vertices = _meshGenerator.Vertices;
            var uvs = _meshGenerator.UVs;
            var colors = _meshGenerator.Colors;
            var indices = _meshGenerator.Indices;

            if (!vertices.IsCreated || !indices.IsCreated || vertices.Length == 0 || indices.Length == 0)
            {
                ErrorHandler.LogError($"[GeneratorPreviewMeshController] Invalid mesh data  - verticesCreated={vertices.IsCreated}, indicesCreated={indices.IsCreated}, vertexCount={vertices.Length}, indexCount={indices.Length}");
                State = MeshGenerationState.Failed;
                return false;
            }

            try
            {
                PreviewMesh.Clear(false);

                var vertexCount = vertices.Length;
                var indexCount = indices.Length;
                var indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                PreviewMesh.indexFormat = indexFormat;

                var layout = new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 2)
                };
                PreviewMesh.SetVertexBufferParams(vertexCount, layout);

                PreviewMesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);
                PreviewMesh.SetVertexBufferData(uvs, 0, 0, vertexCount, 1, MeshUpdateFlags.DontRecalculateBounds);
                PreviewMesh.SetVertexBufferData(colors, 0, 0, vertexCount, 2, MeshUpdateFlags.DontRecalculateBounds);

                PreviewMesh.SetIndexBufferParams(indexCount, indexFormat);

                if (indexFormat == IndexFormat.UInt16)
                {
                    var temp = _tempIndicesManager.GetOrCreateIndices(indexCount);
                    _tempIndicesManager.FillIndices(indices, indexCount);
                    PreviewMesh.SetIndexBufferData(temp, 0, 0, indexCount, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                }
                else
                {
                    PreviewMesh.SetIndexBufferData(indices, 0, 0, indexCount, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                }

                PreviewMesh.subMeshCount = 1;
                var subDesc = new SubMeshDescriptor(0, indexCount)
                {
                    vertexCount = vertexCount
                };
                PreviewMesh.SetSubMesh(0, subDesc, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

                PreviewMesh.RecalculateBounds();
                //   Debug.Log($"[GeneratorPreviewMeshController] Mesh bounds computed center={b.center}, size={b.size}, vertexCount={vertexCount}, firstVertex={vertices[0]}");
                PreviewMesh.UploadMeshData(false);

                // 完成后重置状态

                DisposeCurrentJob();
                State = MeshGenerationState.Ready; // 保持 Ready 状态，避免重复刷新
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"GeneratorPreviewMeshController 应用网格数据失败: {ex.Message}");
                State = MeshGenerationState.Failed;
                return false;
            }
        }

        private void DisposeCurrentJob()
        {
            _meshGenerator?.Release();
        }

        #region API 与旧 PreviewMeshController 保持一致

        public void StartMeshGeneration(PathSpine spine, PathProfile profile)
        {
            // Debug.Log("[GeneratorPreviewMeshController] StartMeshGeneration invoked");
            // 先重置
            DisposeCurrentJob();

            // 当开始一次新的生成任务时，重置已应用标记，确保后续可以再次上传网格数据
            _meshApplied = false;

            if (!_meshGenerator.Start(spine, profile))
            {
                State = MeshGenerationState.Failed;
                PreviewMesh.Clear();
                return;
            }

            State = MeshGenerationState.Generating;
        }

        private bool _meshApplied;
        public bool TryFinalizeMesh()
        {
            // 如果之前已成功应用网格，直接返回 true
            if (_meshApplied)
                return true;

            // 若仍在生成阶段，则尝试完成 Job
            if (State == MeshGenerationState.Generating)
            {
                if (!_meshGenerator.TryComplete())
                    return false; // 仍未完成

                // 生成已完成，将状态同步为 Ready
                if (_meshGenerator.State == RoadPreviewMeshGenerator.GenerationState.Ready)
                    State = MeshGenerationState.Ready;
            }

            // 仅当状态就绪时才尝试应用网格
            if (State != MeshGenerationState.Ready)
                return false;

            // 应用网格数据并记录结果
            _meshApplied = ApplyMeshData();
            return _meshApplied;
        }

        public bool ForceFinalizeMesh()
        {
            if (State == MeshGenerationState.Generating && !_meshGenerator.ForceComplete())
            {
                State = MeshGenerationState.Failed;
                return false;
            }
            State = MeshGenerationState.Ready;
            return ApplyMeshData();
        }

        #endregion
    }
}
