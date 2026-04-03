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

            // 1. THE ZERO-ALLOCATION CACHE
            // Allocator.Temp is a 1-cycle thread stack allocation in Burst. Zero garbage, zero atomic locks.
            NativeArray<float> localHeights = new NativeArray<float>(1024, Allocator.Temp);

            float chunkCenterX = wStartX + (16f * job.layerScale);
            float chunkCenterZ = wStartZ + (16f * job.layerScale);
            float chunkRadius = 16f * 1.414f * job.layerScale;
            
            float exactMinH = 99999f;
            float exactMaxH = -99999f;

            // --- THE BUG FIX: CHUNK-LEVEL CULLING ---
            // Find all features touching THIS chunk exactly ONCE, saving millions of loops!
            NativeArray<FeatureAnchor> activeFeatures = new NativeArray<FeatureAnchor>(128, Allocator.Temp);
            int activeFeatureCount = 0;
            for (int f = 0; f < featureCount; f++) {
                FeatureAnchor anchor = features[f];
                if (math.distance(new float2(chunkCenterX, chunkCenterZ), anchor.position) <= anchor.radius + chunkRadius) {
                    if (activeFeatureCount < 128) {
                        activeFeatures[activeFeatureCount] = anchor;
                        activeFeatureCount++;
                    }
                }
            }

            // 2. THE DIRECTED FILTER EVALUATION
            for (int z = 0; z < 32; z++) {
                float zPos = wStartZ + (z * job.layerScale);
                for (int x = 0; x < 32; x++) {
                    float xPos = wStartX + (x * job.layerScale);

                    float baseH = TerrainNoiseMath.GetBaseHeight(xPos, zPos);
                    float blendedFeatureHeight = 0f;
                    float totalWeight = 0f;

                    // Inverse Distance Weighting (IDW) using ONLY the 1 or 2 active features
                    for (int f = 0; f < activeFeatureCount; f++) {
                        FeatureAnchor anchor = activeFeatures[f];
                        
                        float dist = math.distance(new float2(xPos, zPos), anchor.position);
                        if (dist < anchor.radius) {
                            float weight = math.smoothstep(anchor.radius, anchor.radius * 0.2f, dist);
                            float featureH = baseH;

                            if (anchor.topologyID == 10) featureH = TerrainNoiseMath.GetMountainHeight(baseH, xPos, zPos, anchor.heightMod);
                            else if (anchor.topologyID == 11) featureH = TerrainNoiseMath.GetPlateauHeight(anchor.heightMod);
                            else if (anchor.topologyID == 12) featureH = TerrainNoiseMath.GetDuneHeight(baseH, xPos, zPos);
                            else if (anchor.topologyID == 13) featureH = TerrainNoiseMath.GetSteppeHeight(baseH);
                            
                            blendedFeatureHeight += featureH * weight;
                            totalWeight += weight;
                        }
                    }

                    float finalH = baseH;
                    if (totalWeight > 0f) {
                        float normalizedWeight = math.min(1.0f, totalWeight);
                        finalH = math.lerp(baseH, blendedFeatureHeight / totalWeight, normalizedWeight);
                    }

                    localHeights[x + (z << 5)] = finalH;
                    
                    if (finalH < exactMinH) exactMinH = finalH;
                    if (finalH > exactMaxH) exactMaxH = finalH;
                }
            }
            activeFeatures.Dispose();

            // --- THE NEW EXACT CULLING ---
            float dynamicPad = 5f + (job.layerScale * 2f); // Padding can be much smaller now because we have EXACT max height!
            float minBH = exactMinH - dynamicPad;
            float maxBH = exactMaxH + dynamicPad;

            chunkHeights[job.mapIndex] = maxBH;

            bool isFullyUnderground = wEndY < minBH;
            bool isFullySky = wStartY > maxBH;

            if (isFullySky) {
                if (job.editCount == 0) {
                    var modifiedJob = jobQueue[jobIndex];
                    modifiedJob.pad3 = 1; 
                    jobQueue[jobIndex] = modifiedJob;

                    for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; 
                    
                    int maskBase1 = job.pad2 * 19;
                    for (int i = 0; i < 19; i++) macroMaskPool[maskBase1 + i] = 0; 
                    
                    localHeights.Dispose(); // Cleanup the stack memory
                    return; 
                }
            }

            bool isDistantLOD = job.layerScale >= 0.8f;

            if (isFullyUnderground) {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = uint.MaxValue; 
                if (!isDistantLOD) {
                    CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
                }
            } 
            else {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; 

                // 3. THE ZERO-MATH VOXEL LOOP
                for (int z = 0; z < 32; z++) {
                    for (int x = 0; x < 32; x++) {
                        float h = localHeights[x + (z << 5)]; // O(1) Lookup!

                        for (int y = 0; y < 32; y++) {
                            float yPos = wStartY + (y * job.layerScale); 
                            
                            if (yPos <= h) {
                                int flatIdx = x + (y << 5) + (z << 10);
                                int uintIdx = flatIdx >> 5;
                                int bitIdx = flatIdx & 31;
                                denseChunkPool[(int)denseBase + uintIdx] |= (1u << bitIdx);
                            }
                        }
                    }
                }
                
                if (!isDistantLOD) {
                    CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
                }
            }
            
            localHeights.Dispose(); // Cleanup the stack memory

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