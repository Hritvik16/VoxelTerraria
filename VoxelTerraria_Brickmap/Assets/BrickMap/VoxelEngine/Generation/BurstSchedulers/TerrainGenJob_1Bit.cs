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
        [NativeDisableParallelForRestriction] public NativeArray<uint> cpuMaterialChunkPool; 
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

            // --- NEW: THE BURST SURFACE PACKER (O(N) single pass) ---
            int packedCount = 0;
            for (int flatIdx = 0; flatIdx < 32768; flatIdx++) {
                int uintIdx = flatIdx >> 5;
                int bitIdx = flatIdx & 31;
                
                // If voxel is solid
                if ((denseChunkPool[(int)denseBase + uintIdx] & (1u << bitIdx)) != 0) {
                    int x = flatIdx & 31;
                    int y = (flatIdx >> 5) & 31;
                    int z = (flatIdx >> 10) & 31;
                    
                    bool isSurface = false;
                    
                    // Boundary check
                    if (x == 0 || x == 31 || y == 0 || y == 31 || z == 0 || z == 31) {
                        isSurface = true;
                    } else {
                        // Fast 6-way internal neighbor check
                        int nx = flatIdx - 1;      int px = flatIdx + 1;
                        int ny = flatIdx - 32;     int py = flatIdx + 32;
                        int nz = flatIdx - 1024;   int pz = flatIdx + 1024;
                        
                        if ((denseChunkPool[(int)denseBase + (nx >> 5)] & (1u << (nx & 31))) == 0 ||
                            (denseChunkPool[(int)denseBase + (px >> 5)] & (1u << (px & 31))) == 0 ||
                            (denseChunkPool[(int)denseBase + (ny >> 5)] & (1u << (ny & 31))) == 0 ||
                            (denseChunkPool[(int)denseBase + (py >> 5)] & (1u << (py & 31))) == 0 ||
                            (denseChunkPool[(int)denseBase + (nz >> 5)] & (1u << (nz & 31))) == 0 ||
                            (denseChunkPool[(int)denseBase + (pz >> 5)] & (1u << (pz & 31))) == 0) {
                            isSurface = true;
                        }
                    }
                    
                    if (isSurface) {
                        // THE DESYNC FIX: Only flag the mask IF we have budget for the material!
                        if (packedCount < 16384) { 
                            localSurfaceMask[uintIdx] |= (1u << bitIdx);
                            
                            // Extract material from tempRawMaterials
                            int mUintIdx = flatIdx >> 2;
                            int mShift = (flatIdx & 3) << 3;
                            uint matID = (tempRawMaterials[mUintIdx] >> mShift) & 0xFF;
                            
                            // Pack it directly into the new compacted array
                            int pUintIdx = packedCount >> 2;
                            int pShift = (packedCount & 3) << 3;
                            packedMaterials[pUintIdx] |= (matID << pShift);
                            
                            packedCount++;
                        }
                    }
                }
            }

            // --- NEW: SAVE TO SHADOW RAM ---
            // THE FIX: Save all 8192 uints!
            int shadowBase = job.pad2 * 8192;
            for (int i = 0; i < 8192; i++) {
                cpuShadowRAMPool[shadowBase + i] = tempRawMaterials[i];
            }

            // --- NEW: CALCULATE THE PREFIX SUM ---
            int runningPrefix = 0;
            for (int i = 0; i < 1024; i++) {
                localSurfacePrefix[i] = (uint)runningPrefix;
                runningPrefix += math.countbits(localSurfaceMask[i]);
            }

            // Blast the compacted cache DIRECTLY to the CPU Master Array!
            int matBase = job.pad2 * 4096; // THE FIX
            for (int i = 0; i < 4096; i++) cpuMaterialChunkPool[matBase + i] = packedMaterials[i];
            
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