// 文件路径: Runtime/Jobs/PathJobsUtility.cs (曲线烘焙版)
using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MrPathV2
{
    public static class PathJobsUtility
    {
        // ... SpineData 结构体保持不变 ...
        public struct SpineData : IDisposable 
        {
            [ReadOnly] public NativeArray<float3> points;
            [ReadOnly] public NativeArray<float3> tangents;
            [ReadOnly] public NativeArray<float3> normals;
            public bool IsCreated => points.IsCreated;
            public int Length => points.Length;

            public SpineData(PathSpine spine, Allocator allocator)
            {
                points = new NativeArray<float3>(spine.VertexCount, allocator);
                tangents = new NativeArray<float3>(spine.VertexCount, allocator);
                normals = new NativeArray<float3>(spine.VertexCount, allocator);
                for (int i = 0; i < spine.VertexCount; i++)
                {
                    points[i] = spine.points[i];
                    tangents[i] = spine.tangents[i];
                    normals[i] = spine.surfaceNormals[i];
                }
            }
            public void Dispose()
            {
                if(points.IsCreated) points.Dispose();
                if(tangents.IsCreated) tangents.Dispose();
                if(normals.IsCreated) normals.Dispose();
            }
        }


        public struct ProfileData : IDisposable
        {
            [ReadOnly] public float roadWidth;
            [ReadOnly] public float falloffWidth;
            [ReadOnly] public bool forceHorizontal;
            [ReadOnly] public int crossSectionSegments;

            [ReadOnly] private NativeArray<float> _bakedCrossSection;
            [ReadOnly] private NativeArray<float> _bakedFalloff;

            public bool IsCreated => _bakedCrossSection.IsCreated;
            private const int BAKE_RESOLUTION = 64;

            public ProfileData(PathProfile profile, Allocator allocator)
            {
                roadWidth = profile.roadWidth;
                falloffWidth = profile.falloffWidth;
                forceHorizontal = profile.forceHorizontal;
                crossSectionSegments = profile.crossSectionSegments;

                _bakedCrossSection = new NativeArray<float>(BAKE_RESOLUTION, allocator);
                BakeCurve(profile.crossSection, _bakedCrossSection, -1, 1);

                _bakedFalloff = new NativeArray<float>(BAKE_RESOLUTION, allocator);
                BakeCurve(profile.falloffShape, _bakedFalloff, 0, 1);
            }

            public float EvaluateCrossSection(float t) => EvaluateBakedCurve(_bakedCrossSection, t, -1, 1);
            public float EvaluateFalloff(float t) => EvaluateBakedCurve(_bakedFalloff, t, 0, 1);

            private static void BakeCurve(AnimationCurve curve, NativeArray<float> bakedData, float start, float end)
            {
                for (int i = 0; i < BAKE_RESOLUTION; i++)
                {
                    float time = math.lerp(start, end, i / (float)(BAKE_RESOLUTION - 1));
                    bakedData[i] = curve.Evaluate(time);
                }
            }

            private static float EvaluateBakedCurve(NativeArray<float> bakedData, float t, float start, float end)
            {
                float normalizedT = math.saturate((t - start) / (end - start));
                float floatIndex = normalizedT * (BAKE_RESOLUTION - 1);
                int indexA = (int)math.floor(floatIndex);
                int indexB = (int)math.ceil(floatIndex);
                if (indexA == indexB) return bakedData[indexA];
                return math.lerp(bakedData[indexA], bakedData[indexB], floatIndex - indexA);
            }

            public void Dispose()
            {
                if (_bakedCrossSection.IsCreated) _bakedCrossSection.Dispose();
                if (_bakedFalloff.IsCreated) _bakedFalloff.Dispose();
            }
        }
    }
}