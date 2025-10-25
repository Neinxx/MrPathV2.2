using UnityEngine;

namespace MrPathV2._2.Runtime.Core
{
    /// <summary>
    ///     统一计算 TerrainLayer 纹理平铺系数的工具。
    /// </summary>
    public static class LayerTilingUtility
    {
        /// <summary>
        ///     根据道路世界宽度和 TerrainLayer.tileSize 计算 Tiling。
        /// </summary>
        public static Vector2 CalcLayerTiling(float previewWorldWidth, TerrainLayer layer)
        {
            if (layer == null) return Vector2.one;

            var sz = layer.tileSize;
            if (Mathf.Approximately(sz.x, 0f)) sz.x = 1f;
            if (Mathf.Approximately(sz.y, 0f)) sz.y = 1f;

            // For world-space UV mapping, tiling should be the reciprocal of terrain tile size
            var tilingX = 1f / sz.x;
            var tilingY = 1f / sz.y;
            if (!float.IsFinite(tilingX)) tilingX = 1f;
            if (!float.IsFinite(tilingY)) tilingY = 1f;
            return new Vector2(tilingX, tilingY);
        }
    }
}
