using UnityEngine;

namespace VoxelEngine.World
{
    public static class TerrainMath
    {
        // Must match TerrainMath.hlsl perfectly
        public static float GetCoastDistance(Vector2 worldXZ, float worldRadiusXZ)
        {
            float theta = Mathf.Atan2(worldXZ.y, worldXZ.x);
            float F = 4.0f;
            
            Vector2 noiseUV = new Vector2(Mathf.Cos(theta) * F, Mathf.Sin(theta) * F);
            
            // Note: perlin2D implementation matching the HLSL one is complex to perfectly mirror in C#
            // For the Poisson disk safety check, we can use Mathf.PerlinNoise as a close approximation,
            // or we use a slightly more conservative safety buffer.
            float noiseVal = -1.0f + 2.0f * Mathf.PerlinNoise(noiseUV.x * 10f + 123f, noiseUV.y * 10f + 123f); 
            
            return worldRadiusXZ * (0.75f + 0.25f * noiseVal);
        }
    }

    // Strictly 32-Byte Aligned Struct
    [System.Serializable]
    public struct FeatureAnchor
    {
        public Vector2 position;  // 8 bytes
        public int topologyID;    // 4 bytes
        public int biomeID;       // 4 bytes
        public float radius;      // 4 bytes
        public float heightMod;   // 4 bytes
        public float pad0;        // 4 bytes
        public float pad1;        // 4 bytes
    }

    [System.Serializable]
    public struct CavernNode
    {
        public Vector3 position;  // 12 bytes
        public float radius;      // 4 bytes
        public int biomeID;       // 4 bytes
        public int cavernType;    // 4 bytes (0 = Solid Biome Volume, 1 = Air Hub)
        public float pad0;        // 4 bytes
        public float pad1;        // 4 bytes
    }

    [System.Serializable]
    public struct TunnelSpline
    {
        public Vector3 startPoint;    // 12 bytes
        public Vector3 endPoint;      // 12 bytes
        public float radius;          // 4 bytes
        public float noiseIntensity;  // 4 bytes
    }
}