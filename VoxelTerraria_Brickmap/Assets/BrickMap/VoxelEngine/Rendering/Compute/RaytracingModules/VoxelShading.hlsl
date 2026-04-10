StructuredBuffer<uint> _MaterialChunkPool;
StructuredBuffer<uint> _ChunkMaterialPointers; // NEW: The Sparse Material Ticket Bank
StructuredBuffer<uint> _SurfaceMaskPool; 
StructuredBuffer<uint> _SurfacePrefixPool; 


// --- NEW: MATCH THE CPU'S NOISE HASH EXACTLY ---
float Hash_Biome(float3 p) {
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Compressed Memory Lookup via countbits()
uint GetProceduralMaterial(int3 voxelPos, int3 baseChunkCoord, uint denseBase, int layer) {
    
    // 1. Calculate the mapIndex to find our Ticket
    int baseRadXZ = (int)_RenderBounds.x;
    int baseRadY  = (int)_RenderBounds.y; 
    int sideXZ = 2 * baseRadXZ + 1;
    int sideY = 2 * baseRadY + 1;
    int mx = (int)(((uint)(baseChunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
    int my = (int)(((uint)(baseChunkCoord.y + 400000 * sideY)) % (uint)sideY);
    int mz = (int)(((uint)(baseChunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    uint mapIndex = (layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ;

    uint ticket = _ChunkMaterialPointers[mapIndex];

    // --- THE BIOME-AWARE FALLBACK ---
    // Mathematically guesses the perfect material to prevent pop-in at extreme render distances!
    if (ticket == 0xFFFFFFFF) {
        float layerScale = _ClipmapCenters[layer].w;
        float3 absoluteWorldPos = (baseChunkCoord * 32.0 + voxelPos) * layerScale;
        
        // Find the nearest biome anchor (Only executes once per ray hit!)
        int closestBiome = 0;
        float minDist = 999999.0;
        
        // --- THE FIX: APPLY THE ORGANIC BOUNDARY WARP ---
        float boundaryWarp = (Hash_Biome(float3(absoluteWorldPos.x, 0, absoluteWorldPos.z) * 0.005) - 0.5) * 150.0;

        for (int b = 0; b < _BiomeAnchorCount; b++) {
            float dist = distance(absoluteWorldPos.xz, _BiomeAnchors[b].position.xz) + boundaryWarp;
            if (dist < minDist) {
                minDist = dist;
                closestBiome = _BiomeAnchors[b].biomeType;
            }
        }

        float worldY = absoluteWorldPos.y;
        
        // 0=Forest, 1=Desert, 2=Snow, 3=Jungle, 4=Volcanic
        if (worldY > 20.0) {
            if (closestBiome == 1) return 4;  // Desert Sand
            if (closestBiome == 2) return 7;  // Snow
            if (closestBiome == 3) return 10; // Jungle Dark Grass
            if (closestBiome == 4) return 14; // Volcanic Lava
            return 1; // Default Grass
        }
        if (worldY > -10.0) {
            if (closestBiome == 1) return 5;  // Desert Sandstone
            if (closestBiome == 2) return 8;  // Snow Ice
            if (closestBiome == 3) return 12; // Jungle Mud
            if (closestBiome == 4) return 11; // Volcanic Ash
            return 2; // Default Dirt
        }
        
        if (closestBiome == 4) return 13; // Volcanic Dark Slate
        return 3; // Default Stone
    }
    
    // ---------------------------------------------------------
    // --- PERFORMANCE DEBUG TOGGLE ---
    // Uncomment 'return 1;' below to completely bypass the 2GB VRAM Vault lookup.
    // If your frame rate instantly jumps from 60 FPS back to 111 FPS, 
    // it mathematically proves the _MaterialChunkPool PCI-e memory bandwidth 
    // is bottlenecking the ALU cores!
    // ---------------------------------------------------------
    // return 1; // 1 = Grass
    
    uint localX = (uint)(voxelPos.x & 31);
    uint localY = (uint)(voxelPos.y & 31);
    uint localZ = (uint)(voxelPos.z & 31);
    
    uint flatIdx = localX + (localY << 5) + (localZ << 10);
    
    uint surfaceUintIdx = flatIdx >> 5;
    uint bitIdx = flatIdx & 31;
    
    // --- O(1) PREFIX SUM LOOKUP ---
    // Zero Loops. Zero Compiler Bugs. Instant Math.
    uint prefixCount = _SurfacePrefixPool[denseBase + surfaceUintIdx];
    uint finalMask = _SurfaceMaskPool[denseBase + surfaceUintIdx];
    
    // THE M1 SAFE BITMASK: Guaranteed not to overflow Apple Silicon registers
    uint maskedBits = finalMask & ~(0xFFFFFFFFu << bitIdx);
    uint surfaceCount = prefixCount + countbits(maskedBits);
    // --- COMPRESSED ARRAY LOOKUP ---
    // surfaceCount is now our exact 1D index on the compacted bookshelf!
    uint matUintIdx = surfaceCount >> 2;
    uint byteOffset = (surfaceCount & 3) << 3;
    // THE FIX: Read from the Sparse Ticket, NOT the dense geometry!
    // 4096 is the size of the array, so we multiply the ticket by 4096.
    uint packedMaterials = _MaterialChunkPool[(ticket * 4096) + matUintIdx];
    return (packedMaterials >> byteOffset) & 0xFF;
}

// --- NEW: THE EMISSIVE FAST PATH ---
// Safely fetches the Material ID for AO and Emissive checks, utilizing the Fast-Path where possible.
uint FastGetMaterial(int3 globalPos, int3 baseChunkCoord, uint baseDenseBase, int layer) {
    int3 chunkCoord = globalPos >> 5;
    int3 localPos = globalPos & 31;
    int flatIdx = localPos.x + (localPos.y << 5) + (localPos.z << 10);

    if (all(chunkCoord == baseChunkCoord)) {
        // FAST PATH: Inside the exact same chunk
        uint matData = _DenseChunkPool[baseDenseBase + (flatIdx >> 5)];
        if ((matData & (1u << (flatIdx & 31))) == 0) return 0; // It's Air
        return GetProceduralMaterial(localPos, baseChunkCoord, baseDenseBase, layer);
    }
    
    // SLOW PATH: We crossed a chunk boundary into a neighbor
    ChunkData cd = GetChunkData(chunkCoord, layer);
    if (cd.packedState == 1) {
        uint denseBase = cd.densePoolIndex * 1024u;
        uint matData = _DenseChunkPool[denseBase + (flatIdx >> 5)];
        if ((matData & (1u << (flatIdx & 31))) != 0) {
            return GetProceduralMaterial(localPos, chunkCoord, denseBase, layer);
        }
    }
    return 0; // Air or invalid chunk
}

#if !DUAL_STATE_1BIT
void UnpackLight(uint data, out uint matID, out float r, out float g, out float b, out float sun, out uint ao) {
    matID = data & 0xFF;
    r = ((data >> 8) & 0xF) / 15.0f;
    g = ((data >> 12) & 0xF) / 15.0f;
    b = ((data >> 16) & 0xF) / 15.0f;
    sun = ((data >> 20) & 0xF) / 15.0f;
    ao = (data >> 24) & 0xFF;
}
#endif
