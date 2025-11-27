using UnityEngine;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    /// 基于着色器的纹理格式转换与 Blit 工具。
    /// - 将任意源纹理转换到目标 RenderTexture（ARGB32），避免 CopyTexture 的格式不匹配问题。
    /// - 可选执行 sRGB->Linear 转换以匹配项目色彩空间。
    /// </summary>
    internal static class ShaderBasedConverter
    {
        private static Material s_ConvertMat;

        private static Material GetMaterial(bool srgbToLinear)
        {
            var shader = Shader.Find("Hidden/MrPath/ConvertShader");
            if (!shader)
            {
                // 回退到默认 Blit
                return null;
            }
            if (!s_ConvertMat || s_ConvertMat.shader != shader)
            {
                s_ConvertMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (srgbToLinear)
            {
                s_ConvertMat.EnableKeyword("_SRGB_TO_LINEAR");
            }
            else
            {
                s_ConvertMat.DisableKeyword("_SRGB_TO_LINEAR");
            }
            return s_ConvertMat;
        }

        /// <summary>
        /// 使用着色器将源纹理绘制到目标 RenderTexture（格式由目标决定）。
        /// </summary>
        public static void Blit(Texture src, RenderTexture dst, bool srgbToLinear)
        {
            var mat = GetMaterial(srgbToLinear);
            if (mat)
            {
                Graphics.Blit(src, dst, mat);
            }
            else
            {
                Graphics.Blit(src, dst);
            }
        }
    }
}