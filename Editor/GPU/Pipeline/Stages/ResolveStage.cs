using System;
using UnityEngine;
using __temp.MrPathV2.Editor.GPU.Core;

namespace __temp.MrPathV2.Editor.GPU.Pipeline.Stages
{
    /// <summary>
    /// 解析阶段（类似片段处理的结果应用）：
    /// - 明确输入：GpuRenderResult + Streamer
    /// - 明确输出：bool（是否成功）
    /// </summary>
    public sealed class ResolveStage : IPipelineStage<ResolveStage.Input, bool>
    {
        public struct Input
        {
            public GpuRenderResult Result;
            public GpuDataStreamer Streamer;
        }

        public bool Execute(Input input, out string error)
        {
            error = null;
            var res = input.Result;
            if (res == null || res.IsDisposed)
            {
                error = "[ResolveStage] 渲染结果无效或已释放";
                return false;
            }
            if (input.Streamer == null)
            {
                error = "[ResolveStage] 数据流器未提供";
                return false;
            }

            try
            {
                input.Streamer.ApplyToTerrain(res);
                return true;
            }
            catch (Exception ex)
            {
                error = $"[ResolveStage] 应用到地形失败: {ex.Message}";
                return false;
            }
        }
    }
}

