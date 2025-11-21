using System;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.Constants;
using MrPathV2.Runtime.Core.Resources;
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace MrPathV2.Editor.Terrain
{
    public class GpuTerrainPainter : ITerrainPainter
    {
        private readonly StylizedRoadRecipe _recipe;

        public GpuTerrainPainter(StylizedRoadRecipe recipe)
        {
            _recipe = recipe;
        }
        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData,
            NativeArray<float2> roadContour,
            float4 contourBounds,
            Vector2Int coverageMin,
            Vector2Int coverageMax,
            CancellationToken token)
        {
            var td = terrain?.terrainData;
            if (td == null) return;
            if (!recipeData.IsCreated) return;
            if (_recipe == null) return;

            var cs = ResourceProvider.LoadComputeShader("PaintSplatmapCompute");
            if (cs == null || !cs.HasKernel("paint_terrain")) return;

            var resolution = td.alphamapResolution;
            var layerCount = td.alphamapLayers;
            var startX = Mathf.Clamp(coverageMin.x, 0, resolution - 1);
            var startY = Mathf.Clamp(coverageMin.y, 0, resolution - 1);
            var numX = Mathf.Clamp(coverageMax.x - coverageMin.x, 0, resolution - startX);
            var numY = Mathf.Clamp(coverageMax.y - coverageMin.y, 0, resolution - startY);
            if (numX <= 0 || numY <= 0) return;

            var kernelInit = cs.FindKernel("init_splat_roi");
            var kernelPaint = cs.FindKernel("paint_terrain");

            var terrainLayerMap = MaskAtlasGenerator.BuildTerrainLayerMap(td);
            var layerParamsBuf = MaskAtlasGenerator.BuildLayerParamsBuffer(_recipe, terrainLayerMap);

            var recipeMaskBuf = MaskAtlasGenerator.BuildSliceRecipeMaskBuffer(recipeData, layerCount);

            var spineBuf = new ComputeBuffer(spineData.Length, sizeof(float) * 3, ComputeBufferType.Structured);
            spineBuf.SetData(spineData.Points);

            var contourBuf = new ComputeBuffer(roadContour.Length, sizeof(float) * 2, ComputeBufferType.Structured);
            contourBuf.SetData(roadContour);

            var slices = (layerCount + 3) / 4;
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
            {
                volumeDepth = slices,
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();

            cs.SetTexture(kernelInit, "splat_weights", rt);
            cs.SetInt("alphamap_layer_count", layerCount);
            cs.SetInts("alphamap_offset", startX, startY);
            var cpuAlphas = td.GetAlphamaps(startX, startY, numX, numY);
            var flatLen = numX * numY * layerCount;
            var flat = new float[flatLen];
            var pi = 0;
            for (var y = 0; y < numY; y++)
                for (var x = 0; x < numX; x++)
                {
                    for (var li = 0; li < layerCount; li++)
                    {
                        flat[pi++] = cpuAlphas[y, x, li];
                    }
                }
            var cpuAlphaBuf = new ComputeBuffer(flatLen, sizeof(float), ComputeBufferType.Structured);
            cpuAlphaBuf.SetData(flat);
            cs.SetBuffer(kernelInit, "cpu_alphamaps_roi", cpuAlphaBuf);
            cs.SetInt("roi_width", numX);
            cs.SetInt("roi_height", numY);
            var gxInit = Mathf.CeilToInt(numX / 8f);
            var gyInit = Mathf.CeilToInt(numY / 8f);
            cs.Dispatch(kernelInit, gxInit, gyInit, 1);

            cs.SetBuffer(kernelPaint, "layer_params_buffer", layerParamsBuf);
            cs.SetBuffer(kernelPaint, "spine_data", spineBuf);
            cs.SetBuffer(kernelPaint, "road_contour", contourBuf);
            cs.SetBuffer(kernelPaint, "slice_recipe_mask", recipeMaskBuf);
            cs.SetTexture(kernelPaint, "splat_weights", rt);

            cs.SetInt("layer_count", recipeData.Length);
            cs.SetInt("alphamap_layer_count", layerCount);
            cs.SetInt("spine_point_count", spineData.Length);
            cs.SetFloat("falloff_distance", profileData.FalloffWidth);
            cs.SetFloat("road_width", profileData.RoadWidth);
            var tpos = terrain.GetPosition();
            cs.SetFloats("terrain_position", tpos.x, tpos.z);
            var tsz = td.size;
            cs.SetFloats("terrain_size", tsz.x, tsz.z);
            cs.SetInts("alphamap_resolution", resolution, resolution);
            cs.SetInts("terrain_offset", 0, 0);
            cs.SetInts("alphamap_offset", startX, startY);
            cs.SetInts("coverage_min", startX, startY);
            cs.SetInts("coverage_max", startX + numX, startY + numY);
            cs.SetInt("debug_mode", 0);
            cs.SetInt("contour_point_count", roadContour.Length);
            cs.SetFloats("contour_bounds", contourBounds.x, contourBounds.y, contourBounds.z, contourBounds.w);
            var threshold = profileData.OpaquePreview ? MaskConstants.DefaultAlphaClipThreshold : 0f;
            cs.SetFloat("mask_threshold", threshold);
            cs.SetFloat("edge_width_world", profileData.FalloffWidth);
            cs.SetInt("use_road_mask", 0);
            cs.SetInt("use_road_pixel_info", 0);
            cs.SetInt("opaque_painting", profileData.OpaquePreview ? 1 : 0);

            var gx = Mathf.CeilToInt(numX / 8f);
            var gy = Mathf.CeilToInt(numY / 8f);
            cs.Dispatch(kernelPaint, gx, gy, 1);

            await Task.Yield();

            var alphamaps3D = td.GetAlphamaps(startX, startY, numX, numY);
            for (var slice = 0; slice < slices; slice++)
            {
                for (var ch = 0; ch < 4; ch++)
                {
                    var splatIndex = slice * 4 + ch;
                    if (splatIndex >= layerCount) continue;
                    var tex = new Texture2D(numX, numY, TextureFormat.RGBAFloat, false, true);
                    Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, slice);
                    var rect = new Rect(startX, startY, numX, numY);
                    tex.ReadPixels(rect, 0, 0);
                    tex.Apply(false);
                    var cols = tex.GetPixels();
                    var idx = 0;
                    for (var y = 0; y < numY; y++)
                    {
                        for (var x = 0; x < numX; x++)
                        {
                            var c = cols[idx++];
                            var v = ch == 0 ? c.r : ch == 1 ? c.g : ch == 2 ? c.b : c.a;
                            alphamaps3D[y, x, splatIndex] = Mathf.Clamp01(v);
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            td.SetAlphamaps(startX, startY, alphamaps3D);
            terrain.Flush();
            EditorUtility.SetDirty(td);

            layerParamsBuf?.Release();
            recipeMaskBuf?.Release();
            spineBuf?.Release();
            contourBuf?.Release();
            cpuAlphaBuf?.Release();
            rt.Release();
        }

        public void Dispose() { }
    }
}
