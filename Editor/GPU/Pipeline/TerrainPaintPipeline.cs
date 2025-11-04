using System;
using UnityEngine;
using __temp.MrPathV2.Editor.GPU.Core;
using __temp.MrPathV2.Editor.GPU.Pipeline.Stages;

namespace __temp.MrPathV2.Editor.GPU.Pipeline
{
    /// <summary>
    /// 地形纹理绘制管线编排器：清晰分阶段、明确输入输出、职责单一。
    /// 阶段：
    /// 1) DataPrep（顶点处理）：准备 ComputeBuffer/RenderTexture/参数
    /// 2) Rasterization（光栅化）：Compute Shader 调度输出体纹理
    /// 3) Resolve（解析）：写回 TerrainData（非预览）
    /// </summary>
    public sealed class TerrainPaintPipeline
    {
        private readonly DataPrepStage _dataPrep = new DataPrepStage();
        private readonly RasterizationStage _raster = new RasterizationStage();
        private readonly ResolveStage _resolve = new ResolveStage();

        private readonly GpuDataStreamer _streamer;
        private readonly GpuComputeDispatcher _dispatcher;

        public TerrainPaintPipeline(GpuDataStreamer streamer, GpuComputeDispatcher dispatcher)
        {
            _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// 执行完整管线（预览不执行解析阶段）。
        /// </summary>
        public (RenderTexture tex, GpuDataStreamer.GpuComputeParams cp, string error) Execute(
            UnityEngine.Terrain terrain,
            PathData path,
            PathRecipe recipe,
            bool isPreview)
        {
            // 1) 数据准备
            var prepInput = new DataPrepStage.Input
            {
                Terrain = terrain,
                Path = path,
                Recipe = recipe,
                Streamer = _streamer
            };
            var packet = _dataPrep.Execute(prepInput, out var prepErr);
            if (!string.IsNullOrEmpty(prepErr))
            {
                return (null, default, prepErr);
            }

            // 2) 栅格化
            var rastInput = new RasterizationStage.Input
            {
                Packet = packet,
                Dispatcher = _dispatcher,
                IsPreview = isPreview
            };
            var tex = _raster.Execute(rastInput, out var rastErr);
            if (!string.IsNullOrEmpty(rastErr) || tex == null)
            {
                return (null, default, rastErr ?? "[TerrainPaintPipeline] 栅格化阶段返回空纹理");
            }

            // 3) 返回结果（解析阶段由外部条件决定触发）
            return (tex, packet.ComputeParams, null);
        }

        /// <summary>
        /// 将结果应用到地形（非预览）。
        /// </summary>
        public (bool success, string error) Resolve(GpuRenderResult result)
        {
            var input = new ResolveStage.Input { Result = result, Streamer = _streamer };
            var ok = _resolve.Execute(input, out var err);
            return (ok, err);
        }
    }
}

