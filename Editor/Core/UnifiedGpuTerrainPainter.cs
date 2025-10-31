using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2.Runtime.Core;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Core
{
    /// <summary>
    /// 统一GPU地形绘制器（示例/过渡版本）
    /// </summary>
    public sealed class UnifiedGpuTerrainPainter : IUnifiedTerrainPainter, IDisposable
    {
        private ComputeShader _paintShader;

        public UnifiedGpuTerrainPainter()
        {
            InitializeGpuResources();
        }

        public bool IsSupported => SystemInfo.supportsComputeShaders;
        public PainterType Type => PainterType.GPU;

        private void InitializeGpuResources()
        {
            // 加载计算着色器
            _paintShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");
            if (_paintShader == null)
            {
                UnityEngine.Debug.LogError("[UnifiedGpuTerrainPainter] 无法加载计算着色器 'PaintSplatmapCompute'");
            }
        }

        public void Dispose()
        {
            // 示例版本无需额外释放
        }

        // 简化的参数结构（示例）
        private struct GpuComputeParams
        {
            public Vector2Int Resolution;
            public Vector3 TerrainPosition;
            public Vector3 TerrainSize;
            public float PathWidth;
            public float FalloffDistance;
        }

        public async Task<TerrainPaintResult> PaintAsync(UnityEngine.Terrain terrain, PathData pathData, PathProfile pathProfile, bool isPreview = false, CancellationToken cancellationToken = default)
        {
            var result = Paint(terrain, pathData, pathProfile, isPreview);
            await Task.Yield();
            return result;
        }

        public TerrainPaintResult Paint(UnityEngine.Terrain terrain, PathData pathData, PathProfile pathProfile, bool isPreview = false)
        {
            if (_paintShader == null)
            {
                return TerrainPaintResult.CreateFailure("计算着色器未加载", PainterType.GPU);
            }

            var td = terrain.terrainData;
            var computeParams = new GpuComputeParams
            {
                Resolution = new Vector2Int(td.alphamapResolution, td.alphamapResolution),
                TerrainPosition = terrain.transform.position,
                TerrainSize = td.size,
                PathWidth = pathProfile?.roadWidth ?? 0f,
                FalloffDistance = pathProfile?.falloffWidth ?? 0f
            };

            // 生成简单道路遮罩并执行着色器（示例）
            var roadMask = GenerateRoadMask(terrain, computeParams);
            var rt = ExecuteComputeShader(computeParams, roadMask, isPreview);

            // 示例返回，不应用到地形
            return TerrainPaintResult.CreateSuccess(PainterType.GPU, 0f, null);
        }

        private Texture2D GenerateRoadMask(UnityEngine.Terrain terrain, GpuComputeParams computeParams)
        {
            var terrainData = terrain.terrainData;
            var resolution = terrainData.alphamapResolution;
            var terrainPos = terrain.transform.position;
            var terrainSize = terrainData.size;

            // 创建遮罩纹理
            var maskTexture = new Texture2D(resolution, resolution, TextureFormat.R8, false);
            var pixels = new byte[resolution * resolution];

            // 简化：生成一个中心矩形作为道路示例
            int margin = Mathf.Max(1, resolution / 4);
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    bool inside = (x > margin && x < (resolution - margin) && y > margin && y < (resolution - margin));
                    pixels[y * resolution + x] = inside ? (byte)255 : (byte)0;
                }
            }

            maskTexture.LoadRawTextureData(pixels);
            maskTexture.Apply();

            return maskTexture;
        }

        private RenderTexture ExecuteComputeShader(GpuComputeParams computeParams, Texture2D roadMask, bool isPreview)
        {
            var kernelIndex = _paintShader.FindKernel("paint_terrain");
            if (kernelIndex < 0)
            {
                throw new InvalidOperationException("无法找到计算着色器内核 'paint_terrain'");
            }

            // 创建输出纹理
            var outputTexture = new RenderTexture(
                computeParams.Resolution.x,
                computeParams.Resolution.y,
                0,
                RenderTextureFormat.ARGBFloat)
            {
                enableRandomWrite = true
            };
            outputTexture.Create();

            // 设置计算着色器参数
            _paintShader.SetTexture(kernelIndex, "road_mask", roadMask);
            _paintShader.SetTexture(kernelIndex, "Result", outputTexture);
            _paintShader.SetVector("terrain_position", computeParams.TerrainPosition);
            _paintShader.SetVector("terrain_size", computeParams.TerrainSize);
            _paintShader.SetFloat("path_width", computeParams.PathWidth);
            _paintShader.SetFloat("falloff_distance", computeParams.FalloffDistance);

            // 计算线程组数量
            var threadGroupsX = Mathf.CeilToInt(computeParams.Resolution.x / 8.0f);
            var threadGroupsY = Mathf.CeilToInt(computeParams.Resolution.y / 8.0f);

            // 执行计算着色器
            _paintShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

            return outputTexture;
        }
    }
}
