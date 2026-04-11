#define TICKET_SIZE 8192

StructuredBuffer<uint> _MaterialChunkPool;
StructuredBuffer<uint> _ChunkMaterialPointers; 
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
    uint returnMat = 0; // Explicitly initialized to appease Apple Metal
    
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
    if (ticket == 0xFFFFFFFF) {
        float layerScale = _ClipmapCenters[layer].w;
        float3 absoluteWorldPos = (baseChunkCoord * 32.0 + voxelPos) * layerScale;
        
        float baseHeight = 0;
        int actualBiome = 0;
        GetSurfaceTopology(absoluteWorldPos.xz, baseHeight, actualBiome);
        returnMat = GetMaterial(absoluteWorldPos, baseHeight, actualBiome);
    } else {
        uint localX = (uint)(voxelPos.x & 31);
        uint localY = (uint)(voxelPos.y & 31);
        uint localZ = (uint)(voxelPos.z & 31);
        
        uint flatIdx = localX + (localY << 5) + (localZ << 10);
        
        uint surfaceUintIdx = flatIdx >> 5;
        uint bitIdx = flatIdx & 31;
        
        uint prefixCount = _SurfacePrefixPool[denseBase + surfaceUintIdx];
        uint finalMask = _SurfaceMaskPool[denseBase + surfaceUintIdx];
        
        uint maskedBits = finalMask & ~(0xFFFFFFFFu << bitIdx);
        uint surfaceCount = prefixCount + countbits(maskedBits);
        
        uint matUintIdx = surfaceCount >> 2;
        uint byteOffset = (surfaceCount & 3) << 3;
        
        uint packedMaterials = _MaterialChunkPool[(ticket * TICKET_SIZE) + matUintIdx];
        returnMat = (packedMaterials >> byteOffset) & 0xFF;
    }

    return returnMat;
}

// --- THE EMISSIVE FAST PATH (Metal-Safe Version) ---
uint FastGetMaterial(int3 globalPos, int3 baseChunkCoord, uint baseDenseBase, int layer) {
    uint returnMat = 0; // Explicitly initialized to appease Apple Metal

    int3 chunkCoord = globalPos >> 5;
    int3 localPos = globalPos & 31;
    int flatIdx = localPos.x + (localPos.y << 5) + (localPos.z << 10);

    if (all(chunkCoord == baseChunkCoord)) {
        // FAST PATH: Inside the exact same chunk
        uint matData = _DenseChunkPool[baseDenseBase + (flatIdx >> 5)];
        if ((matData & (1u << (flatIdx & 31))) != 0) {
            returnMat = GetProceduralMaterial(localPos, baseChunkCoord, baseDenseBase, layer);
        }
    } else {
        // SLOW PATH: We crossed a chunk boundary into a neighbor
        ChunkData cd = GetChunkData(chunkCoord, layer);
        if (cd.packedState == 1) {
            uint denseBase = cd.densePoolIndex * 1024u;
            uint matData = _DenseChunkPool[denseBase + (flatIdx >> 5)];
            if ((matData & (1u << (flatIdx & 31))) != 0) {
                returnMat = GetProceduralMaterial(localPos, chunkCoord, denseBase, layer);
            }
        }
    }
    
    return returnMat;
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
