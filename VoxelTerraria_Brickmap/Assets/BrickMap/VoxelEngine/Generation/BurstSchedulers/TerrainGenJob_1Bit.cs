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
        public int featureCount;
        public int cavernCount;
        public int tunnelCount; 
        public float worldRadiusXZ;
        public int worldSeed;

        [NativeDisableParallelForRestriction] public NativeArray<uint> denseChunkPool;
        [NativeDisableParallelForRestriction] public NativeArray<uint> macroMaskPool; 
        [NativeDisableParallelForRestriction] public NativeArray<float> chunkHeights; 
        [NativeDisableParallelForRestriction] public NativeArray<uint> jobMaterialUpload; // THE FIX: Burst writes to Courier, not Master!
        [NativeDisableParallelForRestriction] public NativeArray<uint> cpuSurfaceMaskPool; 
        [NativeDisableParallelForRestriction] public NativeArray<uint> cpuSurfacePrefixPool; 
        [NativeDisableParallelForRestriction] public NativeArray<uint> cpuShadowRAMPool; // NEW

        [ReadOnly] public NativeArray<ChunkManager.BiomeAnchor> biomes;
        public int biomeCount;

        private float Hash(float3 p3) {
            p3  = math.frac(p3 * 0.1031f);
            p3 += math.dot(p3, new float3(p3.y, p3.z, p3.x) + 33.33f);
            return math.frac((p3.x + p3.y) * p3.z);
        }

        public void Execute(int jobIndex)
        {
            ChunkManager.ChunkJobData job = jobQueue[jobIndex];
            uint denseBase = (uint)job.pad2 * 1024u; 

            // --- NEW: RAM VAULT BYPASS ---
            if (job.editCount == 1) {
                // Set bounds very high, because player edits could be anywhere!
                chunkHeights[job.mapIndex] = (job.worldPos.y + 32f) * job.layerScale;
                return; // THE GREAT PURGE: No Math! No Search! Just O(1) Instant Return!
            }

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

            // --- THE NEW FIX: BIOME CULLING ---
            NativeArray<ChunkManager.BiomeAnchor> activeBiomes = new NativeArray<ChunkManager.BiomeAnchor>(32, Allocator.Temp);
            int activeBiomeCount = 0;
            
            float minChunkDist = 999999.0f;
            for (int b = 0; b < biomeCount; b++) {
                float dist = math.distance(new float2(chunkCenterX, chunkCenterZ), new float2(biomes[b].position.x, biomes[b].position.z));
                if (dist < minChunkDist) minChunkDist = dist;
            }
            
            float biomeSearchRadius = minChunkDist + 350.0f + (32f * job.layerScale);
            for (int b = 0; b < biomeCount; b++) {
                float dist = math.distance(new float2(chunkCenterX, chunkCenterZ), new float2(biomes[b].position.x, biomes[b].position.z));
                if (dist <= biomeSearchRadius && activeBiomeCount < 32) {
                    activeBiomes[activeBiomeCount] = biomes[b];
                    activeBiomeCount++;
                }
            }
            
            // Fallback (just in case)
            if (activeBiomeCount == 0 && biomeCount > 0) {
                activeBiomes[0] = biomes[0];
                activeBiomeCount = 1;
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
                    activeBiomes.Dispose(); // Cleanup the biome array memory
                    // Since localMaterials wasn't allocated yet, no need to dispose it here!
                    return; 
                }
            }

            bool isDistantLOD = job.layerScale >= 0.8f;

            // OPTIMIZATION: Temp array clears to 0 instantly. Zero GC.
            NativeArray<uint> tempRawMaterials = new NativeArray<uint>(8192, Allocator.Temp);
            NativeArray<uint> packedMaterials = new NativeArray<uint>(4096, Allocator.Temp); 
            NativeArray<uint> localSurfaceMask = new NativeArray<uint>(1024, Allocator.Temp);
            NativeArray<uint> localSurfacePrefix = new NativeArray<uint>(1024, Allocator.Temp);

            // THE FIX: Explicitly zero-out Burst's uninitialized stack memory!
            for (int i = 0; i < 4096; i++) packedMaterials[i] = 0;
            for (int i = 0; i < 1024; i++) {
                localSurfaceMask[i] = 0;
                localSurfacePrefix[i] = 0;
            }

            if (isFullyUnderground) {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = uint.MaxValue; 
                // OPTIMIZATION: 0x03030303 writes 4 Stone blocks (Index 3) in a single CPU cycle.
                for (int i = 0; i < 8192; i++) tempRawMaterials[i] = 0x03030303; 

                if (!isDistantLOD) {
                    CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
                }
            } 
            else {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; 
                for (int i = 0; i < 8192; i++) tempRawMaterials[i] = 0; // THE FIX: Clear Burst's Temp uninitialized garbage!

                // 3. THE HIGHLY OPTIMIZED VOXEL LOOP
                for (int z = 0; z < 32; z++) {
                    for (int x = 0; x < 32; x++) {
                        float h = localHeights[x + (z << 5)];
                        
                        // BIOME LOOKUP (Once per XZ column! Extremely fast compared to doing it per voxel!)
                        float worldX = job.worldPos.x * job.layerScale + x * job.layerScale;
                        float worldZ = job.worldPos.z * job.layerScale + z * job.layerScale;
                        float3 worldPos2D = new float3(worldX, 0, worldZ); 
                        float boundaryWarp = (Hash(worldPos2D * 0.005f) - 0.5f) * 150.0f;
                        
                        int currentBiome = 0;
                        float closestDist = 999999.0f;
                        for (int b = 0; b < activeBiomeCount; b++) {
                            float dist = math.distance(worldPos2D, new float3(activeBiomes[b].position.x, 0, activeBiomes[b].position.z)) + boundaryWarp;
                            if (dist < closestDist) {
                                closestDist = dist;
                                currentBiome = activeBiomes[b].biomeType;
                            }
                        }

                        // INTEGER BOUNDARY OPTIMIZATION: Zero floats in the Y-loop!
                        int trueYMax = (int)((h - wStartY) / job.layerScale);
                        int trueYDirt = (int)((h - 20.0f - wStartY) / job.layerScale);
                        int loopYMax = math.clamp(trueYMax, -1, 31);

                        for (int y = 0; y <= loopYMax; y++) {
                            int flatIdx = x + (y << 5) + (z << 10);
                            
                            // 1-Bit Shape Fast-Write
                            denseChunkPool[(int)denseBase + (flatIdx >> 5)] |= (1u << (flatIdx & 31));

                            // 8-Bit Material Fast-Write 
                            uint matID = 3; 

                            if (y == trueYMax) {
                                switch (currentBiome) {
                                    case 0: matID = 1u; break;   // FOREST Grass
                                    case 1: matID = 4u; break;   // DESERT Sand
                                    case 2: matID = 7u; break;   // SNOW Snow
                                    case 3: matID = 10u; break;  // JUNGLE Dark Grass
                                    case 4: matID = 14u; break;  // VOLCANIC Lava
                                    default: matID = 1u; break;
                                }
                            } else if (y > trueYDirt) {
                                switch (currentBiome) {
                                    case 0: matID = 2u; break;   // FOREST Dirt
                                    case 1: matID = 5u; break;   // DESERT Soft Sandstone
                                    case 2: matID = 8u; break;   // SNOW Ice
                                    case 3: matID = 12u; break;  // JUNGLE Mud
                                    case 4: matID = 11u; break;  // VOLCANIC Ash
                                    default: matID = 2u; break;
                                }
                            } else {
                                switch (currentBiome) {
                                    case 0: matID = 3u; break;   // FOREST Stone
                                    case 1: matID = 6u; break;   // DESERT Hard Sandstone
                                    case 2: matID = 3u; break;   // SNOW Stone
                                    case 3: matID = 3u; break;   // JUNGLE Stone
                                    case 4: matID = 13u; break;  // VOLCANIC Dark Slate
                                    default: matID = 3u; break;
                                }
                            }

                            int mUintIdx = flatIdx >> 2; 
                            int shift = (flatIdx & 3) << 3; 
                            tempRawMaterials[mUintIdx] |= (matID << shift);
                        }
                    }
                }
                
                if (!isDistantLOD) {
                    CaveCarverWorker.ApplyCavesAndTunnels_1Bit(ref denseChunkPool, denseBase, job.worldPos.x * job.layerScale, job.worldPos.y * job.layerScale, job.worldPos.z * job.layerScale, job.layerScale, caverns, cavernCount, tunnels, tunnelCount);
                }
            }
            
            localHeights.Dispose(); // Cleanup the stack memory
            activeBiomes.Dispose(); // Cleanup the biome array memory

            // --- NEW: ULTRA-FAST BITWISE SURFACE PACKER ---
            // Processes 32 voxels per CPU clock cycle. 1024 iterations instead of 32768.
            int packedCount = 0;
            
            for (int uintIdx = 0; uintIdx < 1024; uintIdx++) {
                uint row = denseChunkPool[(int)denseBase + uintIdx];
                if (row == 0) continue; // Entire row is pure air. Skip instantly!

                int y = uintIdx & 31;
                int z = uintIdx >> 5;
                uint surfaceMask = 0;

                // The outer chunk shell is always flagged as surface
                if (y == 0 || y == 31 || z == 0 || z == 31) {
                    surfaceMask = row;
                } else {
                    // Check Left/Right neighbors using bit-shifts
                    uint nx = (row << 1) | 1u;
                    uint px = (row >> 1) | 0x80000000u;
                    
                    // Check Up/Down/Forward/Back using adjacent memory addresses
                    uint ny = denseChunkPool[(int)denseBase + uintIdx - 1];
                    uint py = denseChunkPool[(int)denseBase + uintIdx + 1];
                    uint nz = denseChunkPool[(int)denseBase + uintIdx - 32];
                    uint pz = denseChunkPool[(int)denseBase + uintIdx + 32];
                    
                    // If a voxel has 6 solid neighbors, the bit in this mask will be 1
                    uint neighborsSolid = nx & px & ny & py & nz & pz;
                    
                    // Surface = Solid AND (Has an air neighbor OR touches X boundary)
                    surfaceMask = row & (~neighborsSolid | 0x80000001u);
                }

                if (surfaceMask != 0) {
                    localSurfaceMask[uintIdx] = surfaceMask;
                    uint tempMask = surfaceMask;
                    
                    // Extract exactly the set bits with zero loop overhead
                    while (tempMask != 0 && packedCount < 16384) {
                        int bitIdx = math.tzcnt(tempMask); // Instantly finds the next surface block
                        tempMask ^= (1u << bitIdx);        // Clears it from the queue
                        
                        int flatIdx = (uintIdx << 5) | bitIdx;
                        
                        // Extract material from 8-bit temp pool
                        int mUintIdx = flatIdx >> 2;
                        int mShift = (flatIdx & 3) << 3;
                        uint matID = (tempRawMaterials[mUintIdx] >> mShift) & 0xFF;
                        
                        // Compact it into the 4096-sized array
                        int pUintIdx = packedCount >> 2;
                        int pShift = (packedCount & 3) << 3;
                        packedMaterials[pUintIdx] |= (matID << pShift);
                        
                        packedCount++;
                    }
                }
            }

            // --- THE JIT SHADOW CACHE EXIT ---
            // If editCount == 2, the CPU just requested a background Shadow Ticket!
            if (job.editCount == 2) {
                int shadowBase = job.editStartIndex * 8192;
                for (int i = 0; i < 8192; i++) cpuShadowRAMPool[shadowBase + i] = tempRawMaterials[i];
                
                tempRawMaterials.Dispose(); packedMaterials.Dispose();
                localSurfaceMask.Dispose(); localSurfacePrefix.Dispose();
                // THE FIX: Removed double-dispose of localHeights and activeBiomes!
                return; // JIT Generation complete! Skip the rest of the pipeline!
            }

            // --- NEW: CALCULATE THE PREFIX SUM ---
            int runningPrefix = 0;
            for (int i = 0; i < 1024; i++) {
                localSurfacePrefix[i] = (uint)runningPrefix;
                runningPrefix += math.countbits(localSurfaceMask[i]);
            }

            // --- NEW: Blast the compacted cache to the Courier Array! ---
            int courierMatBase = jobIndex * 4096;
            for (int i = 0; i < 4096; i++) jobMaterialUpload[courierMatBase + i] = packedMaterials[i];
            
            // Tell the Main Thread exactly how many surface voxels we packed!
            var completedJob = jobQueue[jobIndex];
            completedJob.editStartIndex = packedCount; 
            jobQueue[jobIndex] = completedJob;
            
            // Upload the new 1-bit Surface Vault AND Prefix Sum
            int maskBaseNew = job.pad2 * 1024;
            for (int i = 0; i < 1024; i++) {
                cpuSurfaceMaskPool[maskBaseNew + i] = localSurfaceMask[i];
                cpuSurfacePrefixPool[maskBaseNew + i] = localSurfacePrefix[i];
            }

            tempRawMaterials.Dispose();
            packedMaterials.Dispose();
            localSurfaceMask.Dispose();
            localSurfacePrefix.Dispose();

            int maskBase = job.pad2 * 19;
            MacroMaskBaker.Bake1Bit(ref denseChunkPool, denseBase, ref macroMaskPool, maskBase);
        }
    }
}