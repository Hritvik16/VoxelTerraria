using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.World; 
using VoxelEngine.Generation; // Added

namespace VoxelEngine.World 
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
    public struct TerrainGenJob_32Bit : IJobParallelFor
    {
        public NativeArray<ChunkManager.ChunkJobData> jobQueue;
        [ReadOnly] public NativeArray<FeatureAnchor> features;
        [ReadOnly] public NativeArray<CavernNode> caverns;
        [ReadOnly] public NativeArray<TunnelSpline> tunnels; 
        
        public int featureCount;
        public int cavernCount;
        public int tunnelCount; 
        public float worldRadiusXZ;
        public int worldSeed;

        [NativeDisableParallelForRestriction] public NativeArray<uint> denseChunkPool;
        [NativeDisableParallelForRestriction] public NativeArray<uint> macroMaskPool;

        public void Execute(int jobIndex)
        {
            ChunkManager.ChunkJobData job = jobQueue[jobIndex];
            uint denseBase = (uint)job.pad2 * 32768u; // 32-Bit base

            float wStartX = job.worldPos.x * job.layerScale;
            float wStartZ = job.worldPos.z * job.layerScale;
            float wStartY = job.worldPos.y * job.layerScale;
            float wEndY   = (job.worldPos.y + 32f) * job.layerScale;
            float wEndX   = (job.worldPos.x + 32f) * job.layerScale;
            float wEndZ   = (job.worldPos.z + 32f) * job.layerScale;

            float bh00 = TerrainNoiseMath.GetHeight2D(wStartX, wStartZ);
            float bh10 = TerrainNoiseMath.GetHeight2D(wEndX, wStartZ);
            float bh01 = TerrainNoiseMath.GetHeight2D(wStartX, wEndZ);
            float bh11 = TerrainNoiseMath.GetHeight2D(wEndX, wEndZ);

            float minBH = math.min(math.min(bh00, bh10), math.min(bh01, bh11)) - 15f;
            float maxBH = math.max(math.max(bh00, bh10), math.max(bh01, bh11)) + 15f;

            bool isFullyUnderground = wEndY < minBH;
            bool isFullySky = wStartY > maxBH;

            if (isFullySky) {
                var modifiedJob = jobQueue[jobIndex];
                modifiedJob.pad3 = 1; 
                jobQueue[jobIndex] = modifiedJob;

                for (int i = 0; i < 32768; i++) denseChunkPool[(int)denseBase + i] = 0; 
                return; 
            }

            if (isFullyUnderground) {
                for (int i = 0; i < 32768; i++) denseChunkPool[(int)denseBase + i] = 1; // Solid Stone
                CaveCarverWorker.ApplyCavesAndTunnels_32Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
                return; 
            }

            for (int z = 0; z < 32; z++) {
                float zPos = (job.worldPos.z + z) * job.layerScale; 

                for (int x = 0; x < 32; x += 4) {
                    float x0 = (job.worldPos.x + x)     * job.layerScale;
                    float x1 = (job.worldPos.x + x + 1) * job.layerScale;
                    float x2 = (job.worldPos.x + x + 2) * job.layerScale;
                    float x3 = (job.worldPos.x + x + 3) * job.layerScale;

                    float h0 = TerrainNoiseMath.GetHeight2D(x0, zPos);
                    float h1 = TerrainNoiseMath.GetHeight2D(x1, zPos);
                    float h2 = TerrainNoiseMath.GetHeight2D(x2, zPos);
                    float h3 = TerrainNoiseMath.GetHeight2D(x3, zPos);

                    for (int y = 0; y < 32; y++) {
                        float yPos = (job.worldPos.y + y) * job.layerScale; 

                        uint m0 = yPos <= h0 ? (yPos > h0 - 2f ? 2u : 1u) : 0u; 
                        uint m1 = yPos <= h1 ? (yPos > h1 - 2f ? 2u : 1u) : 0u;
                        uint m2 = yPos <= h2 ? (yPos > h2 - 2f ? 2u : 1u) : 0u;
                        uint m3 = yPos <= h3 ? (yPos > h3 - 2f ? 2u : 1u) : 0u;

                        int flatIdx = x + (y << 5) + (z << 10);
                        denseChunkPool[(int)denseBase + flatIdx]     = m0;
                        denseChunkPool[(int)denseBase + flatIdx + 1] = m1;
                        denseChunkPool[(int)denseBase + flatIdx + 2] = m2;
                        denseChunkPool[(int)denseBase + flatIdx + 3] = m3;
                    }
                }
            }

            CaveCarverWorker.ApplyCavesAndTunnels_32Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
            
            int maskBase = job.pad2 * 16;
            MacroMaskBaker.Bake32Bit(ref denseChunkPool, denseBase, ref macroMaskPool, maskBase);
        }
    }
}