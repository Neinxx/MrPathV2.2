// 文件路径: Runtime/Jobs/PathJobsUtility.cs (曲线烘焙版)

using System;
using __temp.MrPathV2._2.Runtime.Core;
using __temp.MrPathV2._2.Runtime.Jobs.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using NativeArrayExtensions = __temp.MrPathV2._2.Runtime.Jobs.Extensions.NativeArrayExtensions;

namespace __temp.MrPathV2._2.Runtime.Jobs
{
    public static class PathJobsUtility
    {
        // ... SpineData 结构体保持不变 ...
        public struct SpineData : IDisposable
        {
            public NativeArray<float3> Points;
            public NativeArray<float3> Tangents;
            public NativeArray<float3> Normals;

            public int Length => Points.IsCreated ? Points.Length : 0;
            public bool IsCreated => Points.IsCreated && Tangents.IsCreated && Normals.IsCreated;

            public SpineData(PathSpine spine, Allocator allocator)
            {
                Points = NativeArrayExtensions.CreateTracked<float3>(spine.VertexCount, allocator);
                Tangents = NativeArrayExtensions.CreateTracked<float3>(spine.VertexCount, allocator);
                Normals = NativeArrayExtensions.CreateTracked<float3>(spine.VertexCount, allocator);
                
                for (int i = 0; i < spine.VertexCount; i++)
                {
                    Points[i] = spine.points[i];
                    Tangents[i] = spine.tangents[i];
                    Normals[i] = spine.surfaceNormals[i];
                }
            }
            
            public void Dispose()
            {
                Points.SafeDispose();
                Tangents.SafeDispose();
                Normals.SafeDispose();
            }
        }


        public struct ProfileData : IDisposable
        {
            [ReadOnly] public float RoadWidth;
            [ReadOnly] public float FalloffWidth;
            [ReadOnly] public bool ForceHorizontal;
            [ReadOnly] public int CrossSectionSegments;

            private NativeArray<float> _bakedCrossSection;
            private NativeArray<float> _bakedFalloff;

            private const int BakeResolution = 64;

            public bool IsCreated => _bakedCrossSection.IsCreated && _bakedFalloff.IsCreated;

            public ProfileData(PathProfile profile, Allocator allocator)
            {
                RoadWidth = profile.roadWidth;
                FalloffWidth = profile.falloffWidth;
                ForceHorizontal = profile.forceHorizontal;
                CrossSectionSegments = profile.crossSectionSegments;

                _bakedCrossSection = NativeArrayExtensions.CreateTracked<float>(BakeResolution, allocator);
                _bakedFalloff = NativeArrayExtensions.CreateTracked<float>(BakeResolution, allocator);
                
                BakeCurve(profile.crossSection, _bakedCrossSection, -1, 1);
                BakeCurve(profile.falloffShape, _bakedFalloff, 0, 1);
            }

            public float EvaluateCrossSection(float t) => EvaluateBakedCurve(_bakedCrossSection, t, -1, 1);
            public float EvaluateFalloff(float t) => EvaluateBakedCurve(_bakedFalloff, t, 0, 1);

            private static void BakeCurve(AnimationCurve curve, NativeArray<float> bakedData, float start, float end)
            {
                for (int i = 0; i < BakeResolution; i++)
                {
                    float time = math.lerp(start, end, i / (float)(BakeResolution - 1));
                    bakedData[i] = curve.Evaluate(time);
                }
            }

            private static float EvaluateBakedCurve(NativeArray<float> bakedData, float t, float start, float end)
            {
                float normalizedT = math.saturate((t - start) / (end - start));
                float floatIndex = normalizedT * (BakeResolution - 1);
                int indexA = (int)math.floor(floatIndex);
                int indexB = (int)math.ceil(floatIndex);
                if (indexA == indexB) return bakedData[indexA];
                return math.lerp(bakedData[indexA], bakedData[indexB], floatIndex - indexA);
            }

            public void Dispose()
            {
                _bakedCrossSection.SafeDispose();
                _bakedFalloff.SafeDispose();
            }
        }
        public static SpineData CreateSpineData(PathSpine spine, Allocator allocator)
        {
            return new SpineData(spine, allocator);
        }

        public static ProfileData CreateProfileData(PathProfile profile, Allocator allocator)
        {
            return new ProfileData(profile, allocator);
        }
    }
}