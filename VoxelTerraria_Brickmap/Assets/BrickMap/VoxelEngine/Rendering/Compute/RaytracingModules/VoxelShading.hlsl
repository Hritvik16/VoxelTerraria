StructuredBuffer<uint> _MaterialChunkPool;
StructuredBuffer<uint> _ChunkMaterialPointers; // NEW: The Sparse Material Ticket Bank
StructuredBuffer<uint> _SurfaceMaskPool; 
StructuredBuffer<uint> _SurfacePrefixPool; 

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

    // --- THE 1-FRAME FALLBACK ---
    // If the player blows a hole in a 100% solid chunk, the Courier takes 1-2 frames to fetch a ticket.
    // During those 2 frames, the GPU mathematically guesses the material to prevent black flashes!
    if (ticket == 0xFFFFFFFF) {
        float worldY = voxelPos.y * _ClipmapCenters[layer].w; 
        if (worldY > 20.0) return 1; // Grass
        if (worldY > -10.0) return 2; // Dirt
        return 3;                     // Stone
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
