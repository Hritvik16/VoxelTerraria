int GetLayer(float3 worldPos) {
    int baseRadXZ = (int)_RenderBounds.x;
    int baseRadY  = (int)_RenderBounds.y;
    for (int L = 0; L < _ClipmapLayers; L++) {
        float layerScale = _ClipmapCenters[L].w;
        int3 chunkCoord = int3(floor(worldPos / (32.0 * layerScale)));
        int3 centerChunk = (int3)_ClipmapCenters[L].xyz;
        // SQUARE CULLING ON THE GPU
        int dx = abs(chunkCoord.x - centerChunk.x);
        int dz = abs(chunkCoord.z - centerChunk.z);
        int distXZ = max(dx, dz);

        // // --- THE CASCADING TORNADO CLIPMAP ---
        // int activeRadXZ = baseRadXZ;
        // int activeRadY = baseRadY;

        // if (L == 0) {
        //     activeRadY = min(4, baseRadY);
        // } else if (L == 1) {
        //     activeRadXZ = max(2, baseRadXZ - 4);
        //     activeRadY = min(8, baseRadY);
        // } else if (L >= 2) {
        //     activeRadXZ = max(2, baseRadXZ - 8);
        //     activeRadY = baseRadY;
        // }

        // // PURE MATH. ZERO VRAM READS.
        // if (distXZ <= activeRadXZ && abs(chunkCoord.y - centerChunk.y) <= activeRadY) {
        //     return L;
        // }
        // --- THE CASCADING TORNADO CLIPMAP ---
        int activeRadXZ = baseRadXZ; // THE FIX: Never shrink the XZ radius!
        int activeRadY = baseRadY;

        // We only shrink the Y radius to prevent wasting memory on deep underground/high sky LOD0 chunks
        if (L == 0) {
            activeRadY = min(4, baseRadY);
        } else if (L == 1) {
            activeRadY = min(8, baseRadY);
        } 

        // PURE MATH. ZERO VRAM READS.
        if (distXZ <= activeRadXZ && abs(chunkCoord.y - centerChunk.y) <= activeRadY) {
            return L;
        }
    }
    return _ClipmapLayers;
}

// ChunkData GetChunkData(int3 chunkCoord, int layer) {
//     // EXPLICIT ZERO-INITIALIZATION: Metal will refuse to compile the kernel 
//     // if it detects we might return an uninitialized 'packedState'.
//     ChunkData invalidChunk;
//     // invalidChunk.position = float4(0, 0, 0, 0);
//     invalidChunk.packedState = 0;
//     invalidChunk.densePoolIndex = 0;
    
//     int baseRadXZ = (int)_RenderBounds.x;
//     int baseRadY  = (int)_RenderBounds.y; 
    
//     // --- THE CASCADING TORNADO CLIPMAP ---
//     int activeRadXZ = baseRadXZ;
//     int activeRadY = baseRadY;

//     if (layer == 0) {
//         activeRadY = min(4, baseRadY);
//     } else if (layer == 1) {
//         activeRadXZ = max(2, baseRadXZ - 4);
//         activeRadY = min(8, baseRadY);
//     } else if (layer >= 2) {
//         activeRadXZ = max(2, baseRadXZ - 8);
//         activeRadY = baseRadY;
//     }

//     int3 centerChunk = (int3)_ClipmapCenters[layer].xyz;
    
//     // SQUARE CULLING ON THE GPU
//     int dx = abs(chunkCoord.x - centerChunk.x);
//     int dz = abs(chunkCoord.z - centerChunk.z);
//     int distXZ = max(dx, dz);
    
//     if(distXZ > activeRadXZ || abs(chunkCoord.y - centerChunk.y) > activeRadY) return invalidChunk;

//     int sideXZ = 2 * baseRadXZ + 1;
//     int sideY = 2 * baseRadY + 1;
//     int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
//     int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
//     int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    
//     // THE FIX: Natively return the chunk without float precision validation!
//     return _ChunkMap[(layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ];
// }
ChunkData GetChunkData(int3 chunkCoord, int layer) {
    ChunkData invalidChunk;
    invalidChunk.packedState = 0;
    invalidChunk.densePoolIndex = 0xFFFFFFFF;
    
    int baseRadXZ = (int)_RenderBounds.x;
    int baseRadY  = (int)_RenderBounds.y; 
    
    int activeRadXZ = baseRadXZ;
    int activeRadY = baseRadY;

    if (layer == 0) { activeRadY = min(4, baseRadY); } 
    else if (layer == 1) { activeRadXZ = max(2, baseRadXZ - 4); activeRadY = min(8, baseRadY); } 
    else if (layer >= 2) { activeRadXZ = max(2, baseRadXZ - 8); activeRadY = baseRadY; }

    int3 centerChunk = (int3)_ClipmapCenters[layer].xyz;
    int dx = abs(chunkCoord.x - centerChunk.x);
    int dz = abs(chunkCoord.z - centerChunk.z);
    int distXZ = max(dx, dz);
    if(distXZ > activeRadXZ || abs(chunkCoord.y - centerChunk.y) > activeRadY) return invalidChunk;

    int sideXZ = 2 * baseRadXZ + 1;
    int sideY = 2 * baseRadY + 1;
    // int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
    // int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
    // int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    int mx = (chunkCoord.x % sideXZ + sideXZ) % sideXZ;
    int my = (chunkCoord.y % sideY + sideY) % sideY;
    int mz = (chunkCoord.z % sideXZ + sideXZ) % sideXZ;
    
    ChunkData cd = _ChunkMap[(layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ];
    
    // --- THE 0-BYTE ANTI-GHOSTING FIX ---
    // Extract the spatial stamp from the chunk
    uint px = (cd.packedState >> 8) & 0xFF;
    uint py = (cd.packedState >> 16) & 0xFF;
    uint pz = (cd.packedState >> 24) & 0xFF;
    
    uint ex = (uint)(chunkCoord.x & 0xFF);
    uint ey = (uint)(chunkCoord.y & 0xFF);
    uint ez = (uint)(chunkCoord.z & 0xFF);
    
    // If the stamp doesn't match our ray's coordinate, the data is stale! Treat it as empty space.
    if (cd.packedState != 0 && (px != ex || py != ey || pz != ez)) {
        return invalidChunk;
    }
    
    // Strip the spatial tag off so the rest of the shader works normally (1 = solid, 3 = sky)
    cd.packedState = cd.packedState & 0xFF; 
    return cd;
}

    // Array bounds strictly use the maximum base bounds
    // int sideXZ = 2 * baseRadXZ + 1;
    // int sideY = 2 * baseRadY + 1;
    // int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
    // int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
    // int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    
    // ChunkData cd = _ChunkMap[(layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ];
    
    // float3 expectedPos = chunkCoord * 32.0f;
    // // The following validation check prevents stale data flickering during movement.
    // if (abs(cd.position.x - expectedPos.x) > 0.1f || abs(cd.position.y - expectedPos.y) > 0.1f || abs(cd.position.z - expectedPos.z) > 0.1f) return invalidChunk;
    // return cd;

// --- ZERO-FLOAT 1-BIT SOLIDITY CHECK ---
bool IsSolid1Bit(int3 globalPos, int layer) {
    // Bit-shifting handles negative coordinates perfectly (Zero float casting required!)
    int3 chunkCoord = globalPos >> 5;
    int3 localPos = globalPos & 31;
    
    ChunkData cd = GetChunkData(chunkCoord, layer);
    
    if (cd.packedState == 1) {
        uint denseBase = cd.densePoolIndex * 1024u;
        int flatIdx = localPos.x + (localPos.y << 5) + (localPos.z << 10);
        uint matData = _DenseChunkPool[denseBase + (flatIdx >> 5)];
        return (matData & (1u << (flatIdx & 31))) != 0;
    }
    return false;
}
uint GetVoxelFromRoot(uint rootIndex, int3 localPos) {
    if (rootIndex == 0xFFFFFFFF || rootIndex >= (uint)_PoolSize) return 0;
    uint nodeIndex = rootIndex;
    int size = 32;
    for (int i = 0; i < 6; i++) {
        if (nodeIndex >= (uint)_PoolSize) return 0;
        if (nodeIndex >= (uint)_PoolSize) return 0;
        SVONode node = _SVOPool[nodeIndex];
        if (node.childIndex == 0) return node.material;
        size >>= 1; 
        int3 childIdx = (localPos >= size) ? 1 : 0;
        localPos -= childIdx * size;
        nodeIndex = node.childIndex + (childIdx.x + childIdx.y * 2 + childIdx.z * 4);
    }
    
    // FIX: Actually read the final leaf node from VRAM instead of returning Air!
    return (nodeIndex < (uint)_PoolSize) ? _SVOPool[nodeIndex].material : 0; 
}


bool TraceVoxelRay(Ray ray, float maxDist, bool isShadow, out float t, out int3 mask, out uint hitMatID_Data, out float finalVoxelSize, out float3 finalBlockPos, out int raySteps) {
    hitMatID_Data = 0; mask = 0; t = 0; finalVoxelSize = 1.0f; finalBlockPos = 0; raySteps = 0;
    
    // --- THE BULLETPROOF INFINITY GUARD ---
    float3 safeDir = ray.direction;
    safeDir.x = abs(safeDir.x) < 1e-6f ? (safeDir.x >= 0 ? 1e-6f : -1e-6f) : safeDir.x;
    safeDir.y = abs(safeDir.y) < 1e-6f ? (safeDir.y >= 0 ? 1e-6f : -1e-6f) : safeDir.y;
    safeDir.z = abs(safeDir.z) < 1e-6f ? (safeDir.z >= 0 ? 1e-6f : -1e-6f) : safeDir.z;
    float3 invDir = 1.0f / safeDir;

    int3 rayStep = sign(safeDir);
    int3 stepDirBounds = max(rayStep, 0);

    // for (int steps = 0; steps < 160; steps++) {
    for (int steps = 0; steps < 100; steps++) {
        raySteps++;
        if (t > maxDist) break;
        
        // float3 currentPos = ray.origin + ray.direction * t;
        float3 currentPos = ray.origin + safeDir * t;
        if (currentPos.y > 130.0f && ray.direction.y > -0.05f) break;

        int layer = GetLayer(currentPos);
        if (layer >= _ClipmapLayers) break;

        float voxelSize = _ClipmapCenters[layer].w;
        float chunkSize = 32.0f * voxelSize;
        
        int3 mapPos = int3(floor(currentPos / voxelSize));
        int3 chunkCoord = mapPos >> 5;

       ChunkData currentChunk = GetChunkData(chunkCoord, layer);
        uint packed = currentChunk.packedState;

        // --- THE CLIPMAP HOLE FIX ---
        // If LOD 0 hasn't finished generating yet, seamlessly read from LOD 1 to prevent flashing holes!
        if (packed == 0 && layer < _ClipmapLayers - 1) {
            int fLayer = layer + 1;
            float fVoxelSize = _ClipmapCenters[fLayer].w;
            int3 fMapPos = int3(floor(currentPos / fVoxelSize));
            int3 fChunkCoord = fMapPos >> 5;

            ChunkData fChunk = GetChunkData(fChunkCoord, fLayer);
            if (fChunk.packedState > 0) {
                layer = fLayer;
                voxelSize = fVoxelSize;
                chunkSize = 32.0f * voxelSize;
                mapPos = fMapPos;
                chunkCoord = fChunkCoord;
                currentChunk = fChunk;
                packed = currentChunk.packedState;
            } else {
                // --- M1 L2 CACHE SAVER: THE VOID LEAP ---
                // Both LODs are empty! We are deep in unloaded space.
                // Instantly scale the ray to the maximum LOD layer so it leaps 
                // 128-meters at a time instead of taking 6.4-meter micro-steps!
                layer = _ClipmapLayers - 1;
                voxelSize = _ClipmapCenters[layer].w;
                chunkSize = 32.0f * voxelSize;
                mapPos = int3(floor(currentPos / voxelSize));
                chunkCoord = mapPos >> 5;
            }
        }

        // --- THE 2.5D SKY TELEPORTER ---
        int sideXZ = 2 * (int)_RenderBounds.x + 1;
        int sideY  = 2 * (int)_RenderBounds.y + 1;
        // int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
        // int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
        // int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
        int mx = (chunkCoord.x % sideXZ + sideXZ) % sideXZ;
        int my = (chunkCoord.y % sideY + sideY) % sideY;
        int mz = (chunkCoord.z % sideXZ + sideXZ) % sideXZ;
        int mapIndex = (layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ;

        float chunkMaxY = _ChunkHeightMap[mapIndex];
        float rayCurrentY = ray.origin.y + (ray.direction.y * t);

        // If the ray is currently ABOVE the highest mountain in this chunk...
        if (rayCurrentY > chunkMaxY) {
            if (ray.direction.y >= -1e-5f) {
                // Looking flat or up. We will NEVER hit terrain here. Skip the chunk entirely!
                packed = 3; 
            } else {
                // Looking down. Mathematically teleport the ray to the terrain roof!
                float tRoof = (chunkMaxY - ray.origin.y) * invDir.y;
                
                // Prevent overshooting into neighboring chunks by checking the XZ walls
                float3 nextC = (chunkCoord + stepDirBounds) * chunkSize;
                float3 tMaxC = (nextC - ray.origin) * invDir;
                if (!(tMaxC.x > t - 0.1f) || abs(ray.direction.x) < 1e-6f) tMaxC.x = 3.4e38f;
                if (!(tMaxC.z > t - 0.1f) || abs(ray.direction.z) < 1e-6f) tMaxC.z = 3.4e38f;
                float tBoundary = min(tMaxC.x, tMaxC.z); 
                
                // Engage Teleport 
                float teleportT = min(tRoof, tBoundary);
                float pad = max(voxelSize * 1e-3f, teleportT * 1e-5f);
                t = max(t + voxelSize * 1e-4f, teleportT + pad);
                
                // Resync to the safely capped vector
                mapPos = int3(floor((ray.origin + safeDir * t) / voxelSize));
                // tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
            }
        }

        // 1 = Dense, 3 = Empty
        uint denseIndex = currentChunk.densePoolIndex;
        if (packed == 1) {
            float3 chunkMinPos = chunkCoord * chunkSize;
            float3 chunkMaxPos = chunkMinPos + chunkSize; // ADD THIS LINE
            float3 tb0 = (chunkMinPos - ray.origin) * invDir;
            float3 tb1 = (chunkMaxPos - ray.origin) * invDir;
            float3 tMaxB = max(tb0, tb1);
            float tExitB = min(min(tMaxB.x, tMaxB.y), tMaxB.z);

            float3 tDelta = abs(voxelSize * invDir);
            float3 tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
            if (tMaxV.x < -1.0f || abs(ray.direction.x) < 1e-6f) tMaxV.x = 3.4e38f; else if (tMaxV.x <= t) tMaxV.x += tDelta.x;
            if (tMaxV.y < -1.0f || abs(ray.direction.y) < 1e-6f) tMaxV.y = 3.4e38f; else if (tMaxV.y <= t) tMaxV.y += tDelta.y;
            if (tMaxV.z < -1.0f || abs(ray.direction.z) < 1e-6f) tMaxV.z = 3.4e38f; else if (tMaxV.z <= t) tMaxV.z += tDelta.z;

            uint denseBase = denseIndex << 15;
            bool hitInside = false;

            int vSteps = isShadow ? 16 : 128;

            uint cachedMip2 = _MacroMaskPool[denseIndex * 19 + 18];
            uint cachedMip1_0 = _MacroMaskPool[denseIndex * 19 + 16];
            uint cachedMip1_1 = _MacroMaskPool[denseIndex * 19 + 17];

            for (int v = 0; v < vSteps; v++) {
                if (t > tExitB + 0.001f) break;
                
                int3 vLocal = mapPos - (chunkCoord << 5);
                if (any(vLocal < 0) || any(vLocal > 31)) break;

                // --- 3D MIP-MAPPED DDA LEAP ---
                // 1. Check Level 2 (16x16x16 Leap)
                int m2x = vLocal.x >> 4; int m2y = vLocal.y >> 4; int m2z = vLocal.z >> 4;
                int mip2Index = m2x + (m2y << 1) + (m2z << 2);

                if ((cachedMip2 & (1u << mip2Index)) == 0) { // USE CACHED REGISTER
                    int3 nextBoundLocal = (vLocal & ~15) + (stepDirBounds * 16);
                    float3 tPlanes = ((chunkCoord * 32 + nextBoundLocal) * voxelSize - ray.origin) * invDir;
                    if (!(tPlanes.x > t + 1e-4f)) tPlanes.x = 3.4e38f;
                    if (!(tPlanes.y > t + 1e-4f)) tPlanes.y = 3.4e38f;
                    if (!(tPlanes.z > t + 1e-4f)) tPlanes.z = 3.4e38f;
                    
                    // 1. Find the closest plane
                    float tNext = min(min(tPlanes.x, tPlanes.y), tPlanes.z);
                    
                    mask = (tNext == tPlanes.x) ? int3(1,0,0) : ((tNext == tPlanes.y) ? int3(0,1,0) : int3(0,0,1));
                    
                    // 3. SECURE MECHANICAL PROGRESSION
                    // 3. SECURE MECHANICAL PROGRESSION
                    float pad = max(voxelSize * 1e-3f, tNext * 1e-5f);
                    t = max(t, tNext + pad);
                    mapPos = int3(floor((ray.origin + safeDir * t) / voxelSize)); 
                    
                    tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
                    if (tMaxV.x < -1.0f || abs(ray.direction.x) < 1e-6f) tMaxV.x = 3.4e38f; else if (tMaxV.x <= t) tMaxV.x += tDelta.x;
                    if (tMaxV.y < -1.0f || abs(ray.direction.y) < 1e-6f) tMaxV.y = 3.4e38f; else if (tMaxV.y <= t) tMaxV.y += tDelta.y;
                    if (tMaxV.z < -1.0f || abs(ray.direction.z) < 1e-6f) tMaxV.z = 3.4e38f; else if (tMaxV.z <= t) tMaxV.z += tDelta.z;
                    continue;
                }

                // 2. Check Level 1 (8x8x8 Leap)
                int m1x = vLocal.x >> 3; int m1y = vLocal.y >> 3; int m1z = vLocal.z >> 3;
                int mip1Index = m1x + (m1y << 2) + (m1z << 4);
                
                // USE CACHED REGISTERS: Instantly pick the correct uint without touching VRAM!
                uint mip1Mask = (mip1Index < 32) ? cachedMip1_0 : cachedMip1_1; 

                if ((mip1Mask & (1u << (mip1Index & 31))) == 0) {
                    int3 nextBoundLocal = (vLocal & ~7) + (stepDirBounds * 8);
                    float3 tPlanes = ((chunkCoord * 32 + nextBoundLocal) * voxelSize - ray.origin) * invDir;
                    if (!(tPlanes.x > t - 0.1f) || abs(ray.direction.x) < 1e-6f) tPlanes.x = 3.4e38f;
                    if (!(tPlanes.y > t - 0.1f) || abs(ray.direction.y) < 1e-6f) tPlanes.y = 3.4e38f;
                    if (!(tPlanes.z > t - 0.1f) || abs(ray.direction.z) < 1e-6f) tPlanes.z = 3.4e38f;
                    
                    // 1. Find the closest plane
                    float tNext = min(min(tPlanes.x, tPlanes.y), tPlanes.z);
                    
                    mask = (tNext == tPlanes.x) ? int3(1,0,0) : ((tNext == tPlanes.y) ? int3(0,1,0) : int3(0,0,1));
                    
                    // 3. SECURE MECHANICAL PROGRESSION
                    float pad = max(voxelSize * 1e-3f, tNext * 1e-5f);
                    t = max(t + voxelSize * 1e-4f, tNext + pad);
                    mapPos = int3(floor((ray.origin + safeDir * t) / voxelSize)); 
                    
                    tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
                    if (tMaxV.x < -1.0f || abs(ray.direction.x) < 1e-6f) tMaxV.x = 3.4e38f; else if (tMaxV.x <= t) tMaxV.x += tDelta.x;
                    if (tMaxV.y < -1.0f || abs(ray.direction.y) < 1e-6f) tMaxV.y = 3.4e38f; else if (tMaxV.y <= t) tMaxV.y += tDelta.y;
                    if (tMaxV.z < -1.0f || abs(ray.direction.z) < 1e-6f) tMaxV.z = 3.4e38f; else if (tMaxV.z <= t) tMaxV.z += tDelta.z;
                    continue;
                }

                // 3. Check Level 0 (4x4x4 Leap)
                int sx = vLocal.x >> 2; int sy = vLocal.y >> 2; int sz = vLocal.z >> 2;
                int subIndex = sx + (sy << 3) + (sz << 6);
                uint maskVal = _MacroMaskPool[denseIndex * 19 + (subIndex >> 5)];

                if ((maskVal & (1u << (subIndex & 31))) == 0) {
                    int3 nextBoundLocal = (vLocal & ~3) + (stepDirBounds * 4);
                    float3 tPlanes = ((chunkCoord * 32 + nextBoundLocal) * voxelSize - ray.origin) * invDir;
                    if (!(tPlanes.x > t - 0.1f) || abs(ray.direction.x) < 1e-6f) tPlanes.x = 3.4e38f;
                    if (!(tPlanes.y > t - 0.1f) || abs(ray.direction.y) < 1e-6f) tPlanes.y = 3.4e38f;
                    if (!(tPlanes.z > t - 0.1f) || abs(ray.direction.z) < 1e-6f) tPlanes.z = 3.4e38f;
                    
                    // 1. Find the closest plane
                    float tNext = min(min(tPlanes.x, tPlanes.y), tPlanes.z);
                    
                    mask = (tNext == tPlanes.x) ? int3(1,0,0) : ((tNext == tPlanes.y) ? int3(0,1,0) : int3(0,0,1));
                    
                    // 3. SECURE MECHANICAL PROGRESSION
                    float pad = max(voxelSize * 1e-3f, tNext * 1e-5f);
                    t = max(t + voxelSize * 1e-4f, tNext + pad);
                    mapPos = int3(floor((ray.origin + safeDir * t) / voxelSize)); 
                    
                    tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
                    if (tMaxV.x < -1.0f || abs(ray.direction.x) < 1e-6f) tMaxV.x = 3.4e38f; else if (tMaxV.x <= t) tMaxV.x += tDelta.x;
                    if (tMaxV.y < -1.0f || abs(ray.direction.y) < 1e-6f) tMaxV.y = 3.4e38f; else if (tMaxV.y <= t) tMaxV.y += tDelta.y;
                    if (tMaxV.z < -1.0f || abs(ray.direction.z) < 1e-6f) tMaxV.z = 3.4e38f; else if (tMaxV.z <= t) tMaxV.z += tDelta.z;
                    continue;
                }

                // STABLE
                // // // Standard dense check if the bit is 1
                // // uint matData = _DenseChunkPool[denseBase + vLocal.x + (vLocal.y << 5) + (vLocal.z << 10)];
                // // if ((matData & 0xFF) > 0) { hitInside = true; hitMatID_Data = matData; finalVoxelSize = voxelSize; finalBlockPos = mapPos; break; }
                // // Standard dense check if the bit is 1
                // int flatIdx = vLocal.x + (vLocal.y << 5) + (vLocal.z << 10);
                // uint packedBase = denseIndex * 1024; // Was denseIndex << 15
                // uint matData = _DenseChunkPool[packedBase + (flatIdx >> 5)];

                // 1 BIT TEST
                // if ((matData & (1u << (flatIdx & 31))) != 0) { 
                //     hitInside = true; 
                //     hitMatID_Data = 1; // Just flag it as solid
                //     finalVoxelSize = voxelSize; 
                //     finalBlockPos = mapPos; 
                //     break; 
                // }

                // --- DYNAMIC DENSE CHECK ---
                #if DUAL_STATE_1BIT
                    // 1 BIT TEST
                    int flatIdx = vLocal.x + (vLocal.y << 5) + (vLocal.z << 10);
                    uint packedBase = denseIndex * 1024u; 
                    uint matData = _DenseChunkPool[packedBase + (flatIdx >> 5)];
                    if ((matData & (1u << (flatIdx & 31))) != 0) { 
                        float minExitT = min(min(tMaxV.x, tMaxV.y), tMaxV.z);
                        if (minExitT > t + max(1e-5f, t * 1e-5f)) {
                            hitInside = true; hitMatID_Data = 1; finalVoxelSize = voxelSize; finalBlockPos = mapPos; break;
                        }
                    }
                #else
                    uint denseBase = denseIndex << 15;
                    uint matData = _DenseChunkPool[denseBase + vLocal.x + (vLocal.y << 5) + (vLocal.z << 10)];
                    if ((matData & 0xFF) > 0) { 
                        float minExitT = min(min(tMaxV.x, tMaxV.y), tMaxV.z);
                        if (minExitT > t + max(1e-5f, t * 1e-5f)) {
                            hitInside = true; hitMatID_Data = matData; finalVoxelSize = voxelSize; finalBlockPos = mapPos; break;
                        }
                    }
                #endif
                
                // APPLE SILICON OPTIMIZATION: 16-Bit ALU Math
                // Casting to 'half' explicitly forces the M1 GPU to use its double-speed 16-bit registers
                // half3 tMaxV_h = (half3)tMaxV;
                // half3 tDelta_h = (half3)tDelta;
                
                // if (tMaxV_h.x < tMaxV_h.y) {
                //     if (tMaxV_h.x < tMaxV_h.z) { t = tMaxV.x; tMaxV.x += tDelta.x; mapPos.x += rayStep.x; mask = int3(1,0,0); }
                //     else { t = tMaxV.z; tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                // } else {
                //     if (tMaxV_h.y < tMaxV_h.z) { t = tMaxV.y; tMaxV.y += tDelta.y; mapPos.y += rayStep.y; mask = int3(0,1,0); }
                //     else { t = tMaxV.z; tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                // }

                if (tMaxV.x < tMaxV.y) {
                    if (tMaxV.x < tMaxV.z) { t = tMaxV.x; tMaxV.x += tDelta.x; mapPos.x += rayStep.x; mask = int3(1,0,0); }
                    else { t = tMaxV.z; tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                } else {
                    if (tMaxV.y < tMaxV.z) { t = tMaxV.y; tMaxV.y += tDelta.y; mapPos.y += rayStep.y; mask = int3(0,1,0); }
                    else { t = tMaxV.z; tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                }
            }
            if (hitInside) return true;
        }
        
        float3 nextC = (chunkCoord + stepDirBounds) * chunkSize;
        float3 tMaxC = (nextC - ray.origin) * invDir;
        if (!(tMaxC.x > t - 0.1f) || abs(ray.direction.x) < 1e-6f) tMaxC.x = 3.4e38f;
        if (!(tMaxC.y > t - 0.1f) || abs(ray.direction.y) < 1e-6f) tMaxC.y = 3.4e38f;
        if (!(tMaxC.z > t - 0.1f) || abs(ray.direction.z) < 1e-6f) tMaxC.z = 3.4e38f;
        float tNext = min(min(tMaxC.x, tMaxC.y), tMaxC.z);
        mask = (tNext == tMaxC.x) ? int3(1,0,0) : ((tNext == tMaxC.y) ? int3(0,1,0) : int3(0,0,1));
        
        float pad = max(voxelSize * 1e-3f, tNext * 1e-5f);
        t = max(t + voxelSize * 1e-4f, tNext + pad);
    }
    return false;
}

