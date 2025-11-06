using Unity.Profiling;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     Centralized repository for <see cref="ProfilerMarker" /> instances used across MrPath.
    ///     This avoids string duplication and ensures consistent naming when adding profiling scopes.
    /// </summary>
    public static class ProfilingMarkers
    {
        // ----------------- PathSampler -----------------
        public static readonly ProfilerMarker PathSamplerSamplePath = new ProfilerMarker("MrPath.PathSampler.SamplePath");
        public static readonly ProfilerMarker PathSamplerDrapeSpineOnTerrain = new ProfilerMarker("MrPath.PathSampler.DrapeSpineOnTerrain");

        // ----------------- MaskAtlasGenerator -----------------
        public static readonly ProfilerMarker MaskAtlasGeneratorBuild = new ProfilerMarker("MrPath.MaskAtlasGenerator.Build");

        // ----------------- Preview -----------------
        public static readonly ProfilerMarker PreviewLineRendererUpdate = new ProfilerMarker("MrPath.PreviewLineRenderer.UpdateMesh");

        // ----------------- TerrainJobs -----------------
        public static readonly ProfilerMarker TerrainJobsExecute = new ProfilerMarker("MrPath.TerrainJobs.Execute");

        // Add more markers here as new hotspots are identified.
    }
}
