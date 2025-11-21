using System.IO;
using UnityEditor;
using UnityEngine;
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Core.BlendMasks.Composition;

namespace MrPathV2.Editor.Tools
{
    public static class ComposedMaskConverter
    {
        [MenuItem("MrPath/Tools/Convert Selected Mask To Composed", false, 1000)]
        public static void ConvertSelected()
        {
            var objs = Selection.objects;
            if (objs == null) return;
            foreach (var o in objs)
            {
                ConvertOne(o as BlendMaskBase);
            }
        }

        private static void ConvertOne(BlendMaskBase src)
        {
            if (!src) return;
            var path = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(path)) return;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var cm = ScriptableObject.CreateInstance<ComposedMaskSO>();
            cm.tiling = src.tiling;
            cm.offset = src.offset;
            cm.overallScale = src.overallScale;
            cm.smooth = src.smooth;
            if (src is ProceduralMaskBase p)
            {
                cm.seed = p.seed;
                cm.strength = p.strength;
            }

            RegionSelectorSO region = null;
            NoiseVariantSO variant = null;

            if (src is ShoulderPerlinNoiseMask spn)
            {
                var r = ScriptableObject.CreateInstance<ShoulderRegionSO>();
                r.shoulderWidthRatio = spn.shoulderWidthRatio;
                r.positionRatio = spn.shoulderPositionRatio;
                r.shoulderStrength = spn.shoulderStrength;
                r.edgeFalloff = spn.edgeFalloff;
                r.enableLeft = spn.enableLeftShoulder;
                r.enableRight = spn.enableRightShoulder;
                AssetDatabase.CreateAsset(r, Path.Combine(dir, name + "_Region.asset"));
                region = r;

                var v = ScriptableObject.CreateInstance<FbmNoiseVariantSO>();
                v.octaves = spn.octaves;
                v.lacunarity = spn.lacunarity;
                v.gain = spn.gain;
                AssetDatabase.CreateAsset(v, Path.Combine(dir, name + "_Fbm.asset"));
                variant = v;

                cm.noiseScale = spn.noiseScale;
                cm.uniformScale = spn.uniformScale;
                cm.rotationDeg = spn.rotationDeg;
                cm.useAsymmetricEdges = spn.useAsymmetricEdges;
                cm.edgeLow = spn.edgeLow;
                cm.edgeHigh = spn.edgeHigh;
            }
            else if (src is EdgePerlinNoiseMask epn)
            {
                var r = ScriptableObject.CreateInstance<EdgeBandRegionSO>();
                r.bandWidthRatio = epn.edgeBandWidthRatio;
                r.edgeFalloff = epn.edgeFalloff;
                r.enableLeft = epn.enableLeftEdge;
                r.enableRight = epn.enableRightEdge;
                AssetDatabase.CreateAsset(r, Path.Combine(dir, name + "_Region.asset"));
                region = r;

                var v = ScriptableObject.CreateInstance<FbmNoiseVariantSO>();
                v.octaves = epn.octaves;
                v.lacunarity = epn.lacunarity;
                v.gain = epn.gain;
                AssetDatabase.CreateAsset(v, Path.Combine(dir, name + "_Fbm.asset"));
                variant = v;

                cm.noiseScale = epn.noiseScale;
                cm.uniformScale = epn.uniformScale;
                cm.rotationDeg = epn.rotationDeg;
                cm.useAsymmetricEdges = epn.useAsymmetricEdges;
                cm.edgeLow = epn.edgeLow;
                cm.edgeHigh = epn.edgeHigh;
            }
            else if (src is NoiseMask nm)
            {
                var v = ScriptableObject.CreateInstance<FbmNoiseVariantSO>();
                v.octaves = nm.octaves;
                v.lacunarity = nm.lacunarity;
                v.gain = nm.gain;
                AssetDatabase.CreateAsset(v, Path.Combine(dir, name + "_Fbm.asset"));
                variant = v;
                cm.noiseScale = nm.noiseScale;
                cm.uniformScale = nm.uniformScale;
                cm.rotationDeg = nm.rotationDeg;
                cm.useAsymmetricEdges = nm.useAsymmetricEdges;
                cm.edgeLow = nm.edgeLow;
                cm.edgeHigh = nm.edgeHigh;
            }
            else if (src is StripeNoiseMask snm)
            {
                var v = ScriptableObject.CreateInstance<StripeNoiseVariantSO>();
                v.period = snm.period;
                v.jitter = snm.jitter;
                AssetDatabase.CreateAsset(v, Path.Combine(dir, name + "_Stripe.asset"));
                variant = v;
                cm.noiseScale = snm.noiseScale;
                cm.uniformScale = snm.uniformScale;
                cm.rotationDeg = snm.rotationDeg;
                cm.useAsymmetricEdges = snm.useAsymmetricEdges;
                cm.edgeLow = snm.edgeLow;
                cm.edgeHigh = snm.edgeHigh;
            }
            else if (src is WorleyNoiseMask wnm)
            {
                var v = ScriptableObject.CreateInstance<WorleyNoiseVariantSO>();
                v.cellPeriod = wnm.cellPeriod;
                v.jitter = wnm.jitter;
                v.invert = wnm.invert;
                AssetDatabase.CreateAsset(v, Path.Combine(dir, name + "_Worley.asset"));
                variant = v;
                cm.noiseScale = wnm.noiseScale;
                cm.uniformScale = wnm.uniformScale;
                cm.rotationDeg = wnm.rotationDeg;
                cm.useAsymmetricEdges = wnm.useAsymmetricEdges;
                cm.edgeLow = wnm.edgeLow;
                cm.edgeHigh = wnm.edgeHigh;
            }

            cm.region = region;
            cm.noise = variant;
            var cmPath = Path.Combine(dir, name + "_Composed.asset");
            AssetDatabase.CreateAsset(cm, cmPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = cm;
        }
    }
}
