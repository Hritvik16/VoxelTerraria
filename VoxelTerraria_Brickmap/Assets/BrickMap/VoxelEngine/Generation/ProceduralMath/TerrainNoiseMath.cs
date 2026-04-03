using Unity.Mathematics;

namespace VoxelEngine.Generation
{
    public static class TerrainNoiseMath
    {
        // 0: Base Terrain (Rolling Hills)
        public static float GetBaseHeight(float x, float z) {
            float2 pos = new float2(x, z);
            // THE FIX: Sea level is now exactly 0.
            float h = 0f; 
            h += noise.snoise(pos * 0.002f) * 40f;  
            h += noise.snoise(pos * 0.01f) * 15f;   
            h += noise.snoise(pos * 0.05f) * 4f;    
            h += noise.snoise(pos * 0.4f) * 0.6f;   
            return h;
        }

        // 10: Mountains (Erosion Masked)
        public static float GetMountainHeight(float baseHeight, float x, float z, float targetHeight) {
            float2 pos = new float2(x, z);
            
            // 1. Smooth, old weathered mountains
            float smoothMountain = noise.snoise(pos * 0.015f) * 0.5f + 0.5f;
            
            // 2. Sharp, young alpine peaks (Ridged Multifractal)
            float ridged = 0f;
            ridged += (1f - math.abs(noise.snoise(pos * 0.015f))) * 0.60f;
            ridged += (1f - math.abs(noise.snoise(pos * 0.040f))) * 0.30f;
            ridged += (1f - math.abs(noise.snoise(pos * 0.100f))) * 0.10f;
            ridged = ridged * ridged; // Square for extra sharpness
            
            // 3. The Erosion Mask (Slow moving noise dictates where the sharp peaks are)
            float erosionMask = noise.snoise(pos * 0.005f) * 0.5f + 0.5f;
            
            // Blend them together!
            float finalM = math.lerp(smoothMountain, ridged, erosionMask);
            return baseHeight + (finalM * targetHeight);
        }

        // 11: Plateaus / Mesas
        public static float GetPlateauHeight(float targetHeight) {
            // Because base terrain is now 0, targetHeight (e.g., 40f) will correctly form a raised Mesa!
            return targetHeight; 
        }

        // 12: Sweeping Dunes (Pure Ground Truth, No Warping)
        public static float GetDuneHeight(float baseHeight, float x, float z) {
            // THE FIX: Pure linear coordinate based on ground truth. No noise distortion.
            // Using (x + z) makes the perfectly straight dunes run diagonally across the grid.
            float linearCoord = (x + z) * 0.05f; 
            
            // Asymmetrical sine wave prevents the jagged voxel tearing.
            float wave = math.sin(linearCoord);
            float asymmetricalWave = wave - 0.4f * math.sin(linearCoord * 2.0f);
            
            float normalized = (asymmetricalWave + 1.4f) * 0.35f;
            float duneShape = normalized * normalized; 
            
            return baseHeight + (duneShape * 4.0f);
        }

        // 13: Terraced Steppes
        public static float GetSteppeHeight(float baseHeight) {
            float terraceHeight = 0.8f; 
            return math.floor(baseHeight / terraceHeight) * terraceHeight;
        }

        public static float GetHeight2D(float x, float z) {
            return GetBaseHeight(x, z);
        }
    }
}