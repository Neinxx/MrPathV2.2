using System;
using UnityEngine;
using MrPathV2.Editor.GPU.Core;

namespace MrPathV2.Editor.GPU.Pipeline.Stages
{
    /// <summary>
    /// 数据准备阶段（类似顶点处理）：
    /// - 负责将路径与配方数据转换为 GPU 可用的缓冲与纹理
    /// - 明确输入：Terrain、PathData、PathRecipe
    /// - 明确输出：GpuDataStreamer.GpuDataPacket
    /// </summary>
    public sealed class DataPrepStage : IPipelineStage<DataPrepStage.Input, GpuDataStreamer.GpuDataPacket>
    {
        public struct Input
        {
            public UnityEngine.Terrain Terrain;
            public PathData Path;
            public PathRecipe Recipe;
            public GpuDataStreamer Streamer;
        }

        public GpuDataStreamer.GpuDataPacket Execute(Input input, out string error)
        {
            error = null;

            // 提前返回：严格校验输入
            if (input.Terrain == null || input.Terrain.terrainData == null)
            {
                error = "[DataPrepStage] 地形或地形数据无效";
                return default;
            }
            if (input.Path == null)
            {
                error = "[DataPrepStage] 路径数据为空";
                return default;
            }
            if (input.Recipe == null)
            {
                error = "[DataPrepStage] 绘制配方为空";
                return default;
            }
            if (input.Streamer == null)
            {
                error = "[DataPrepStage] 数据流器未提供";
                return default;
            }

            try
            {
                // 使用高效的数据打包与资源复用
                return input.Streamer.PrepareGpuData(input.Terrain, input.Path, input.Recipe);
            }
            catch (Exception ex)
            {
                error = $"[DataPrepStage] 准备GPU数据失败: {ex.Message}";
                return default;
            }
        }
    }
}

