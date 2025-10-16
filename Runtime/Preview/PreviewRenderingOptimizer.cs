using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;

namespace MrPathV2
{
    /// <summary>
    /// 预览渲染优化器：提供批量渲染、GPU实例化和渲染状态缓存
    /// </summary>
    public class PreviewRenderingOptimizer : System.IDisposable
    {
        private struct RenderBatch
        {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] matrices;
            public int count;
        }

        private readonly List<RenderBatch> _renderBatches;
        private readonly Dictionary<int, MaterialPropertyBlock> _propertyBlocks;
        private readonly MaterialPropertyBlock _sharedPropertyBlock;
        
        // GPU实例化支持
        private Matrix4x4[] _instanceMatrices;
        private Vector4[] _instanceColors;
        private const int MAX_INSTANCES_PER_BATCH = 1023; // Unity限制

        // 渲染状态缓存
        private Camera _lastCamera;
        private Plane[] _frustumPlanes;
        private bool _frustumPlanesValid = false;

        public PreviewRenderingOptimizer()
        {
            _renderBatches = new List<RenderBatch>();
            _propertyBlocks = new Dictionary<int, MaterialPropertyBlock>();
            _sharedPropertyBlock = new MaterialPropertyBlock();
            
            _instanceMatrices = new Matrix4x4[MAX_INSTANCES_PER_BATCH];
            _instanceColors = new Vector4[MAX_INSTANCES_PER_BATCH];
        }

        /// <summary>
        /// 添加渲染项到批次中
        /// </summary>
        public void AddRenderItem(Mesh mesh, Material material, Matrix4x4 matrix, Color color = default)
        {
            if (mesh == null || material == null) return;

            // 查找或创建批次
            int batchIndex = FindOrCreateBatch(mesh, material);
            var batch = _renderBatches[batchIndex];
            
            if (batch.count < MAX_INSTANCES_PER_BATCH)
            {
                batch.matrices[batch.count] = matrix;
                if (color != default)
                {
                    _instanceColors[batch.count] = new Vector4(color.r, color.g, color.b, color.a);
                }
                batch.count++;
                _renderBatches[batchIndex] = batch;
            }
        }

        /// <summary>
        /// 执行批量渲染
        /// </summary>
        public void ExecuteBatchedRender(Camera camera = null)
        {
            if (_renderBatches.Count == 0) return;

            UpdateFrustumPlanes(camera);

            foreach (var batch in _renderBatches)
            {
                if (batch.count == 0) continue;

                // 视锥体剔除
                int visibleCount = PerformFrustumCulling(batch);
                if (visibleCount == 0) continue;

                // 执行GPU实例化渲染
                if (visibleCount == 1)
                {
                    // 单个实例直接渲染
                    Graphics.DrawMesh(batch.mesh, batch.matrices[0], batch.material, 0, camera);
                }
                else
                {
                    // 批量实例化渲染
                    Graphics.DrawMeshInstanced(
                        batch.mesh, 
                        0, 
                        batch.material, 
                        batch.matrices, 
                        visibleCount, 
                        _sharedPropertyBlock, 
                        UnityEngine.Rendering.ShadowCastingMode.Off, 
                        false, 
                        0, 
                        camera
                    );
                }
            }
        }

        /// <summary>
        /// 清空所有渲染批次
        /// </summary>
        public void ClearBatches()
        {
            foreach (var batch in _renderBatches)
            {
                // 重置计数但保留数组以避免重新分配
                var resetBatch = batch;
                resetBatch.count = 0;
            }
            _renderBatches.Clear();
        }

        /// <summary>
        /// 设置全局渲染属性
        /// </summary>
        public void SetGlobalProperty(string propertyName, float value)
        {
            _sharedPropertyBlock.SetFloat(propertyName, value);
        }

        public void SetGlobalProperty(string propertyName, Vector4 value)
        {
            _sharedPropertyBlock.SetVector(propertyName, value);
        }

        public void SetGlobalProperty(string propertyName, Color value)
        {
            _sharedPropertyBlock.SetColor(propertyName, value);
        }

        public void SetGlobalProperty(string propertyName, Texture value)
        {
            _sharedPropertyBlock.SetTexture(propertyName, value);
        }

        /// <summary>
        /// 查找或创建渲染批次
        /// </summary>
        private int FindOrCreateBatch(Mesh mesh, Material material)
        {
            // 查找现有批次
            for (int i = 0; i < _renderBatches.Count; i++)
            {
                var batch = _renderBatches[i];
                if (batch.mesh == mesh && batch.material == material && batch.count < MAX_INSTANCES_PER_BATCH)
                {
                    return i;
                }
            }

            // 创建新批次
            var newBatch = new RenderBatch
            {
                mesh = mesh,
                material = material,
                matrices = new Matrix4x4[MAX_INSTANCES_PER_BATCH],
                count = 0
            };
            
            _renderBatches.Add(newBatch);
            return _renderBatches.Count - 1;
        }

        /// <summary>
        /// 更新视锥体平面
        /// </summary>
        private void UpdateFrustumPlanes(Camera camera)
        {
            if (camera != _lastCamera)
            {
                _lastCamera = camera;
                _frustumPlanesValid = false;
            }

            if (!_frustumPlanesValid && camera != null)
            {
                _frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                _frustumPlanesValid = true;
            }
        }

        /// <summary>
        /// 执行视锥体剔除
        /// </summary>
        private int PerformFrustumCulling(RenderBatch batch)
        {
            if (_frustumPlanes == null || batch.mesh == null)
            {
                return batch.count;
            }

            int visibleCount = 0;
            var meshBounds = batch.mesh.bounds;

            for (int i = 0; i < batch.count; i++)
            {
                // 变换边界框到世界空间
                var worldBounds = TransformBounds(meshBounds, batch.matrices[i]);
                
                // 视锥体测试
                if (GeometryUtility.TestPlanesAABB(_frustumPlanes, worldBounds))
                {
                    // 如果不是第一个可见项，需要移动到前面
                    if (visibleCount != i)
                    {
                        batch.matrices[visibleCount] = batch.matrices[i];
                        _instanceColors[visibleCount] = _instanceColors[i];
                    }
                    visibleCount++;
                }
            }

            return visibleCount;
        }

        /// <summary>
        /// 变换边界框到世界空间
        /// </summary>
        private Bounds TransformBounds(Bounds localBounds, Matrix4x4 transform)
        {
            var center = transform.MultiplyPoint3x4(localBounds.center);
            var extents = localBounds.extents;
            
            // 计算变换后的边界框
            var newExtents = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                newExtents[i] = Mathf.Abs(transform[i, 0] * extents.x) +
                               Mathf.Abs(transform[i, 1] * extents.y) +
                               Mathf.Abs(transform[i, 2] * extents.z);
            }
            
            return new Bounds(center, newExtents * 2);
        }

        public void Dispose()
        {
            ClearBatches();
            _propertyBlocks?.Clear();
            _instanceMatrices = null;
            _instanceColors = null;
        }
    }
}