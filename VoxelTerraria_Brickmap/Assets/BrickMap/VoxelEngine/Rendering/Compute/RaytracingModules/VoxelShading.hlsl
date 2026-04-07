StructuredBuffer<uint> _MaterialChunkPool;
StructuredBuffer<uint> _SurfaceMaskPool; 
StructuredBuffer<uint> _SurfacePrefixPool; // NEW

// Compressed Memory Lookup via countbits()
uint GetProceduralMaterial(int3 voxelPos, int3 baseChunkCoord, uint denseBase, int layer) {
    
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
    
    // denseBase is the 1024 offset. Our compacted array is 4096, so we multiply by 4.
    uint packedMaterials = _MaterialChunkPool[(denseBase * 4) + matUintIdx];
    
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
