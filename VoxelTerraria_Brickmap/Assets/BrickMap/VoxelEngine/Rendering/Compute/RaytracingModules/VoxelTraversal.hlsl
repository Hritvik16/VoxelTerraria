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

        // --- THE CASCADING TORNADO (Smooth Linear Curve) ---
        int activeRadXZ = max(2, baseRadXZ - L);
        int activeRadY = baseRadY;

        if (L == 0) {
            activeRadY = min(4, baseRadY);
        } else if (L == 1) {
            activeRadY = min(6, baseRadY);
        } else if (L == 2) {
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
    
//     // --- THE CASCADING TORNADO (Smooth Linear Curve) ---
//     int activeRadXZ = max(2, baseRadXZ - (int)layer);
//     int activeRadY = baseRadY;
//
//     if (layer == 0) {
//         activeRadY = min(4, baseRadY);
//     } else if (layer == 1) {
//         activeRadY = min(6, baseRadY);
//     } else if (layer == 2) {
//         activeRadY = min(8, baseRadY);
//     }
//
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
    
    // --- THE CASCADING TORNADO (Smooth Linear Curve MATCH) ---
    int activeRadXZ = max(2, baseRadXZ - layer);
    int activeRadY = baseRadY;

    if (layer == 0) {
        activeRadY = min(4, baseRadY);
    } else if (layer == 1) {
        activeRadY = min(6, baseRadY);
    } else if (layer == 2) {
        activeRadY = min(8, baseRadY);
    }

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
    int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
    int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
    int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    
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
    float3 tDelta = 0; // Declare for function scope
    float3 tMaxV = 0;  // Declare for function scope

    int3 rayStep = sign(safeDir);
    int3 stepDirBounds = max(rayStep, 0);

    // for (int steps = 0; steps < 160; steps++) {
    for (int steps = 0; steps < 400; steps++) {
        raySteps++;
        if (t > maxDist) break;
        
        // float3 currentPos = ray.origin + ray.direction * t;
        float3 currentPos = ray.origin + safeDir * t;
        if (currentPos.y > 400.0f && ray.direction.y > -0.05f) break;

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

        // --- THE DDA HEARTBEAT (LOD-Sync) ---
        // We MUST update these every iteration because the layer (and voxelSize) 
        // can change dynamically during traversal.
        tDelta = abs(voxelSize * invDir);
        tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
        if (tMaxV.x <= t + 1e-5f) tMaxV.x += tDelta.x;
        if (tMaxV.y <= t + 1e-5f) tMaxV.y += tDelta.y;
        if (tMaxV.z <= t + 1e-5f) tMaxV.z += tDelta.z;

        // --- THE 2.5D SKY TELEPORTER ---
        int sideXZ = 2 * (int)_RenderBounds.x + 1;
        int sideY  = 2 * (int)_RenderBounds.y + 1;
        // int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
        // int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
        // int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
        int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
        int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
        int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
        int mapIndex = (layer * _ChunkCount) + mx + mz * sideXZ + my * sideXZ * sideXZ;

        float chunkMaxY = _ChunkHeightMap[mapIndex];
        float rayCurrentY = ray.origin.y + (ray.direction.y * t);
        uint fTicket = (layer == 0) ? _ChunkFluidPointers[mapIndex] : 0xFFFFFFFF; // FETCH FLUID TICKET
        
        // If the ray is ABOVE the mountain, AND there is no fluid ticket here...
        if (rayCurrentY > chunkMaxY && fTicket == 0xFFFFFFFF) {
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
                
                // --- FIX: SET THE TELEPORT NORMAL ---
                if (teleportT == tRoof) mask = int3(0, 1, 0);
                else if (teleportT == tMaxC.x) mask = int3(1, 0, 0);
                else mask = int3(0, 0, 1);

                float pad = max(voxelSize * 1e-3f, teleportT * 1e-5f);
                t = max(t + voxelSize * 1e-4f, teleportT + pad);
                
                // --- THE VOID KILLER: FULL DDA RE-SYNC ---
                // We must restore the DDA Heart (tMaxV) so the first voxel registers correctly!
                mapPos = int3(floor((ray.origin + safeDir * t) / voxelSize));
                tDelta = abs(voxelSize * invDir); 
                tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
                if (tMaxV.x <= t) tMaxV.x += tDelta.x;
                if (tMaxV.y <= t) tMaxV.y += tDelta.y;
                if (tMaxV.z <= t) tMaxV.z += tDelta.z;
                
                // Force a chunk data re-fetch at the new surface position
                continue; 
            }
        }

        // 1 = Dense, 3 = Empty
        uint denseIndex = currentChunk.densePoolIndex;
        if (packed == 1 || (fTicket != 0xFFFFFFFF && packed != 0)) {
            float3 chunkMinPos = chunkCoord * chunkSize;
            float3 chunkMaxPos = chunkMinPos + chunkSize; // ADD THIS LINE
            float3 tb0 = (chunkMinPos - ray.origin) * invDir;
            float3 tb1 = (chunkMaxPos - ray.origin) * invDir;
            float3 tMaxB = max(tb0, tb1);
            float tExitB = min(min(tMaxB.x, tMaxB.y), tMaxB.z);

            float3 tDelta = abs(voxelSize * invDir);
            float3 tMaxV = ((mapPos + stepDirBounds) * voxelSize - ray.origin) * invDir;
            
            // Pad the 't' check slightly, but securely reject bounds mathematically behind the camera
            if (tMaxV.x < -1.0f || abs(ray.direction.x) < 1e-6f) tMaxV.x = 3.4e38f; 
            else if (tMaxV.x <= t + 1e-5f) tMaxV.x += tDelta.x;
            
            if (tMaxV.y < -1.0f || abs(ray.direction.y) < 1e-6f) tMaxV.y = 3.4e38f; 
            else if (tMaxV.y <= t + 1e-5f) tMaxV.y += tDelta.y;
            
            if (tMaxV.z < -1.0f || abs(ray.direction.z) < 1e-6f) tMaxV.z = 3.4e38f; 
            else if (tMaxV.z <= t + 1e-5f) tMaxV.z += tDelta.z;

            uint denseBase = denseIndex << 15;
            bool hitInside = false;

            int vSteps = isShadow ? 32 : 128; // Give shadows a bit more breathing room to exit the chunk

            for (int v = 0; v < vSteps; v++) {
                if (t > tExitB + 0.001f) break;
                int3 vLocal = mapPos - (chunkCoord << 5);
                if (any(vLocal < 0) || any(vLocal > 31)) break;

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
                if (packed == 1) {
                    #if DUAL_STATE_1BIT
                        // 1 BIT TEST
                        int flatIdx = vLocal.x + (vLocal.y << 5) + (vLocal.z << 10);
                        uint packedBase = denseIndex * 1024u; 
                        uint matData = _DenseChunkPool[packedBase + (flatIdx >> 5)];
                        if ((matData & (1u << (flatIdx & 31))) != 0) { 
                            hitInside = true;
                            hitMatID_Data = 1; finalVoxelSize = voxelSize; finalBlockPos = mapPos; break;
                        }
                    #else
                        uint denseBase = denseIndex << 15;
                        uint matData = _DenseChunkPool[denseBase + vLocal.x + (vLocal.y << 5) + (vLocal.z << 10)];
                        if ((matData & 0xFF) > 0) { 
                            hitInside = true;
                            hitMatID_Data = matData; finalVoxelSize = voxelSize; finalBlockPos = mapPos; break;
                        }
                    #endif
                }
                
                // --- NEW: FLUID COLLISION CHECK ---
                if (fTicket != 0xFFFFFFFF) {
                    uint fData = Trace_GetFluidData(fTicket, vLocal);
                    if ((fData & 0x3F) > 0) {
                        hitInside = true; 
                        hitMatID_Data = fData | 0x80000000; // Flag 31st bit to scream "I AM FLUID!"
                        finalVoxelSize = voxelSize; 
                        finalBlockPos = mapPos; 
                        break;
                    }
                }
                
                // --- STANDARD ROBUST 32-BIT DDA ---
                if (tMaxV.x < tMaxV.y) {
                    if (tMaxV.x < tMaxV.z) { t = tMaxV.x;
                    tMaxV.x += tDelta.x; mapPos.x += rayStep.x; mask = int3(1,0,0); }
                    else { t = tMaxV.z;
                    tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                } else {
                    if (tMaxV.y < tMaxV.z) { t = tMaxV.y;
                    tMaxV.y += tDelta.y; mapPos.y += rayStep.y; mask = int3(0,1,0); }
                    else { t = tMaxV.z;
                    tMaxV.z += tDelta.z; mapPos.z += rayStep.z; mask = int3(0,0,1); }
                }
            }
            if (hitInside) return true;
        }
        
        // --- SAFE CHUNK ADVANCEMENT ---
        float3 nextC = (chunkCoord + stepDirBounds) * chunkSize;
        float3 tMaxC = (nextC - ray.origin) * invDir;
        
        // Prevent ignoring planes if the ray is perfectly parallel, but discard planes safely behind the ray
        if (tMaxC.x < t - 0.1f || abs(ray.direction.x) < 1e-6f) tMaxC.x = 3.4e38f;
        if (tMaxC.y < t - 0.1f || abs(ray.direction.y) < 1e-6f) tMaxC.y = 3.4e38f;
        if (tMaxC.z < t - 0.1f || abs(ray.direction.z) < 1e-6f) tMaxC.z = 3.4e38f;
        
        float tNext = min(min(tMaxC.x, tMaxC.y), tMaxC.z);
        mask = (tNext == tMaxC.x) ? int3(1,0,0) : ((tNext == tMaxC.y) ? int3(0,1,0) : int3(0,0,1));
        
        // Advance cleanly. We use max() to ensure we push forward securely regardless of whether 
        // we jumped an empty chunk or walked up to the boundary via the inner loop.
        float pad = max(1e-4f, voxelSize * 1e-3f);
        t = max(t + pad, tNext + pad);
    }
    return false;
}

