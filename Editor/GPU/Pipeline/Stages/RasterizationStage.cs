using System;
using UnityEngine;
using __temp.MrPathV2.Editor.GPU.Core;

namespace __temp.MrPathV2.Editor.GPU.Pipeline.Stages
{
    /// <summary>
    /// 栅格化阶段（Compute Shader 调度）：
    /// - 明确输入：GpuDataPacket + 是否预览
    /// - 明确输出：RenderTexture（体渲染到 AlphaMap 数组）
    /// </summary>
    public sealed class RasterizationStage : IPipelineStage<RasterizationStage.Input, RenderTexture>
    {
        public struct Input
        {
            public GpuDataStreamer.GpuDataPacket Packet;
            public GpuComputeDispatcher Dispatcher;
            public bool IsPreview;
        }

        public RenderTexture Execute(Input input, out string error)
        {
            error = null;

            if (!input.Packet.IsValid)
            {
                error = "[RasterizationStage] 数据包无效";
                return null;
            }
            if (input.Dispatcher == null)
            {
                error = "[RasterizationStage] 计算调度器未提供";
                return null;
            }

            try
            {
                return input.Dispatcher.ExecuteCompute(input.Packet, input.IsPreview);
            }
            catch (Exception ex)
            {
                error = $"[RasterizationStage] 执行计算失败: {ex.Message}";
                return null;
            }
        }
    }
}

