using System;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Jobs;
using Unity.Collections;
using Unity.Mathematics;
// For PathSpine, PathProfile etc.

namespace MrPathV2.Editor.Terrain // Or Runtime.Interfaces
{
    /// <summary>
    ///     Interface for terrain painting strategies (CPU or GPU).
    /// </summary>
    public interface ITerrainPainter : IDisposable
    {
        /// <summary>
        ///     Executes the terrain painting operation asynchronously.
        /// </summary>
        /// <param name="terrain">The target terrain.</param>
        /// <param name="spineData">Pre-calculated path spine data.</param>
        /// <param name="profileData">Pre-calculated path profile data.</param>
        /// <param name="recipeData">Pre-calculated recipe data (CPU version needs this).</param>
        /// <param name="recipeGpuData">GPU data manager (GPU version needs this).</param>
        /// <param name="roadContour">Road contour polygon.</param>
        /// <param name="contourBounds">Contour bounding box.</param>
        /// <param name="coverageMin">Pixel min bounds for painting.</param>
        /// <param name="coverageMax">Pixel max bounds for painting.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task representing the async operation.</returns>
        Task ExecuteAsync(
            UnityEngine.Terrain terrain,
            PathJobsUtility.SpineData spineData,
            PathJobsUtility.ProfileData profileData,
            RecipeData recipeData, // For CPU path
            RecipeGpuDataManager recipeGpuData, // For GPU path
            NativeArray<float2> roadContour,
            float4 contourBounds,
            int2 coverageMin,
            int2 coverageMax,
            CancellationToken token
        );
    }
}
