using System;
using Unity.Collections;
using MrPathV2.Memory;

namespace MrPathV2
{
    /// <summary>
    /// 临时索引数组管理器，专门处理异步网格操作中的 NativeArray 生命周期
    /// </summary>
    public sealed class TempIndicesManager : IDisposable
    {
        private MemoryOwner<NativeArray<ushort>> _currentIndicesOwner;
        private bool _disposed;

        /// <summary>
        /// 获取或创建指定大小的临时索引数组
        /// </summary>
        /// <param name="indexCount">索引数量</param>
        public NativeArray<ushort> GetOrCreateIndices(int indexCount)
        {
            ThrowIfDisposed();

            // 如果当前数组大小不同，释放后重建
            if (_currentIndicesOwner != null &&
                _currentIndicesOwner.Collection.IsCreated &&
                _currentIndicesOwner.Collection.Length != indexCount)
            {
                ReleaseCurrentIndices();
            }

            // 若尚未创建，则进行分配
            if (_currentIndicesOwner == null || !_currentIndicesOwner.Collection.IsCreated)
            {
                _currentIndicesOwner = UnifiedMemory.Instance.RentNativeArray<ushort>(indexCount, Allocator.Persistent);
            }

            return _currentIndicesOwner.Collection;
        }

        /// <summary>
        /// 将 <see cref="NativeArray{int}"/> 数据复制到当前 ushort 索引缓冲
        /// </summary>
        public void FillIndices(NativeArray<int> sourceIndices, int count)
        {
            ThrowIfDisposed();

            if (_currentIndicesOwner == null || !_currentIndicesOwner.Collection.IsCreated)
                throw new InvalidOperationException("必须先调用 GetOrCreateIndices 创建数组");

            var dest = _currentIndicesOwner.Collection;

            if (count > dest.Length || count > sourceIndices.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "索引数量超出数组范围");

            for (int i = 0; i < count; i++)
            {
                dest[i] = (ushort)sourceIndices[i];
            }
        }

        /// <summary>
        /// 当前索引数组（只读）
        /// </summary>
        public NativeArray<ushort> CurrentIndices
        {
            get
            {
                ThrowIfDisposed();
                return _currentIndicesOwner != null ? _currentIndicesOwner.Collection : default;
            }
        }

        /// <summary>
        /// 是否存在有效索引缓存
        /// </summary>
        public bool HasValidIndices => !_disposed &&
                                        _currentIndicesOwner != null &&
                                        _currentIndicesOwner.Collection.IsCreated;

        /// <summary>
        /// 释放当前索引数组
        /// </summary>
        public void ReleaseCurrentIndices()
        {
            _currentIndicesOwner?.Dispose();
            _currentIndicesOwner = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            ReleaseCurrentIndices();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TempIndicesManager));
        }
    }
}