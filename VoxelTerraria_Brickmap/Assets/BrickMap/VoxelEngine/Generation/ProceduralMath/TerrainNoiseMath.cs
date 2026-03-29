using Unity.Mathematics;

namespace VoxelEngine.Generation
{
    public static class TerrainNoiseMath
    {
        // --- THE NEW AAA 2D SURFACE MATH ---
        public static float GetHeight2D(float x, float z) {
            float2 pos = new float2(x, z);
            float h = 60f; // Base sea level
            h += noise.snoise(pos * 0.002f) * 40f;  // Octave 1: Continental shifts
            h += noise.snoise(pos * 0.01f) * 15f;   // Octave 2: Rolling Hills
            h += noise.snoise(pos * 0.05f) * 4f;    // Octave 3: Boulders/Cliffs
            h += noise.snoise(pos * 0.2f) * 0.5f;   // Octave 4: 0.2m Surface Crunch
            return h;
        }
    }
}
