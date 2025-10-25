// 文件: Editor/Terrain/GpuPreviewCache.cs

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MrPathV2._2.Editor.Terrain
{
    /// <summary>
    ///     全局 GPU 预览缓存：在「实时预览」模式下保存每个 Terrain 的 RenderTexture（alphamap 纹理数组）。
    ///     设计要点：
    ///     1. 以 Terrain 实例 ID 作为 key，避免场景内多个地形的冲突；
    ///     2. 请求方负责保证 RenderTexture 的尺寸、格式与 Terrain.alphamapTextures 保持一致；
    ///     3. Register 时，如已存在旧的 RT，需要释放旧 RT 避免泄漏；
    ///     4. 在 Domain Reload 或 Editor 退出时，自动清理所有缓存；
    ///     5. 仅在 Editor 环境下生效（Wrap 进 UNITY_EDITOR）。
    /// </summary>
    [InitializeOnLoad]
    public static class GpuPreviewCache
    {
        private static readonly Dictionary<int, RenderTexture> TerrainPreviewMap = new Dictionary<int, RenderTexture>();

        static GpuPreviewCache()
        {
            // 在脚本重载或退出编辑器时清理
            AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
            EditorApplication.quitting += ClearAll;
        }

        /// <summary>
        ///     注册 / 更新指定 Terrain 的预览 RT。
        /// </summary>
        /// <param name="terrain">目标 Terrain</param>
        /// <param name="rt">渲染结果 RenderTexture（Tex2DArray）。此方法会接管其生命周期。</param>
        public static void Register(UnityEngine.Terrain terrain, RenderTexture rt)
        {
            if (terrain == null || rt == null) return;
            var id = terrain.GetInstanceID();
            if (TerrainPreviewMap.TryGetValue(id, out var existing) && existing != null)
            {
                if (existing != rt)
                {
                    if (existing)
                    {
                        existing.Release();
                        Object.DestroyImmediate(existing);
                    }
                    TerrainPreviewMap[id] = rt;
                }
            }
            else
            {
                TerrainPreviewMap[id] = rt;
            }
        }

        /// <summary>
        ///     尝试获取指定 Terrain 的预览 RT。
        /// </summary>
        public static bool TryGet(UnityEngine.Terrain terrain, out RenderTexture rt)
        {
            if (terrain == null)
            {
                rt = null;
                return false;
            }
            return TerrainPreviewMap.TryGetValue(terrain.GetInstanceID(), out rt) && rt;
        }

        /// <summary>
        ///     删除并释放指定 Terrain 的预览 RT。
        /// </summary>
        public static void Clear(UnityEngine.Terrain terrain)
        {
            if (terrain == null) return;
            var id = terrain.GetInstanceID();
            if (TerrainPreviewMap.TryGetValue(id, out var rt) && rt)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            TerrainPreviewMap.Remove(id);
        }

        /// <summary>
        ///     释放所有缓存。
        /// </summary>
        public static void ClearAll()
        {
            foreach (var kvp in TerrainPreviewMap)
            {
                if (kvp.Value) kvp.Value.Release();
            }
            TerrainPreviewMap.Clear();
        }
    }
}
