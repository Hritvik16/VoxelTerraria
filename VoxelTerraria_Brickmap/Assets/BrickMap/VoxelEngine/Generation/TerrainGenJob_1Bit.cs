using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.World; 

namespace VoxelEngine.World 
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
    public struct TerrainGenJob_1Bit : IJobParallelFor
    {
        public NativeArray<ChunkManager.ChunkJobData> jobQueue;
        [ReadOnly] public NativeArray<FeatureAnchor> features;
        [ReadOnly] public NativeArray<CavernNode> caverns;
        [ReadOnly] public NativeArray<TunnelSpline> tunnels; // NEW
        
        public int featureCount;
        public int cavernCount;
        public int tunnelCount; // NEW
        public float worldRadiusXZ;
        public int worldSeed;

        [NativeDisableParallelForRestriction] public NativeArray<uint> denseChunkPool;
        // [NativeDisableParallelForRestriction] public NativeArray<uint> denseLogicPool;
        [NativeDisableParallelForRestriction] public NativeArray<uint> macroMaskPool; // ADD THIS
        [NativeDisableParallelForRestriction] public NativeArray<float> chunkHeights; // NEW: The Sky Teleporter Map

        // --- THE NEW AAA 2D SURFACE MATH ---
        private float GetHeight2D(float x, float z) {
            float2 pos = new float2(x, z);
            float h = 60f; // Base sea level
            h += noise.snoise(pos * 0.002f) * 40f;  // Octave 1: Continental shifts
            h += noise.snoise(pos * 0.01f) * 15f;   // Octave 2: Rolling Hills
            h += noise.snoise(pos * 0.05f) * 4f;    // Octave 3: Boulders/Cliffs
            h += noise.snoise(pos * 0.2f) * 0.5f;   // Octave 4: 0.2m Surface Crunch
            return h;
        }
        public void Execute(int jobIndex)
        {
            ChunkManager.ChunkJobData job = jobQueue[jobIndex];
            uint denseBase = (uint)job.pad2 * 1024u; // CHANGED FROM 32768u

            float wStartX = job.worldPos.x * job.layerScale;
            float wStartZ = job.worldPos.z * job.layerScale;
            float wStartY = job.worldPos.y * job.layerScale;
            float wEndY   = (job.worldPos.y + 32f) * job.layerScale;
            float wEndX   = (job.worldPos.x + 32f) * job.layerScale;
            float wEndZ   = (job.worldPos.z + 32f) * job.layerScale;

            float bh00 = GetHeight2D(wStartX, wStartZ);
            float bh10 = GetHeight2D(wEndX, wStartZ);
            float bh01 = GetHeight2D(wStartX, wEndZ);
            float bh11 = GetHeight2D(wEndX, wEndZ);

            float bhMid = GetHeight2D(wStartX + 16f * job.layerScale, wStartZ + 16f * job.layerScale);
            
            float minBH = math.min(math.min(math.min(bh00, bh10), math.min(bh01, bh11)), bhMid) - 30f;
            float maxBH = math.max(math.max(math.max(bh00, bh10), math.max(bh01, bh11)), bhMid) + 30f;

            chunkHeights[job.mapIndex] = maxBH;

            bool isFullyUnderground = wEndY < minBH;
            bool isFullySky = wStartY > maxBH;

            if (isFullySky) {
                var modifiedJob = jobQueue[jobIndex];
                modifiedJob.pad3 = 1; 
                jobQueue[jobIndex] = modifiedJob;

                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; 
                
                // --- THE FIX: Clear the mask so the GPU doesn't read garbage data! ---
                int maskBase1 = job.pad2 * 19;
                for (int i = 0; i < 19; i++) macroMaskPool[maskBase1 + i] = 0; 
                
                return; 
            }

            if (isFullyUnderground) {
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = uint.MaxValue; // Fill with 1s
                ApplyCavesAndTunnels(denseBase, job);
            } 
            else {
                // 2. SURFACE GENERATION (Packed 1-Bit)
                for (int i = 0; i < 1024; i++) denseChunkPool[(int)denseBase + i] = 0; // Clear memory first

                for (int z = 0; z < 32; z++) {
                    float zPos = (job.worldPos.z + z) * job.layerScale; 
                    for (int x = 0; x < 32; x++) {
                        float xPos = (job.worldPos.x + x) * job.layerScale;
                        float h = GetHeight2D(xPos, zPos);

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
                ApplyCavesAndTunnels(denseBase, job);
            }

            // 3. BAKE THE HIERARCHICAL DDA MASK (19 Uints)
            int maskBase = job.pad2 * 19;
            for (int i = 0; i < 19; i++) macroMaskPool[maskBase + i] = 0; 

            // LEVEL 0: 4x4x4 (16 Uints)
            for (int x = 0; x < 32; x++) {
                for (int y = 0; y < 32; y++) {
                    for (int z = 0; z < 32; z++) {
                        int flatIdx = x + (y << 5) + (z << 10);
                        if ((denseChunkPool[(int)denseBase + (flatIdx >> 5)] & (1u << (flatIdx & 31))) != 0) {
                            int sx = x >> 2; int sy = y >> 2; int sz = z >> 2;
                            int subIndex = sx + (sy << 3) + (sz << 6);
                            macroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31));
                        }
                    }
                }
            }

            // LEVEL 1: 8x8x8 (2 Uints / 64 Bits)
            for(int i = 0; i < 512; i++) {
                if ((macroMaskPool[maskBase + (i >> 5)] & (1u << (i & 31))) != 0) {
                    int mx = (i & 7) >> 1;        // x in 0..3
                    int my = ((i >> 3) & 7) >> 1; // y in 0..3
                    int mz = (i >> 6) >> 1;       // z in 0..3
                    int mip1Idx = mx + (my << 2) + (mz << 4);
                    macroMaskPool[maskBase + 16 + (mip1Idx >> 5)] |= (1u << (mip1Idx & 31));
                }
            }

            // LEVEL 2: 16x16x16 (1 Uint / 8 Bits)
            for(int i = 0; i < 64; i++) {
                if ((macroMaskPool[maskBase + 16 + (i >> 5)] & (1u << (i & 31))) != 0) {
                    int mx = (i & 3) >> 1;        // x in 0..1
                    int my = ((i >> 2) & 3) >> 1; // y in 0..1
                    int mz = (i >> 4) >> 1;       // z in 0..1
                    int mip2Idx = mx + (my << 1) + (mz << 2);
                    macroMaskPool[maskBase + 18] |= (1u << mip2Idx);
                }
            }
        }

        private void ApplyCavesAndTunnels(uint denseBase, ChunkManager.ChunkJobData job) {
            float cx = job.worldPos.x * job.layerScale;
            float cy = job.worldPos.y * job.layerScale;
            float cz = job.worldPos.z * job.layerScale;
            float cSize = 32f * job.layerScale;
            float3 chunkCenter = new float3(cx + cSize * 0.5f, cy + cSize * 0.5f, cz + cSize * 0.5f);

            if (cavernCount > 0) {
                for (int c = 0; c < cavernCount; c++) {
                    CavernNode node = caverns[c];
                    float3 cPos = new float3(node.position.x, node.position.y, node.position.z);
                    float expandedRad = node.radius + (cSize * 0.866f);
                    if (math.lengthsq(chunkCenter - cPos) > expandedRad * expandedRad) continue;

                    float rSq = node.radius * node.radius;
                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * job.layerScale, cy + y * job.layerScale, cz + z * job.layerScale);
                                if (math.lengthsq(wPos - cPos) < rSq) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31)); // Carve bit
                                }
                            }
                        }
                    }
                }
            }

            if (tunnelCount > 0) {
                for (int t = 0; t < tunnelCount; t++) {
                    TunnelSpline spline = tunnels[t];
                    float3 pA = new float3(spline.startPoint.x, spline.startPoint.y, spline.startPoint.z);
                    float3 pB = new float3(spline.endPoint.x, spline.endPoint.y, spline.endPoint.z);
                    float3 lineVec = pB - pA;
                    float lineLenSq = math.lengthsq(lineVec);
                    float tVal = math.clamp(math.dot(chunkCenter - pA, lineVec) / lineLenSq, 0f, 1f);
                    float3 closestPoint = pA + tVal * lineVec;

                    float expandedRad = spline.radius + (cSize * 0.866f) + 3f; 
                    if (math.lengthsq(chunkCenter - closestPoint) > expandedRad * expandedRad) continue;

                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * job.layerScale, cy + y * job.layerScale, cz + z * job.layerScale);
                                float vtVal = math.clamp(math.dot(wPos - pA, lineVec) / lineLenSq, 0f, 1f);
                                float distSq = math.lengthsq(wPos - (pA + vtVal * lineVec));

                                if (distSq < (spline.radius - 2f) * (spline.radius - 2f)) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31));
                                }
                                else if (distSq < (spline.radius + 3f) * (spline.radius + 3f)) {
                                    float tunnelNoise = noise.snoise(new float2(vtVal * 50f, 0)) * spline.noiseIntensity * 3f;
                                    float dynamicRad = spline.radius + tunnelNoise;
                                    if (distSq < dynamicRad * dynamicRad) {
                                        int flatIdx = x + (y << 5) + (z << 10);
                                        denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // public void Execute(int jobIndex)
        // {
        //     ChunkManager.ChunkJobData job = jobQueue[jobIndex];
        //     uint denseBase = (uint)job.pad2 * 32768u;

        //     float wStartX = job.worldPos.x * job.layerScale;
        //     float wStartZ = job.worldPos.z * job.layerScale;
        //     float wStartY = job.worldPos.y * job.layerScale;
        //     float wEndY   = (job.worldPos.y + 32f) * job.layerScale;
        //     float wEndX   = (job.worldPos.x + 32f) * job.layerScale;
        //     float wEndZ   = (job.worldPos.z + 32f) * job.layerScale;

        //     // 1. MACRO CULLING: Evaluate the 4 corners using the NEW math
        //     float bh00 = GetHeight2D(wStartX, wStartZ);
        //     float bh10 = GetHeight2D(wEndX, wStartZ);
        //     float bh01 = GetHeight2D(wStartX, wEndZ);
        //     float bh11 = GetHeight2D(wEndX, wEndZ);

        //     // Added +/- 15m padding so mountain peaks inside the chunk aren't accidentally culled!
        //     float minBH = math.min(math.min(bh00, bh10), math.min(bh01, bh11)) - 15f;
        //     float maxBH = math.max(math.max(bh00, bh10), math.max(bh01, bh11)) + 15f;

        //     // No 3D noise means zero vertical padding required! Strict 0m bounds.
        //     bool isFullyUnderground = wEndY < minBH;
        //     bool isFullySky = wStartY > maxBH;

        //     if (isFullySky) {
        //         var modifiedJob = jobQueue[jobIndex];
        //         modifiedJob.pad3 = 1; 
        //         jobQueue[jobIndex] = modifiedJob;

        //         for (int i = 0; i < 32768; i++) denseChunkPool[(int)denseBase + i] = 0;
        //         return; 
        //     }

        //     if (isFullyUnderground) {
        //         for (int i = 0; i < 32768; i++) denseChunkPool[(int)denseBase + i] = 1; // Solid Stone
        //         ApplyCavesAndTunnels(denseBase, job);
        //         return; 
        //     }

        //     // 2. SURFACE GENERATION (Zero 3D Math!)
        //     for (int z = 0; z < 32; z++) {
        //         float zPos = (job.worldPos.z + z) * job.layerScale; 

        //         for (int x = 0; x < 32; x += 4) {
        //             float x0 = (job.worldPos.x + x)     * job.layerScale;
        //             float x1 = (job.worldPos.x + x + 1) * job.layerScale;
        //             float x2 = (job.worldPos.x + x + 2) * job.layerScale;
        //             float x3 = (job.worldPos.x + x + 3) * job.layerScale;

        //             float h0 = GetHeight2D(x0, zPos);
        //             float h1 = GetHeight2D(x1, zPos);
        //             float h2 = GetHeight2D(x2, zPos);
        //             float h3 = GetHeight2D(x3, zPos);

        //             for (int y = 0; y < 32; y++) {
        //                 float yPos = (job.worldPos.y + y) * job.layerScale; 

        //                 // INSTANT ROCK/AIR CHECK. NO 3D SIMPLEX REQUIRED.
        //                 uint m0 = yPos <= h0 ? (yPos > h0 - 2f ? 2u : 1u) : 0u; // Grass on top, Stone below
        //                 uint m1 = yPos <= h1 ? (yPos > h1 - 2f ? 2u : 1u) : 0u;
        //                 uint m2 = yPos <= h2 ? (yPos > h2 - 2f ? 2u : 1u) : 0u;
        //                 uint m3 = yPos <= h3 ? (yPos > h3 - 2f ? 2u : 1u) : 0u;

        //                 int flatIdx = x + (y << 5) + (z << 10);
        //                 denseChunkPool[(int)denseBase + flatIdx]     = m0;
        //                 denseChunkPool[(int)denseBase + flatIdx + 1] = m1;
        //                 denseChunkPool[(int)denseBase + flatIdx + 2] = m2;
        //                 denseChunkPool[(int)denseBase + flatIdx + 3] = m3;
        //             }
        //         }
        //     }

        //     ApplyCavesAndTunnels(denseBase, job);
        //     // --- 3. BAKE THE HIERARCHICAL DDA MASK ---
        //     int maskBase = job.pad2 * 16;
        //     for (int i = 0; i < 16; i++) macroMaskPool[maskBase + i] = 0; // Clear the memory

        //     for (int x = 0; x < 32; x++) {
        //         for (int y = 0; y < 32; y++) {
        //             for (int z = 0; z < 32; z++) {
        //                 int flatIdx = x + (y << 5) + (z << 10);
        //                 if (denseChunkPool[(int)denseBase + flatIdx] > 0) {
        //                     // Calculate which 4x4x4 Sub-Chunk this voxel belongs to
        //                     int sx = x >> 2; int sy = y >> 2; int sz = z >> 2;
        //                     int subIndex = sx + (sy << 3) + (sz << 6);
        //                     // Flip the corresponding bit to 1!
        //                     macroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31));
        //                 }
        //             }
        //         }
        //     }
        // }

        // private void ApplyCavesAndTunnels(uint denseBase, ChunkManager.ChunkJobData job) {
        //     float cx = job.worldPos.x * job.layerScale;
        //     float cy = job.worldPos.y * job.layerScale;
        //     float cz = job.worldPos.z * job.layerScale;
        //     float cSize = 32f * job.layerScale;
        //     float3 chunkCenter = new float3(cx + cSize * 0.5f, cy + cSize * 0.5f, cz + cSize * 0.5f);
        //     float chunkRadiusSq = (cSize * 0.866f) * (cSize * 0.866f); 

        //     // --- 1. CARVE CAVERNS & ENTRANCES ---
        //     if (cavernCount > 0) {
        //         for (int c = 0; c < cavernCount; c++) {
        //             CavernNode node = caverns[c];
        //             if (node.cavernType == 0) continue; // Skip solid biomes

        //             float3 cPos = new float3(node.position.x, node.position.y, node.position.z);
                    
        //             // Chunk Culling
        //             float expandedRad = node.radius + (cSize * 0.866f);
        //             if (math.lengthsq(chunkCenter - cPos) > expandedRad * expandedRad) continue;

        //             float rSq = node.radius * node.radius;
        //             for (int x = 0; x < 32; x++) {
        //                 for (int y = 0; y < 32; y++) {
        //                     for (int z = 0; z < 32; z++) {
        //                         float3 wPos = new float3(cx + x * job.layerScale, cy + y * job.layerScale, cz + z * job.layerScale);
        //                         if (math.lengthsq(wPos - cPos) < rSq) {
        //                             int flatIdx = x + (y << 5) + (z << 10);
        //                             denseChunkPool[(int)denseBase + flatIdx] = 0; 
        //                         }
        //                     }
        //                 }
        //             }
        //         }
        //     }

        //     // --- 2. CARVE SPLINE TUNNELS ---
        //     if (tunnelCount > 0) {
        //         for (int t = 0; t < tunnelCount; t++) {
        //             TunnelSpline spline = tunnels[t];
        //             float3 pA = new float3(spline.startPoint.x, spline.startPoint.y, spline.startPoint.z);
        //             float3 pB = new float3(spline.endPoint.x, spline.endPoint.y, spline.endPoint.z);
                    
        //             // Line-to-Point distance optimization for chunk culling
        //             float3 lineVec = pB - pA;
        //             float lineLenSq = math.lengthsq(lineVec);
        //             float tVal = math.clamp(math.dot(chunkCenter - pA, lineVec) / lineLenSq, 0f, 1f);
        //             float3 closestPoint = pA + tVal * lineVec;

        //             float expandedRad = spline.radius + (cSize * 0.866f) + 3f; // 3f padding for noise
        //             if (math.lengthsq(chunkCenter - closestPoint) > expandedRad * expandedRad) continue;

        //             for (int x = 0; x < 32; x++) {
        //                 for (int y = 0; y < 32; y++) {
        //                     for (int z = 0; z < 32; z++) {
        //                         float3 wPos = new float3(cx + x * job.layerScale, cy + y * job.layerScale, cz + z * job.layerScale);
                                
        //                         // Distance to tunnel core
        //                         float vtVal = math.clamp(math.dot(wPos - pA, lineVec) / lineLenSq, 0f, 1f);
        //                         float3 vClosest = pA + vtVal * lineVec;
        //                         float distSq = math.lengthsq(wPos - vClosest);

        //                         // Fast inner core check
        //                         if (distSq < (spline.radius - 2f) * (spline.radius - 2f)) {
        //                             denseChunkPool[(int)denseBase + x + (y << 5) + (z << 10)] = 0;
        //                         }
        //                         // The Crust Check (Ultra-cheap 1D noise modifier)
        //                         else if (distSq < (spline.radius + 3f) * (spline.radius + 3f)) {
        //                             // 1D noise based on progression along the tunnel
        //                             float tunnelNoise = noise.snoise(new float2(vtVal * 50f, 0)) * spline.noiseIntensity * 3f;
        //                             float dynamicRad = spline.radius + tunnelNoise;
                                    
        //                             if (distSq < dynamicRad * dynamicRad) {
        //                                 denseChunkPool[(int)denseBase + x + (y << 5) + (z << 10)] = 0;
        //                             }
        //                         }
        //                     }
        //                 }
        //             }
        //         }
        //     }
        // }
    }
}