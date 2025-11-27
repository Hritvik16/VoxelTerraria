using Unity.Mathematics;

namespace VoxelTerraria.World.SDF
{
    public static class CaveRoomSdf
    {
        public static float Evaluate(float3 p, in Feature f)
        {
            // data0: Center (x, y, z)
            float3 center = f.data0;

            // data1: Radius, NoiseFreq, NoiseAmp
            float radius = f.data1.x;
            float noiseFreq = f.data1.y;
            float noiseAmp = f.data1.z;

            // data2: Seed
            float seed = f.data2.x;

            // Basic Sphere SDF
            float dist = math.distance(p, center) - radius;

            // Add Noise
            if (noiseAmp > 0.001f)
            {
                float noise = NoiseUtils.Noise3D(p * noiseFreq + seed, 1f, 1f);
                dist += noise * noiseAmp;
            }

            // Invert for subtraction (negative = inside hole)
            // But wait, the system handles subtraction by max(sdf, -featureSdf).
            // So we should return NEGATIVE inside the feature (standard SDF).
            // Then the system does: sdf = max(sdf, -(-val)) = max(sdf, val).
            // Wait, standard SDF: negative inside.
            // If we want to carve a hole, we want the terrain SDF to become POSITIVE (air) inside the hole.
            // Terrain SDF: negative = solid, positive = air.
            // We want to INCREASE the SDF value inside the hole.
            // CombinedTerrainSdf:
            // if (Subtract) sdf = max(sdf, -s);
            // If 's' is negative inside the sphere (e.g. -10), then -s is +10.
            // max(existing, +10) -> +10 (Air). Correct.
            
            return dist;
        }
    }
}
