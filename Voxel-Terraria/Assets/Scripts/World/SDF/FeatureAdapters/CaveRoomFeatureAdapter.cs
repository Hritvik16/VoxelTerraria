using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.Data.Features;

namespace VoxelTerraria.World.SDF.FeatureAdapters
{
    public static class CaveRoomFeatureAdapter
    {
        private static bool s_registered;

        public static void EnsureRegistered()
        {
            if (s_registered) return;

            FeatureBounds3DComputer.Register(
                FeatureType.CaveRoom,
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
            // data0: Center (x, y, z)
            center = f.data0;

            // data1: Radius, NoiseFreq, NoiseAmp
            float radius = f.data1.x;
            float noiseAmp = f.data1.z;

            // Max possible extent is radius + noise amplitude
            float maxExt = radius + noiseAmp;

            halfExtents = new float3(maxExt, maxExt, maxExt);
        }
    }
}
