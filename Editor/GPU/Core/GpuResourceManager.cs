using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace __temp.MrPathV2.Editor.GPU.Core
{
    public sealed class GpuResourceManager : IDisposable
    {
        private bool _isInitialized;
        private Dictionary<string, ComputeShader> _computeShaders = new Dictionary<string, ComputeShader>();
        private Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private List<ComputeBuffer> _computeBuffers = new List<ComputeBuffer>();
        private List<RenderTexture> _renderTextures = new List<RenderTexture>();
        private readonly Dictionary<string, ComputeBuffer> _computeBufferMap = new Dictionary<string, ComputeBuffer>();
        private readonly Dictionary<string, RenderTexture> _renderTextureMap = new Dictionary<string, RenderTexture>();

        public void Initialize()
        {
            if (_isInitialized) return;
            // 初始化逻辑，例如加载着色器
            _isInitialized = true;
        }

        public ComputeShader GetComputeShader(string name)
        {
            if (_computeShaders.TryGetValue(name, out var shader))
                return shader;

            var loaded = Resources.Load<ComputeShader>(name);
            if (loaded == null)
            {
                Debug.LogWarning($"[GpuResourceManager] 未找到计算着色器: {name}");
                return null;
            }

            _computeShaders[name] = loaded;
            return loaded;
        }

        public Material GetMaterial(string shaderName)
        {
            if (_materials.TryGetValue(shaderName, out var mat))
                return mat;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[GpuResourceManager] 未找到着色器: {shaderName}");
                return null;
            }

            var newMat = new Material(shader);
            _materials[shaderName] = newMat;
            return newMat;
        }

        public void RegisterComputeBuffer(ComputeBuffer buffer)
        {
            if (buffer != null) _computeBuffers.Add(buffer);
        }

        public void RegisterRenderTexture(RenderTexture rt)
        {
            if (rt != null) _renderTextures.Add(rt);
        }

        public void Dispose()
        {
            foreach (var buf in _computeBuffers)
            {
                if (buf != null) buf.Dispose();
            }
            _computeBuffers.Clear();

            foreach (var rt in _renderTextures)
            {
                if (rt != null) rt.Release();
            }
            _renderTextures.Clear();

            foreach (var mat in _materials.Values)
            {
                if (mat != null) UnityEngine.Object.DestroyImmediate(mat);
            }
            _materials.Clear();

            _computeShaders.Clear();
            _isInitialized = false;
        }

        public ComputeBuffer GetOrCreateComputeBuffer<T>(string key, T[] data) where T : struct
        {
            if (string.IsNullOrEmpty(key)) key = $"Buffer_{Guid.NewGuid()}";
            if (_computeBufferMap.TryGetValue(key, out var buffer))
            {
                // 若大小一致则直接复用
                if (buffer.count == data.Length)
                {
                    buffer.SetData(data);
                    return buffer;
                }
                // 否则重新创建
                buffer.Dispose();
            }

            int stride = Marshal.SizeOf(typeof(T));
            buffer = new ComputeBuffer(data.Length, stride);
            buffer.SetData(data);
            _computeBufferMap[key] = buffer;
            RegisterComputeBuffer(buffer);
            return buffer;
        }

        public RenderTexture GetOrCreateRenderTexture(string key, int width, int height, int volumeDepth = 1, RenderTextureFormat format = RenderTextureFormat.RFloat)
        {
            // 兼容旧接口：不返回创建标记
            return GetOrCreateRenderTextureEx(key, width, height, volumeDepth, format, out _);
        }

        public RenderTexture GetOrCreateRenderTextureEx(string key, int width, int height, int volumeDepth, RenderTextureFormat format, out bool createdNew)
        {
            if (string.IsNullOrEmpty(key)) key = $"RT_{Guid.NewGuid()}";
            var safeDepth = Mathf.Max(volumeDepth, 1);
            createdNew = false;

            if (_renderTextureMap.TryGetValue(key, out var rt))
            {
                // 防御：引用已被销毁（Unity 假 null），移除并重建
                if (!rt)
                {
                    _renderTextureMap.Remove(key);
                    rt = null;
                }
                else if (rt.width == width && rt.height == height && rt.volumeDepth == safeDepth)
                {
                    return rt;
                }

                if (rt != null)
                {
                    rt.Release();
                }
            }

            var descriptor = new RenderTextureDescriptor(width, height, format)
            {
                volumeDepth = safeDepth,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                enableRandomWrite = true
            };
            var newRt = new RenderTexture(descriptor) { name = key };
            newRt.Create();
            _renderTextureMap[key] = newRt;
            RegisterRenderTexture(newRt);
            createdNew = true;
            return newRt;
        }
    }
}