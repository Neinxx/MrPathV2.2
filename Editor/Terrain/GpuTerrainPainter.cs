// 文件: Editor/Terrain/GpuTerrainPainter.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Jobs; // For PathJobsUtility
using __temp.MrPathV2._2.Runtime.Jobs.Extensions; // For SafeDispose
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using __temp.MrPathV2._2.Runtime.Interfaces;
using __temp.MrPathV2._2.Runtime.Settings;
using System.Collections.Generic; // For Dictionary
using System.Linq;
using UnityEditor;
using UnityEngine.Experimental.Rendering; // For Linq

namespace __temp.MrPathV2._2.Editor.Terrain
{
    public class GpuTerrainPainter : ITerrainPainter
    {
        private readonly ComputeShader _paintComputeShader;
        private readonly int _kernelHandle = -1;

        // --- Cache Shader Property IDs ---
        private static readonly int AlphamapResolutionID = Shader.PropertyToID("_AlphamapResolution");
        private static readonly int AlphamapLayerCountID = Shader.PropertyToID("_AlphamapLayerCount");
        private static readonly int TerrainPositionID = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeID = Shader.PropertyToID("_TerrainSize");
        private static readonly int SpinePointsID = Shader.PropertyToID("_SpinePoints");
        private static readonly int SpineTangentsID = Shader.PropertyToID("_SpineTangents");
        private static readonly int SpineNormalsID = Shader.PropertyToID("_SpineNormals");
        private static readonly int SpinePointCountID = Shader.PropertyToID("_SpinePointCount");
        private static readonly int RoadWidthID = Shader.PropertyToID("_RoadWidth");
        private static readonly int PathLengthID = Shader.PropertyToID("_PathLength");
        private static readonly int ForceHorizontalID = Shader.PropertyToID("_ForceHorizontal");
        private static readonly int CoverageMinID = Shader.PropertyToID("_CoverageMin");
        private static readonly int CoverageMaxID = Shader.PropertyToID("_CoverageMax");
        private static readonly int LayerParamsID = Shader.PropertyToID("_LayerParams");
        private static readonly int TerrainTexturesID = Shader.PropertyToID("_TerrainTextures");
        private static readonly int OutputAlphaMapsID = Shader.PropertyToID("_OutputAlphaMaps");
        private static readonly int NumActiveLayersID = Shader.PropertyToID("_NumActiveLayers");
        // --------------------------------

        public GpuTerrainPainter()
        {
            _paintComputeShader = Resources.Load<ComputeShader>("PaintSplatmapCompute");
            if (_paintComputeShader)
            {
                _kernelHandle = _paintComputeShader.FindKernel("PaintTerrain");
            }
            else
            {
                Debug.LogError("[GpuTerrainPainter] PaintSplatmapCompute.compute shader not found in Resources folder!");
            }
        }

        public async Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData, // Not used
            RecipeGpuDataManager recipeGpuData, // Used
            NativeArray<float2> roadContour,
            float4 contourBounds,
            int2 coverageMin,
            int2 coverageMax,
            CancellationToken token)
        {
            // --- Input Validation ---
             if (_kernelHandle == -1) { Debug.LogError("[GpuTerrainPainter] Compute shader kernel not found."); return; }
             if (recipeGpuData?.LayerParamsBuffer == null || !recipeGpuData.LayerParamsBuffer.IsValid()) { Debug.LogError("[GpuTerrainPainter] Recipe GPU data LayerParamsBuffer is invalid or null."); return; }
             if (!spineData.IsCreated) { Debug.LogError("[GpuTerrainPainter] Invalid SpineData provided."); return; }
             var td = terrain.terrainData;
             if (!td) { Debug.LogError("[GpuTerrainPainter] TerrainData is null."); return; }
            // 预先缓存原始 alphamap，以便后续将未修改区域保持不变，防止被置黑
            var originalAlphamaps = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
             var alphaMapTextures = td.alphamapTextures;
             if (alphaMapTextures == null || alphaMapTextures.Length == 0) { Debug.LogError($"[GpuTerrainPainter] Terrain '{terrain.name}' does not have alphamap textures."); return; }

            // --- Graphics Format Check ---
            var format = alphaMapTextures[0].graphicsFormat;
            // --- FIX for FormatUsage ---
            // Use LoadStore (10) as seen in screenshot, or RandomWrite if available
            if (!SystemInfo.IsFormatSupported(format, FormatUsage.LoadStore)) 
            {
                 // Fallback check for older Unity
                 if (!SystemInfo.IsFormatSupported(format, (FormatUsage)10 /*LoadStore*/))
                 {
                    Debug.LogError($"[GpuTerrainPainter] Terrain alphamap format '{format}' is not supported for Compute Shader RWTexture (LoadStore Usage).");
                    return;
                 }
            }
            // ------------------------

            var resolution = td.alphamapResolution;
            var layers = td.alphamapLayers;
            var numPixelsX = coverageMax.x - coverageMin.x + 1;
            var numPixelsY = coverageMax.y - coverageMin.y + 1;

            if (numPixelsX <= 0 || numPixelsY <= 0) return;

            RenderTexture tempAlphaMaps = null;
            ComputeBuffer spinePointsBuffer = null;
            ComputeBuffer spineTangentsBuffer = null;
            ComputeBuffer spineNormalsBuffer = null;

            try
            {
                token.ThrowIfCancellationRequested();

                // 1. Create Temporary Writable AlphaMap Texture Array
                tempAlphaMaps = new RenderTexture(resolution, resolution, 0, format)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = layers,
                    enableRandomWrite = true,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    name = "Temp_Alphamap_RT"
                };
                if (!tempAlphaMaps.Create()) { Debug.LogError("[GpuTerrainPainter] Failed to create temporary RenderTexture for alphamaps."); return; }
                
                // IMPORTANT: Clear the RT to 0, otherwise old data or NaN can cause issues
                // We copy the *first slice* (base terrain) to clear all slices? No, better to clear manually.
                // Easiest way is often a quick compute shader clear pass, or Blit with clear material.
                // For simplicity, let's copy existing data into it.
                Graphics.CopyTexture(alphaMapTextures[0], 0, 0, tempAlphaMaps, 0, 0);
                 if(layers > 1 && alphaMapTextures.Length > 1) { // Handle multi-texture alphamaps if they exist
                      Graphics.CopyTexture(alphaMapTextures[1], 0, 0, tempAlphaMaps, 1, 0); // (Untested, check API)
                 }

                token.ThrowIfCancellationRequested();

                // 2. Prepare Compute Shader Inputs
                 _paintComputeShader.SetInt(AlphamapResolutionID, resolution);
                 _paintComputeShader.SetInt(AlphamapLayerCountID, layers);
                 _paintComputeShader.SetVector(TerrainPositionID, terrain.GetPosition());
                 _paintComputeShader.SetVector(TerrainSizeID, td.size);

                 // --- Upload Spine Data ---
                 spinePointsBuffer = new ComputeBuffer(math.max(1, spineData.Points.Length), sizeof(float) * 3);
                 if (spineData.Points.Length > 0) spinePointsBuffer.SetData(spineData.Points);
                 _paintComputeShader.SetBuffer(_kernelHandle, SpinePointsID, spinePointsBuffer);

                 spineTangentsBuffer = new ComputeBuffer(math.max(1, spineData.Tangents.Length), sizeof(float) * 3);
                 if (spineData.Tangents.Length > 0) spineTangentsBuffer.SetData(spineData.Tangents);
                 _paintComputeShader.SetBuffer(_kernelHandle, SpineTangentsID, spineTangentsBuffer);

                 spineNormalsBuffer = new ComputeBuffer(math.max(1, spineData.Normals.Length), sizeof(float) * 3);
                 if (spineData.Normals.Length > 0) spineNormalsBuffer.SetData(spineData.Normals);
                 _paintComputeShader.SetBuffer(_kernelHandle, SpineNormalsID, spineNormalsBuffer);

                _paintComputeShader.SetInt(SpinePointCountID, spineData.Points.Length);
                // -------------------------

                _paintComputeShader.SetFloat(RoadWidthID, profileData.RoadWidth);
                var pathLength = CalculatePathLengthFromSpine(spineData);
                _paintComputeShader.SetFloat(PathLengthID, pathLength);
                _paintComputeShader.SetBool(ForceHorizontalID, profileData.ForceHorizontal);
                _paintComputeShader.SetInts(CoverageMinID, coverageMin.x, coverageMin.y);
                _paintComputeShader.SetInts(CoverageMaxID, coverageMax.x, coverageMax.y);

                // --- Recipe Data ---
                _paintComputeShader.SetInt(NumActiveLayersID, recipeGpuData.ActiveLayerCount); // <-- FIX
                _paintComputeShader.SetBuffer(_kernelHandle, LayerParamsID, recipeGpuData.LayerParamsBuffer);
                if (recipeGpuData.TerrainTextureArray)
                {
                    _paintComputeShader.SetTexture(_kernelHandle, TerrainTexturesID, recipeGpuData.TerrainTextureArray);
                }
                // -------------------

                // 3. Set Output Texture
                _paintComputeShader.SetTexture(_kernelHandle, OutputAlphaMapsID, tempAlphaMaps);

                token.ThrowIfCancellationRequested();

                // 4. Dispatch Compute Shader
                _paintComputeShader.GetKernelThreadGroupSizes(_kernelHandle, out var threadsX, out var threadsY, out _);
                var groupsX = Mathf.CeilToInt((float)numPixelsX / threadsX);
                var groupsY = Mathf.CeilToInt((float)numPixelsY / threadsY);
                if (groupsX > 0 && groupsY > 0)
                {
                    _paintComputeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
                }
                else { return; }

                token.ThrowIfCancellationRequested();

                // 5. Copy Result back to TerrainData
                //    We use Graphics.CopyTexture for speed. This assumes alphamapTextures[0] is the destination.
                //    This requires the GPU to be finished.
                //    A better way for editor scripting is often to just readback and SetAlphamaps.

                 var request = AsyncGPUReadback.Request(tempAlphaMaps);
                 while (!request.done)
                 {
                     if (token.IsCancellationRequested)
                     {
                          Debug.LogWarning("[GpuTerrainPainter] Cancellation requested during GPU readback.");
                          token.ThrowIfCancellationRequested();
                     }
                     await Task.Yield();
                 }
                 token.ThrowIfCancellationRequested();

                if (request.hasError)
                {
                    Debug.LogError("[GpuTerrainPainter] AsyncGPUReadback encountered an error!");
                }
                else
                {
                    await Task.Yield(); // Ensure main thread
                    token.ThrowIfCancellationRequested();

                    if (td.alphamapTextureCount > 0)
                    {
                         var data = request.GetData<float>(0);
                         td.SetAlphamaps(0, 0, MergeWithOriginal(originalAlphamaps, ConvertReadbackTo3D(data, resolution, resolution, layers), coverageMin, coverageMax));
                         terrain.Flush();
                         EditorUtility.SetDirty(td);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                 Debug.Log($"[GpuTerrainPainter] Operation cancelled.");
                 throw;
            }
            catch (Exception ex)
            {
                 Debug.LogError($"[GpuTerrainPainter] Error during execution: {ex.Message}\n{ex.StackTrace}");
                 throw;
            }
            finally
            {
                // 6. Cleanup GPU resources
                spinePointsBuffer?.Release();
                spineTangentsBuffer?.Release();
                spineNormalsBuffer?.Release();
                tempAlphaMaps?.Release();
            }
        }

        private float CalculatePathLengthFromSpine(PathJobsUtility.SpineData spineData)
        {
             if (!spineData.IsCreated || spineData.Points.Length < 2) return 0f;
             var length = 0f;
             for(var i = 1; i < spineData.Points.Length; i++)
             {
                 length += math.distance(spineData.Points[i], spineData.Points[i-1]);
             }
             return length;
        }

        private float[,,] ConvertReadbackTo3D(NativeArray<float> flatData, int width, int height, int depth)
        {
            var data3D = new float[height, width, depth];
            if (!flatData.IsCreated || flatData.Length != width * height * depth) {
                 Debug.LogError($"Readback data length mismatch! Expected {width*height*depth}, got {(flatData.IsCreated ? flatData.Length : 0)}");
                 return data3D;
            }
            var index = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    for (var z = 0; z < depth; z++)
                        data3D[y, x, z] = flatData[index++];
            return data3D;
        }

        private float[,,] MergeWithOriginal(float[,,] original, float[,,] modified, int2 coverageMin, int2 coverageMax)
        {
            int height = original.GetLength(0), width = original.GetLength(1), depth = original.GetLength(2);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < coverageMin.x || x > coverageMax.x || y < coverageMin.y || y > coverageMax.y)
                    {
                        for (int z = 0; z < depth; z++)
                        {
                            modified[y, x, z] = original[y, x, z];
                        }
                    }
                }
            }
            return modified;
        }

        public void Dispose() { }
    }
}