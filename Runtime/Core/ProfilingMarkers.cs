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
        public static readonly ProfilerMarker PathSampler_SamplePath = new ProfilerMarker("MrPath.PathSampler.SamplePath");
        public static readonly ProfilerMarker PathSampler_DrapeSpineOnTerrain = new ProfilerMarker("MrPath.PathSampler.DrapeSpineOnTerrain");

        // ----------------- MaskAtlasGenerator -----------------
        public static readonly ProfilerMarker MaskAtlasGenerator_Build = new ProfilerMarker("MrPath.MaskAtlasGenerator.Build");

        // ----------------- Preview -----------------
        public static readonly ProfilerMarker PreviewLineRenderer_Update = new ProfilerMarker("MrPath.PreviewLineRenderer.UpdateMesh");

        // ----------------- TerrainJobs -----------------
        public static readonly ProfilerMarker TerrainJobs_Execute = new ProfilerMarker("MrPath.TerrainJobs.Execute");

        // Add more markers here as new hotspots are identified.
    }
}
