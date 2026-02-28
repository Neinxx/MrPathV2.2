using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(menuName = "MrPath/Blend Masks/Noise Mask")]
    public class NoiseMask : __temp.MrPathV2._2.Runtime.Core.BlendMasks.ProceduralMaskBase
    {
        // Note: Removed legacy 'scale' field. Use tiling (inherited from BlendMaskBase) to control
        // horizontal (X) and vertical (Y) frequency of the noise pattern.

        /// <summary>
        /// Evaluate mask value at the given horizontal position and path progress.
        /// </summary>
        /// <param name="horizontalPosition">Normalized horizontal position across the road (-1 .. 1).</param>
        /// <param name="pathProgress">Normalized progress along the path (0 .. 1).</param>
        /// <param name="worldWidth">World-space width of the road section (meters).</param>
        /// <param name="pathLength">Total length of the path (meters).</param>
        /// <returns>Mask strength in range 0..1.</returns>
        public override float Evaluate(float horizontalPosition, float pathProgress, float worldWidth, float pathLength)
        {
            // Convert to UV space with tiling & offset taken from base class fields
            float u = TransformPosition(horizontalPosition, worldWidth);
            float v = TransformPathPosition(pathProgress, pathLength);

            // Perlin noise in range 0..1, remapped by strength
            float noise = Mathf.PerlinNoise(u, v);
            float rawValue = noise * strength;
            return ApplySmoothing(Mathf.Clamp01(rawValue));
        }

        // Backward-compatibility: if caller does not supply pathProgress, assume center (0.5)
        public override float Evaluate(float horizontalPosition, float worldWidth, float pathLength)
        {
            return Evaluate(horizontalPosition, 0.5f, worldWidth, pathLength);
        }
    }
}