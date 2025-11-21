using NUnit.Framework;
using UnityEngine;
using MrPathV2.Runtime.Core.BlendMasks;

namespace MrPathV2.Editor.Tests
{
    public class NoiseMasksSmokeTest
    {
        [Test]
        public void NoiseMask_Evaluate_InRange()
        {
            var m = ScriptableObject.CreateInstance<NoiseMask>();
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = 0f;
            m.octaves = 3;
            m.lacunarity = 2f;
            m.gain = 0.5f;
            m.seed = 123f;
            var v = m.Evaluate(0f, 0.5f, 10f, 100f);
            Assert.GreaterOrEqual(v, 0f);
            Assert.LessOrEqual(v, 1f);
        }

        [Test]
        public void ShoulderNoiseMask_Center_IsZero()
        {
            var m = ScriptableObject.CreateInstance<ShoulderPerlinNoiseMask>();
            m.shoulderWidthRatio = 0.12f;
            m.shoulderPositionRatio = 0.1f;
            m.edgeFalloff = 0.05f;
            m.enableLeftShoulder = true;
            m.enableRightShoulder = true;
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = 0f;
            m.octaves = 3;
            m.lacunarity = 2f;
            m.gain = 0.5f;
            m.seed = 123f;
            var v = m.Evaluate(0f, 0.5f, 10f, 100f);
            Assert.That(v, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void EdgePerlinNoiseMask_Edges_NonZero_Center_Zero()
        {
            var m = ScriptableObject.CreateInstance<EdgePerlinNoiseMask>();
            m.edgeBandWidthRatio = 0.12f;
            m.edgeFalloff = 0.05f;
            m.enableLeftEdge = true;
            m.enableRightEdge = true;
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = 0f;
            m.octaves = 3;
            m.lacunarity = 2f;
            m.gain = 0.5f;
            m.seed = 123f;
            var center = m.Evaluate(0f, 0.5f, 10f, 100f);
            var left = m.Evaluate(-1f, 0.5f, 10f, 100f);
            var right = m.Evaluate(1f, 0.5f, 10f, 100f);
            Assert.That(center, Is.EqualTo(0f).Within(1e-3f));
            Assert.Greater(left, 0f);
            Assert.Greater(right, 0f);
        }

        [Test]
        public void StripeNoiseMask_InRange()
        {
            var m = ScriptableObject.CreateInstance<StripeNoiseMask>();
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = 15f;
            m.period = 8f;
            m.jitter = 0.3f;
            m.seed = 42f;
            var v = m.Evaluate(0.25f, 0.5f, 12f, 150f);
            Assert.GreaterOrEqual(v, 0f);
            Assert.LessOrEqual(v, 1f);
        }

        [Test]
        public void WorleyNoiseMask_InRange()
        {
            var m = ScriptableObject.CreateInstance<WorleyNoiseMask>();
            m.noiseScale = new Vector2(1f, 1f);
            m.uniformScale = true;
            m.rotationDeg = -30f;
            m.cellPeriod = 32;
            m.jitter = 0.5f;
            m.invert = false;
            m.seed = 99f;
            var v = m.Evaluate(-0.4f, 0.25f, 8f, 80f);
            Assert.GreaterOrEqual(v, 0f);
            Assert.LessOrEqual(v, 1f);
        }
    }
}
