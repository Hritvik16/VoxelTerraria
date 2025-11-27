using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.Data.Features;

namespace VoxelTerraria.World.SDF.FeatureAdapters
{
    public static class CaveTunnelFeatureAdapter
    {
        private static bool s_registered;

        public static void EnsureRegistered()
        {
            if (s_registered) return;

            FeatureBounds3DComputer.Register(
                FeatureType.CaveTunnel,
                ComputeAnalyticBounds
            );

            s_registered = true;
        }

        private static void ComputeAnalyticBounds(
            in Feature f,
            WorldSettings settings,
            out float3 center,
            out float3 halfExtents)
        {
            // data0: Start Point
            float3 start = f.data0;
            // data1: End Point
            float3 end = f.data1;

            // data2: Radius, NoiseFreq, NoiseAmp
            float radius = f.data2.x;
            float noiseAmp = f.data2.z;

            float maxRadius = radius + noiseAmp;

            float3 min = math.min(start, end) - maxRadius;
            float3 max = math.max(start, end) + maxRadius;

            center = (min + max) * 0.5f;
            halfExtents = (max - min) * 0.5f;
        }
    }
}
