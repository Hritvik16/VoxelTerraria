using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.World; 
using VoxelEngine.Generation; // Added

namespace VoxelEngine.World 
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
    public struct TerrainGenJob_1Bit : IJobParallelFor
    {
        public NativeArray<ChunkManager.ChunkJobData> jobQueue;
        [ReadOnly] public NativeArray<FeatureAnchor> features;
        [ReadOnly] public NativeArray<CavernNode> caverns;
        [ReadOnly] public NativeArray<TunnelSpline> tunnels; 
        
        // --- NEW: The Delivery Truck ---
        [ReadOnly] public NativeArray<ChunkManager.VoxelEdit> deltaMap;
        
        public int featureCount;
        public int cavernCount;
        public int tunnelCount; 
        public float worldRadiusXZ;
        public int worldSeed;

        [NativeDisableParallelForRestriction] public NativeArray<uint> denseChunkPool;
        [NativeDisableParallelForRestriction] public NativeArray<uint> macroMaskPool; 
        [NativeDisableParallelForRestriction] public NativeArray<float> chunkHeights; 

        public void Execute(int jobIndex)
        {
            ChunkManager.ChunkJobData job = jobQueue[jobIndex];
            uint denseBase = (uint)job.pad2 * 1024u; 

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
            float bhMid = TerrainNoiseMath.GetHeight2D(wStartX + 16f * job.layerScale, wStartZ + 16f * job.layerScale);
            
            float minBH = math.min(math.min(math.min(bh00, bh10), math.min(bh01, bh11)), bhMid) - 30f;
            float maxBH = math.max(math.max(math.max(bh00, bh10), math.max(bh01, bh11)), bhMid) + 30f;

            chunkHeights[job.mapIndex] = maxBH;

            bool isFullyUnderground = wEndY < minBH;
            bool isFullySky = wStartY > maxBH;

            if (isFullySky) {
                // THE FIX: Never skip a chunk if the player built something in it!
                if (job.editCount == 0) {
                    var modifiedJob = jobQueue[jobIndex];
                    modifiedJob.pad3 = 1; 
                    jobQueue[jobIndex] = modifiedJob;

                    for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; 
                    
                    int maskBase1 = job.pad2 * 19;
                    for (int i = 0; i < 19; i++) macroMaskPool[maskBase1 + i] = 0; 
                    return; 
                }
            }

            if (isFullyUnderground) {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = uint.MaxValue; // Fill with 1s
                CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
            } 
            else {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; // Clear memory first

                for (int z = 0; z < 32; z++) {
                    float zPos = (job.worldPos.z + z) * job.layerScale; 
                    for (int x = 0; x < 32; x++) {
                        float xPos = (job.worldPos.x + x) * job.layerScale;
                        float h = TerrainNoiseMath.GetHeight2D(xPos, zPos);

                        for (int y = 0; y < 32; y++) {
                            float yPos = (job.worldPos.y + y) * job.layerScale; 
                            
                            if (yPos <= h) {
                                int flatIdx = x + (y << 5) + (z << 10);
                                int uintIdx = flatIdx >> 5;
                                int bitIdx = flatIdx & 31;
                                denseChunkPool[(int)denseBase + uintIdx] |= (1u << bitIdx);
                            }
                        }
                    }
                }
                CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
            }

            // --- THE DELTA OVERWRITE (Absolute Authority) ---
            if (job.editCount > 0) {
                for (int e = 0; e < job.editCount; e++) {
                    ChunkManager.VoxelEdit edit = deltaMap[job.editStartIndex + e];
                    
                    int flatIdx = edit.flatIndex;
                    int uintIdx = flatIdx >> 5;
                    int bitIdx = flatIdx & 31;

                    // 1-Bit Architecture: Material > 0 means Solid (1), Material == 0 means Air (0)
                    if (edit.material > 0) {
                        denseChunkPool[(int)denseBase + uintIdx] |= (1u << bitIdx); 
                    } else {
                        denseChunkPool[(int)denseBase + uintIdx] &= ~(1u << bitIdx); 
                    }
                }
            }

            int maskBase = job.pad2 * 19;
            MacroMaskBaker.Bake1Bit(ref denseChunkPool, denseBase, ref macroMaskPool, maskBase);
        }
    }
}