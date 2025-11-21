using UnityEngine;

namespace MrPathV2.Runtime.Core.Resources
{
    public interface IResourceProvider
    {
        T Load<T>(string path) where T : Object;
    }

    public static class ResourceProvider
    {
        private static IResourceProvider _instance = new DefaultResourceProvider();
        public static IResourceProvider Instance
        {
            get => _instance;
            set => _instance = value ?? new DefaultResourceProvider();
        }

        public static T LoadAsset<T>(string path) where T : Object => Instance.Load<T>(path);
        public static ComputeShader LoadComputeShader(string name) => Instance.Load<ComputeShader>(name);
    }

    internal sealed class DefaultResourceProvider : IResourceProvider
    {
        public T Load<T>(string path) where T : Object => UnityEngine.Resources.Load<T>(path);
    }
}
