using System.Collections.Generic;
using UnityEngine;
using MrPathV2.Runtime.Preview;

namespace MrPathV2.Editor.Preview
{
    // 负责在预览阶段将多层纹理打包为 Texture2DArray，并绑定到材质。
    // - 自动为缺失纹理填充白色占位，以保证数组层数与配置一致
    // - 统一到指定尺寸，不同尺寸纹理使用 GPU Blit 进行缩放
    // - 尽量使用 Graphics.CopyTexture，必要时回退 ReadPixels
    internal class PreviewTextureArrayManager
    {
        private Texture2DArray _arrayCache;
        private int _cachedWidth;
        private int _cachedHeight;
        private int _cachedSlices;
        private string _cachedKey;

        public bool TryCreateAndBindTextureArray(
            Material target,
            IList<Texture2D> textures,
            int targetWidth,
            int targetHeight,
            string cacheKey)
        {
            if (target == null || textures == null || textures.Count == 0)
                return false;

            // 若未提供尺寸则取第一张有效纹理尺寸
            var firstValid = GetFirstValid(texures: textures);
            if (targetWidth <= 0 || targetHeight <= 0)
            {
                if (firstValid != null)
                {
                    targetWidth = firstValid.width;
                    targetHeight = firstValid.height;
                }
                else
                {
                    targetWidth = 1;
                    targetHeight = 1;
                }
            }

            int slices = Mathf.Max(1, textures.Count);

            bool needRebuild = _arrayCache == null
                               || _cachedWidth != targetWidth
                               || _cachedHeight != targetHeight
                               || _cachedSlices != slices
                               || _cachedKey != cacheKey
                               || _arrayCache.format != TextureFormat.RGBA32;

            if (needRebuild)
            {
                if (_arrayCache != null)
                {
                    Object.DestroyImmediate(_arrayCache);
                    _arrayCache = null;
                }
                _arrayCache = new Texture2DArray(targetWidth, targetHeight, slices, TextureFormat.RGBA32, true, true)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 1,
                    name = "MrPath_Preview_LayerTexArray"
                };
                _cachedWidth = targetWidth;
                _cachedHeight = targetHeight;
                _cachedSlices = slices;
                _cachedKey = cacheKey;
            }

            // 逐层写入，保持索引一致性（统一通过着色器转换到 ARGB32 RT，再拷贝到数组）
            for (int i = 0; i < slices; i++)
            {
                var src = (i < textures.Count && textures[i] != null) ? textures[i] : Texture2D.whiteTexture;

                var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rt.filterMode = FilterMode.Bilinear;

                // 根据项目色彩空间决定是否执行 sRGB->Linear 转换
                // 在运行时无法读取导入器的 sRGB 标记；对预览的漫反射贴图默认执行 sRGB->Linear
                var srgbToLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                ShaderBasedConverter.Blit(src, rt, srgbToLinear);

                // 直接从 RT 拷贝到数组，避免 ReadPixels 的 CPU 复制
                try
                {
                    Graphics.CopyTexture(rt, 0, 0, _arrayCache, i, 0);
                }
                catch
                {
                    // 罕见情况下的回退：RT -> Texture2D -> 数组
                    var tmp = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false, true);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0, false);
                    tmp.Apply(false, false);
                    RenderTexture.active = prev;
                    Graphics.CopyTexture(tmp, 0, 0, _arrayCache, i, 0);
                    Object.DestroyImmediate(tmp);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            _arrayCache.Apply(false, false);

            // 绑定到材质，并打开数组采样开关
            target.SetTexture(PreviewShaderContracts.Properties.LayerTextures, _arrayCache);
            target.SetFloat(PreviewShaderContracts.Properties.UseLayerTexArray, 1f);
            return true;
        }

        public void Cleanup()
        {
            if (_arrayCache != null)
            {
                Object.DestroyImmediate(_arrayCache);
                _arrayCache = null;
            }
            _cachedWidth = _cachedHeight = _cachedSlices = 0;
            _cachedKey = null;
        }

        private static Texture2D GetFirstValid(IList<Texture2D> texures)
        {
            for (int i = 0; i < texures.Count; i++)
            {
                var t = texures[i];
                if (t != null) return t;
            }
            return null;
        }
    }
}
