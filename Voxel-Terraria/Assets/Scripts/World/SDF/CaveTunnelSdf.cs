using Unity.Mathematics;

namespace VoxelTerraria.World.SDF
{
    public static class CaveTunnelSdf
    {
        public static float Evaluate(float3 p, in Feature f)
        {
            // data0: Start Point (x, y, z)
            float3 start = f.data0;

            // data1: End Point (x, y, z)
            float3 end = f.data1;

            // data2: Radius, NoiseFreq, NoiseAmp
            float radius = f.data2.x;
            float noiseFreq = f.data2.y;
            float noiseAmp = f.data2.z;

            // data3: Seed
            float seed = f.data3.x;

            // Capsule SDF (Segment SDF)
            float3 pa = p - start;
            float3 ba = end - start;
            float h = math.clamp(math.dot(pa, ba) / math.dot(ba, ba), 0.0f, 1.0f);
            float dist = math.length(pa - ba * h) - radius;

            // Add Noise
            if (noiseAmp > 0.001f)
            {
                float noise = NoiseUtils.Noise3D(p * noiseFreq + seed, 1f, 1f);
                dist += noise * noiseAmp;
            }

            return dist;
        }
    }
}
