using System.Collections.Generic;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 运行时 / 编辑器共用：根据 <see cref="StylizedRoadRecipe"/> 在 GPU 上生成合成结果，存入 RenderTexture。
    /// 设计目标：
    /// 1. 分辨率可与目标 TerrainData.alphamapResolution 保持一致；
    /// 2. 与 <see cref="PreviewPipelineUtility"/>、<see cref="TerrainJobsUtility"/> 的混合规则保持一致；
    /// 3. 支持任意数量图层（Ping-Pong Blit）。
    /// </summary>
    public static class RoadPreviewRenderPipeline
    {
        /// <summary>
        /// 生成或更新预览 RenderTexture。
        /// </summary>
        /// <param name="reuseRT">可复用的 RT，若尺寸或格式不符将被释放并重新创建。</param>
        /// <param name="terrainData">用于确定分辨率，可为空 (使用默认 512)。</param>
        /// <param name="profile">路径外观配置。</param>
        /// <param name="recipe">配方。</param>
        /// <returns>生成好的 RenderTexture（可能为复用对象，也可能为新建）。</returns>
        public static RenderTexture GeneratePreviewRT(RenderTexture reuseRT, TerrainData terrainData, PathProfile profile, StylizedRoadRecipe recipe)
        {
            if (profile == null || recipe == null)
            {
                // 无法生成
                return reuseRT;
            }

            // 1. 解析目标分辨率
            int resolution = terrainData ? terrainData.alphamapResolution : 512;
            resolution = Mathf.Clamp(resolution, 16, 4096);

            // 2. (Re)create RT if necessary
            if (reuseRT == null || reuseRT.width != resolution || reuseRT.height != resolution)
            {
                if (reuseRT != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(reuseRT);
                    else
                        Object.DestroyImmediate(reuseRT);
                }

                reuseRT = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "MrPath_PreviewRT",
                    enableRandomWrite = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                reuseRT.Create();
            }

            // 3. 收集激活图层
            var activeLayers = new List<BlendLayer>();
            foreach (var l in recipe.blendLayers)
            {
                if (l != null && l.enabled && l.terrainLayer != null && l.mask != null)
                    activeLayers.Add(l);
            }

            // 若无层，清空 RT 并返回
            if (activeLayers.Count == 0)
            {
                var old = RenderTexture.active;
                RenderTexture.active = reuseRT;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = old;
                return reuseRT;
            }

            // 4. 构建一次性的 Material（使用 StylizedRoadBlend.shader）
            Shader shader = Shader.Find("MrPathV2/StylizedRoadBlend");
            if (shader == null)
            {
                Debug.LogError("[MrPath] StylizedRoadBlend shader not found, cannot generate preview RT.");
                return reuseRT;
            }

            var mat = new Material(shader);
            // 若未提供 PathProfile，则回退到配方自身宽度
            float worldWidth = profile != null ? Mathf.Max(0.1f, profile.roadWidth) : Mathf.Max(0.1f, recipe.width);

            // 5. 准备 Ping-Pong 临时 RT
            RenderTextureDescriptor desc = reuseRT.descriptor;
            RenderTexture rtA = RenderTexture.GetTemporary(desc);
            RenderTexture rtB = RenderTexture.GetTemporary(desc);

            Graphics.SetRenderTarget(rtA);
            GL.Clear(true, true, Color.clear);

            // 6. 逐层叠加
            for (int i = 0; i < activeLayers.Count; i++)
            {
                var layer = activeLayers[i];
                Vector2 tiling = PreviewPipelineUtility.CalcLayerTiling(worldWidth, layer.terrainLayer);

                var pli = new PreviewPipelineUtility.PreviewLayerInfo(
                    layer.terrainLayer.diffuseTexture as Texture2D,
                    tiling,
                    Vector2.zero,
                    Color.white,
                    Mathf.Clamp01(layer.opacity * recipe.masterOpacity),
                    layer.blendMode,
                    layer.GetActiveMask());

                // 构建 LUT（单层版本：仅用 R 通道）
                Texture2D maskLUT = PreviewPipelineUtility.BuildMaskLUT(null, new List<PreviewPipelineUtility.PreviewLayerInfo> { pli }, worldWidth);

                // 设定材质属性
                mat.SetTexture("_LayerTex", pli.texture);
                mat.SetVector("_LayerTiling", new Vector4(tiling.x, tiling.y, 0, 0));
                mat.SetColor("_LayerTint", pli.tint);
                mat.SetFloat("_LayerOpacity", pli.opacity);
                mat.SetFloat("_BlendMode", (float)pli.blendMode);
                mat.SetTexture("_MaskLUT", maskLUT);

                // 输出到对方 RT
                if (i % 2 == 0)
                {
                    mat.SetTexture("_PreviousResultTex", rtA);
                    Graphics.Blit(rtA, rtB, mat);
                }
                else
                {
                    mat.SetTexture("_PreviousResultTex", rtB);
                    Graphics.Blit(rtB, rtA, mat);
                }
            }

            // 7. 将最终结果写入目标 RT
            Graphics.Blit(activeLayers.Count % 2 != 0 ? rtB : rtA, reuseRT);

            // 8. 释放临时资源
            RenderTexture.ReleaseTemporary(rtA);
            RenderTexture.ReleaseTemporary(rtB);

            // 销毁临时材质，防止泄漏
            if (Application.isPlaying)
                Object.Destroy(mat);
            else
                Object.DestroyImmediate(mat);

            return reuseRT;
        }
    }
}