StructuredBuffer<uint> _MaterialChunkPool;

// O(1) Absolute Memory Lookup
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
    
    // Fast bitwise indexing for the 8-bit offset
    uint uintIdx = flatIdx >> 2; 
    uint byteOffset = (flatIdx & 3) << 3;
    
    // Read the chunk's dedicated material array. denseBase is the 1024 offset, so we multiply by 8 for the 8192 offset.
    uint packedMaterials = _MaterialChunkPool[(denseBase * 8) + uintIdx];
    
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
