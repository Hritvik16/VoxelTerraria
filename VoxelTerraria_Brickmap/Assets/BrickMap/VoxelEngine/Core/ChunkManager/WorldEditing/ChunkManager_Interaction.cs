using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;

public partial class ChunkManager : MonoBehaviour, VoxelEngine.Interfaces.IVoxelWorld
{
    private struct EditedChunkInfo {
        public Vector3Int coord;
        public int mapIndex;
    }
    
    private Dictionary<uint, EditedChunkInfo> editorDirtyChunks = new Dictionary<uint, EditedChunkInfo>();
    
    // --- THE COURIER BUFFERS ---
    private ComputeBuffer editorChunkUploadBuffer; // RESTORED
    private ComputeBuffer editorMaskUploadBuffer;  // RESTORED
    private ComputeBuffer editorMaterialUploadBuffer; 
    private ComputeBuffer editorSurfaceUploadBuffer; 
    private ComputeBuffer editorPrefixUploadBuffer;  
    private ComputeBuffer editorJobQueueBuffer;
    private NativeArray<uint> editorChunkArray;
    private NativeArray<uint> editorMaskArray;
    private NativeArray<uint> editorMaterialArray; 
    private NativeArray<uint> editorSurfaceArray;    // NEW
    private NativeArray<uint> editorPrefixArray;     // NEW
    private NativeArray<ChunkJobData> editorJobArray;
    private bool editorBuffersInitialized = false;

    private void InitEditorBuffers() {
        if (editorBuffersInitialized) return;
        int capacity = 128; // Can comfortably handle brush sizes up to 10
        editorChunkUploadBuffer = new ComputeBuffer(capacity * 1024, sizeof(uint)); // RESTORED
        editorMaskUploadBuffer = new ComputeBuffer(capacity * 19, sizeof(uint));    // RESTORED
        editorMaterialUploadBuffer = new ComputeBuffer(capacity * 4096, sizeof(uint)); 
        editorSurfaceUploadBuffer = new ComputeBuffer(capacity * 1024, sizeof(uint)); 
        editorPrefixUploadBuffer = new ComputeBuffer(capacity * 1024, sizeof(uint));  
        editorJobQueueBuffer = new ComputeBuffer(capacity, 48); 
        
        editorChunkArray = new NativeArray<uint>(capacity * 1024, Allocator.Persistent);
        editorMaskArray = new NativeArray<uint>(capacity * 19, Allocator.Persistent);
        editorMaterialArray = new NativeArray<uint>(capacity * 4096, Allocator.Persistent); 
        editorSurfaceArray = new NativeArray<uint>(capacity * 1024, Allocator.Persistent); // NEW
        editorPrefixArray = new NativeArray<uint>(capacity * 1024, Allocator.Persistent);  // NEW
        editorJobArray = new NativeArray<ChunkJobData>(capacity, Allocator.Persistent);

        editorBuffersInitialized = true;
    }

    private void OnDestroy() {
        if (editorBuffersInitialized) {
            editorChunkUploadBuffer?.Release();    // RESTORED
            editorMaskUploadBuffer?.Release();     // RESTORED
            editorMaterialUploadBuffer?.Release(); 
            editorSurfaceUploadBuffer?.Release(); 
            editorPrefixUploadBuffer?.Release();  
            editorJobQueueBuffer?.Release();

            if (editorChunkArray.IsCreated) editorChunkArray.Dispose();
            if (editorMaskArray.IsCreated) editorMaskArray.Dispose();
            if (editorMaterialArray.IsCreated) editorMaterialArray.Dispose(); 
            if (editorSurfaceArray.IsCreated) editorSurfaceArray.Dispose(); // NEW
            if (editorPrefixArray.IsCreated) editorPrefixArray.Dispose();   // NEW
            if (editorJobArray.IsCreated) editorJobArray.Dispose();
        }
    }

    // 100% CPU-Driven Math: Guarantees Physics, Visuals, and Persistence never desync!
    private uint TrackEditOnCPU(Vector3Int globalPos, uint material, out Vector3Int outChunkCoord, out int outMapIndex) {
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(globalPos.x / 32f),
            Mathf.FloorToInt(globalPos.y / 32f),
            Mathf.FloorToInt(globalPos.z / 32f)
        );
        outChunkCoord = chunkCoord;

        int idx = GetMapIndex(0, chunkCoord);
        outMapIndex = idx;

        if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) return 0xFFFFFFFF;

        ChunkData cd = chunkMapArray[idx];
        uint denseIndex = 0;

        if ((cd.packedState & 1) == 1 && cd.densePoolIndex != 0xFFFFFFFF) {
            denseIndex = cd.densePoolIndex;
        } else {
            if (freeDenseIndices.Count == 0 || material == 0) return 0xFFFFFFFF;
            denseIndex = freeDenseIndices.Dequeue();

            cd.packedState = (cd.packedState & 0xFFFFFF00) | 1u;
            cd.densePoolIndex = denseIndex;
            chunkMapArray[idx] = cd;
            chunkMapBuffer.SetData(chunkMapArray, idx, idx, 1); // Upload map natively
            
            // Clear the new memory block
            int clearOffset = (int)(denseIndex * 1024u);
            for(int i = 0; i < 1024; i++) cpuDenseChunkPool[clearOffset + i] = 0;
            int maskOffset = (int)(denseIndex * 19);
            for(int i = 0; i < 19; i++) cpuMacroMaskPool[maskOffset + i] = 0;
        }

        int localX = globalPos.x - (chunkCoord.x * 32);
        int localY = globalPos.y - (chunkCoord.y * 32);
        int localZ = globalPos.z - (chunkCoord.z * 32);

        if (localX < 0 || localX >= 32 || localY < 0 || localY >= 32 || localZ < 0 || localZ >= 32) return 0xFFFFFFFF;

        int flatIdx = localX + (localY << 5) + (localZ << 10);
        int uintIdx = flatIdx >> 5;
        int bitIdx = flatIdx & 31;
        int poolIndex = (int)(denseIndex * 1024u) + uintIdx;

        // 2. Physics & Visual Dense Memory
        if (material > 0) cpuDenseChunkPool[poolIndex] |= (1u << bitIdx);
        else cpuDenseChunkPool[poolIndex] &= ~(1u << bitIdx);

        // --- NEW: UPDATE GROUND TRUTH SHADOW RAM ---
        if (shadowCoordMap.TryGetValue(chunkCoord, out uint shadowTicket)) {
            int shadowIndex = (int)(shadowTicket * 8192) + (flatIdx >> 2); 
            int shadowShift = (flatIdx & 3) << 3;
            uint shadowMask = ~(255u << shadowShift);
            cpuShadowRAMPool[shadowIndex] = (cpuShadowRAMPool[shadowIndex] & shadowMask) | (material << shadowShift);
        }

        // Flag for the Vault!
        editedChunkCoords.Add(chunkCoord);

        // 3. Lightning Fast O(1) Shadow Mask Updates
        if (material > 0) {
            int maskBase = (int)denseIndex * 19;
            int subIndex = (localX >> 2) + ((localY >> 2) << 3) + ((localZ >> 2) << 6);
            cpuMacroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31));

            int mip1Index = (localX >> 3) + ((localY >> 3) << 2) + ((localZ >> 3) << 4);
            cpuMacroMaskPool[maskBase + 16 + (mip1Index >> 5)] |= (1u << (mip1Index & 31));

            int mip2Index = (localX >> 4) + ((localY >> 4) << 1) + ((localZ >> 4) << 2);
            cpuMacroMaskPool[maskBase + 18] |= (1u << mip2Index);
        }

        return denseIndex;
    }

    private void DispatchCourier() {
        int dirtyCount = editorDirtyChunks.Count;
        if (dirtyCount == 0) return; 

        InitEditorBuffers();

        // --- THE FIX: CHAIN THE JOB HANDLES TO PREVENT ARRAY COLLISIONS ---
        JobHandle rebuildChain = default;
        // --- NEW: WE MUST EXTRACT TICKETS BEFORE THE JOBS RUN ---
        Dictionary<uint, uint> chunkTickets = new Dictionary<uint, uint>();

        foreach (var kvp in editorDirtyChunks) {
            uint denseIndex = kvp.Key;
            EditedChunkInfo info = kvp.Value;
            uint ticket = cpuMaterialPointers[info.mapIndex];

            // If the player edits a solid chunk without a ticket, grab one instantly!
            if (ticket == 0xFFFFFFFF) {
                if (freeMaterialIndices.Count > 0) ticket = freeMaterialIndices.Dequeue();
                else ticket = EvictFurthestMaterialTicket();
                
                cpuMaterialPointers[info.mapIndex] = ticket;
                ticketToMapIndex[ticket] = info.mapIndex;
            }
            chunkTickets[denseIndex] = ticket;

            EditorRebuildJob rebuildJob = new EditorRebuildJob {
                denseIndex = denseIndex,
                ticketIndex = ticket, 
                shadowTicketIndex = shadowCoordMap[info.coord], // Fetch the Shadow Ticket
                cpuDenseChunkPool = cpuDenseChunkPool,
                cpuShadowRAMPool = cpuShadowRAMPool,
                cpuMaterialChunkPool = cpuMaterialChunkPool,
                cpuSurfaceMaskPool = cpuSurfaceMaskPool,
                cpuSurfacePrefixPool = cpuSurfacePrefixPool
            };
            // Tell Unity's Job System to run these sequentially, passing the previous job as a dependency!
            rebuildChain = rebuildJob.Schedule(rebuildChain);
        }
        // Force the main thread to wait for the chain to finish repacking the arrays!
        rebuildChain.Complete();

        if (dirtyCount > 0) {
            int i = 0;
            foreach (var kvp in editorDirtyChunks) {
                uint denseIndex = kvp.Key;
                EditedChunkInfo info = kvp.Value;

                // Pack the 4KB Chunk Data
                NativeArray<uint>.Copy(cpuDenseChunkPool, (int)denseIndex * 1024, editorChunkArray, i * 1024, 1024);
                
                // Pack the 76-Byte Mask Data
                NativeArray<uint>.Copy(cpuMacroMaskPool, (int)denseIndex * 19, editorMaskArray, i * 19, 19);
                
                uint ticket = chunkTickets[denseIndex];
                // THE FIX: Pack materials using the Sparse Ticket!
                if (ticket != 0xFFFFFFFF) {
                    NativeArray<uint>.Copy(cpuMaterialChunkPool, (int)ticket * 4096, editorMaterialArray, i * 4096, 4096);
                } else {
                    for(int m = 0; m < 4096; m++) editorMaterialArray[i * 4096 + m] = 0;
                }
                
                // NEW: Pack the Surface Mask and Prefix Sum!
                NativeArray<uint>.Copy(cpuSurfaceMaskPool, (int)denseIndex * 1024, editorSurfaceArray, i * 1024, 1024);
                NativeArray<uint>.Copy(cpuSurfacePrefixPool, (int)denseIndex * 1024, editorPrefixArray, i * 1024, 1024);

                // Construct the Job Data so the GPU knows exactly where to paste it!
                ChunkJobData jobData = new ChunkJobData {
                    worldPos = new Vector4(info.coord.x * 32, info.coord.y * 32, info.coord.z * 32, 0),
                    mapIndex = info.mapIndex,
                    editStartIndex = (int)ticket, // THE FIX: Hand the ticket to the GPU Courier!
                    pad2 = (int)denseIndex,
                    pad3 = 0 // 0 = Solid. This allows the GPU to successfully write the Spatial Anti-Ghosting Tag!
                };
                editorJobArray[i] = jobData;
                i++;
            }

            // Upload the tiny payload (Zero PCIe Stalls!)
            editorChunkUploadBuffer.SetData(editorChunkArray, 0, 0, dirtyCount * 1024);
            editorMaskUploadBuffer.SetData(editorMaskArray, 0, 0, dirtyCount * 19);
            editorMaterialUploadBuffer.SetData(editorMaterialArray, 0, 0, dirtyCount * 4096);
            editorSurfaceUploadBuffer.SetData(editorSurfaceArray, 0, 0, dirtyCount * 1024); // NEW
            editorPrefixUploadBuffer.SetData(editorPrefixArray, 0, 0, dirtyCount * 1024);   // NEW
            editorJobQueueBuffer.SetData(editorJobArray, 0, 0, dirtyCount);

            // Command the GPU to natively copy the data into the Master Pools
            worldGenUtilityShader.SetInt("_JobCount", dirtyCount);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempChunkUpload", editorChunkUploadBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempLogicUpload", editorChunkUploadBuffer); 
            worldGenUtilityShader.SetBuffer(kernel_commit, "_DenseChunkPool", denseChunkPoolBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_DenseLogicPool", denseChunkPoolBuffer); 
            worldGenUtilityShader.SetBuffer(kernel_commit, "_JobQueue", editorJobQueueBuffer); 
            worldGenUtilityShader.SetBuffer(kernel_commit, "_MacroMaskPool", macroMaskPoolBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempMaskUpload", editorMaskUploadBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_MaterialChunkPool", materialChunkPoolBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempMaterialUpload", editorMaterialUploadBuffer);
            
            // NEW: Bind the missing Surface buffers so the GPU doesn't read garbage!
            worldGenUtilityShader.SetBuffer(kernel_commit, "_SurfaceMaskPool", surfaceMaskPoolBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempSurfaceUpload", editorSurfaceUploadBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_SurfacePrefixPool", surfacePrefixPoolBuffer);
            worldGenUtilityShader.SetBuffer(kernel_commit, "_TempPrefixUpload", editorPrefixUploadBuffer);
            
            worldGenUtilityShader.SetBuffer(kernel_commit, "_ChunkMap", chunkMapBuffer);

            // --- NEW: Sync the GPU Pointers in case the player triggered a new ticket! ---
            materialPointersBuffer.SetData(cpuMaterialPointers);

            int threadGroups = Mathf.CeilToInt((dirtyCount * 1024f) / 256f);
            worldGenUtilityShader.Dispatch(kernel_commit, threadGroups, 1, 1);
        }

        editorDirtyChunks.Clear();
    }
    public void RequestShadowTickets(Vector3Int globalCenter, int brushSize) {
        Vector3Int minGlobal = globalCenter - new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int maxGlobal = globalCenter + new Vector3Int(brushSize, brushSize, brushSize);

        Vector3Int minChunk = new Vector3Int(Mathf.FloorToInt(minGlobal.x / 32f), Mathf.FloorToInt(minGlobal.y / 32f), Mathf.FloorToInt(minGlobal.z / 32f));
        Vector3Int maxChunk = new Vector3Int(Mathf.FloorToInt(maxGlobal.x / 32f), Mathf.FloorToInt(maxGlobal.y / 32f), Mathf.FloorToInt(maxGlobal.z / 32f));

        int maxPossible = (maxChunk.x - minChunk.x + 1) * (maxChunk.y - minChunk.y + 1) * (maxChunk.z - minChunk.z + 1);
        NativeArray<ChunkJobData> jitJobData = new NativeArray<ChunkJobData>(maxPossible, Allocator.TempJob);
        int missingCount = 0;

        for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
            for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
                for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
                    Vector3Int cCoord = new Vector3Int(cx, cy, cz);
                    
                    if (!shadowCoordMap.ContainsKey(cCoord)) {
                        uint ticket;
                        if (freeShadowIndices.Count > 0) {
                            ticket = freeShadowIndices.Dequeue();
                        } else {
                            Vector3Int evictedCoord = shadowFifoQueue.Dequeue();
                            ticket = shadowCoordMap[evictedCoord];
                            shadowCoordMap.Remove(evictedCoord);
                            UnityEngine.Debug.LogWarning($"[Amnesia Cache] Evicted Shadow RAM for chunk {evictedCoord} to make room for {cCoord}");
                        }
                        
                        shadowFifoQueue.Enqueue(cCoord);
                        shadowCoordMap[cCoord] = ticket;

                        jitJobData[missingCount] = new ChunkJobData {
                            worldPos = new Vector4(cx * 32f, cy * 32f, cz * 32f, 0),
                            layerScale = voxelScale,
                            editCount = 2, // JIT SHADOW FLAG
                            editStartIndex = (int)ticket, // TICKET INDEX
                            mapIndex = 0 
                        };
                        missingCount++;
                    }
                }
            }
        }

        if (missingCount > 0) {
            VoxelEngine.World.TerrainGenJob_1Bit jitJob = new VoxelEngine.World.TerrainGenJob_1Bit {
                jobQueue = jitJobData,
                features = persistentFeatureArray, caverns = persistentCavernArray, tunnels = persistentTunnelArray,
                denseChunkPool = cpuDenseChunkPool, macroMaskPool = cpuMacroMaskPool, chunkHeights = cpuChunkHeights,
                jobMaterialUpload = nativeMaterialUpload, cpuSurfaceMaskPool = cpuSurfaceMaskPool,
                cpuSurfacePrefixPool = cpuSurfacePrefixPool, 
                cpuShadowRAMPool = cpuShadowRAMPool, // THIS IS WRITTEN TO IN THE JIT
                featureCount = VoxelEngine.WorldManager.Instance.mapFeatures.Count,
                cavernCount = Mathf.Min(VoxelEngine.WorldManager.Instance.cavernNodes.Count, 5000),
                tunnelCount = Mathf.Min(VoxelEngine.WorldManager.Instance.tunnelSplines.Count, 5000),
                worldSeed = VoxelEngine.WorldManager.Instance.worldSeed,
                biomes = cpuBiomes, biomeCount = cpuBiomes.IsCreated ? cpuBiomes.Length : 0
            };
            jitJob.Schedule(missingCount, 1).Complete(); // Executes instantly on Main Thread!
        }
        jitJobData.Dispose();
    }

    public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
        WaitForTerrainJobs(); // THE FIX: Wait for background gen to finish before mutating shared pools!
        RequestShadowTickets(globalVoxelPos, brushSize); // THE JIT TRIGGER
        editorDirtyChunks.Clear();

        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    
                    float dist = brushShape == 0 ?
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : Mathf.Sqrt(x*x + y*y + z*z);

                    if (dist <= brushSize + 0.5f) {
                        uint dirtyIdx = TrackEditOnCPU(targetPos, newMaterial, out Vector3Int cCoord, out int mIdx);
                        if (dirtyIdx != 0xFFFFFFFF && !editorDirtyChunks.ContainsKey(dirtyIdx)) {
                            editorDirtyChunks[dirtyIdx] = new EditedChunkInfo { coord = cCoord, mapIndex = mIdx };
                        }
                    }
                }
            }
        }

        // Fire the Courier!
        DispatchCourier();

        if (brushSize == 0) OnVoxelChanged?.Invoke(globalVoxelPos, newMaterial);
        else {
            Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
            Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
            OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);
        }
    }

    public void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0) {
        WaitForTerrainJobs(); // THE FIX: Wait for background gen to finish before mutating shared pools!
        RequestShadowTickets(globalVoxelPos, brushSize); // THE JIT TRIGGER
        editorDirtyChunks.Clear();

        // 1. THE "DEATH ROW" PASS
        HashSet<Vector3Int> blocksToDestroy = new HashSet<Vector3Int>();

        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : Mathf.Sqrt(x*x + y*y + z*z);
                    
                    if (dist <= brushSize + 0.5f) {
                        blocksToDestroy.Add(targetPos);
                    }
                }
            }
        }

        // 2. EXECUTE EDITS (Destruction)
        foreach (Vector3Int doomedBlock in blocksToDestroy) {
            uint dirtyIdx = TrackEditOnCPU(doomedBlock, 0, out Vector3Int cCoord, out int mIdx);
            if (dirtyIdx != 0xFFFFFFFF && !editorDirtyChunks.ContainsKey(dirtyIdx)) {
                editorDirtyChunks[dirtyIdx] = new EditedChunkInfo { coord = cCoord, mapIndex = mIdx };
            }
        }

        DispatchCourier();

        if (brushSize > 0) {
            Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
            Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
            OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);
        }
        else OnVoxelChanged?.Invoke(globalVoxelPos, 0);
    }

    // CheckAndRestoreChunks purged as RAM Vault handles it.
}
[BurstCompile(CompileSynchronously = true)]
public struct EditorRebuildJob : IJob
{
    public uint denseIndex;
    public uint ticketIndex; 
    public uint shadowTicketIndex; // NEW: The Shadow Ticket
    
    [ReadOnly] public NativeArray<uint> cpuDenseChunkPool;
    [ReadOnly] public NativeArray<uint> cpuShadowRAMPool;
    
    [NativeDisableParallelForRestriction] public NativeArray<uint> cpuMaterialChunkPool;
    [NativeDisableParallelForRestriction] public NativeArray<uint> cpuSurfaceMaskPool;
    [NativeDisableParallelForRestriction] public NativeArray<uint> cpuSurfacePrefixPool;

    public void Execute()
    {
        uint denseBase = denseIndex * 1024u;
        int shadowBase = (int)shadowTicketIndex * 8192; // THE FIX: Use the Shadow Ticket!
        int maskBase = (int)denseIndex * 1024;

        NativeArray<uint> localSurfaceMask = new NativeArray<uint>(1024, Allocator.Temp);
        NativeArray<uint> packedMaterials = new NativeArray<uint>(4096, Allocator.Temp);
        
        for (int i = 0; i < 4096; i++) packedMaterials[i] = 0;
        for (int i = 0; i < 1024; i++) localSurfaceMask[i] = 0;

        int packedCount = 0;

        for (int flatIdx = 0; flatIdx < 32768; flatIdx++) {
            int uintIdx = flatIdx >> 5;
            int bitIdx = flatIdx & 31;
            
            if ((cpuDenseChunkPool[(int)denseBase + uintIdx] & (1u << bitIdx)) != 0) {
                int x = flatIdx & 31;
                int y = (flatIdx >> 5) & 31;
                int z = (flatIdx >> 10) & 31;
                
                bool isSurface = false;
                if (x == 0 || x == 31 || y == 0 || y == 31 || z == 0 || z == 31) {
                    isSurface = true;
                } else {
                    int nx = flatIdx - 1;      int px = flatIdx + 1;
                    int ny = flatIdx - 32;     int py = flatIdx + 32;
                    int nz = flatIdx - 1024;   int pz = flatIdx + 1024;
                    
                    if ((cpuDenseChunkPool[(int)denseBase + (nx >> 5)] & (1u << (nx & 31))) == 0 ||
                        (cpuDenseChunkPool[(int)denseBase + (px >> 5)] & (1u << (px & 31))) == 0 ||
                        (cpuDenseChunkPool[(int)denseBase + (ny >> 5)] & (1u << (ny & 31))) == 0 ||
                        (cpuDenseChunkPool[(int)denseBase + (py >> 5)] & (1u << (py & 31))) == 0 ||
                        (cpuDenseChunkPool[(int)denseBase + (nz >> 5)] & (1u << (nz & 31))) == 0 ||
                        (cpuDenseChunkPool[(int)denseBase + (pz >> 5)] & (1u << (pz & 31))) == 0) {
                        isSurface = true;
                    }
                }
                
                if (isSurface && packedCount < 16384) { 
                    localSurfaceMask[uintIdx] |= (1u << bitIdx);
                    
                    // GRAB THE GROUND TRUTH COLOR FROM SHADOW RAM!
                    int mUintIdx = flatIdx >> 2;
                    int mShift = (flatIdx & 3) << 3;
                    uint matID = (cpuShadowRAMPool[shadowBase + mUintIdx] >> mShift) & 0xFF;
                    
                    int pUintIdx = packedCount >> 2;
                    int pShift = (packedCount & 3) << 3;
                    packedMaterials[pUintIdx] |= (matID << pShift);
                    
                    packedCount++;
                }
            }
        }

        int runningPrefix = 0;
        for (int i = 0; i < 1024; i++) {
            cpuSurfacePrefixPool[maskBase + i] = (uint)runningPrefix;
            runningPrefix += math.countbits(localSurfaceMask[i]);
            
            cpuSurfaceMaskPool[maskBase + i] = localSurfaceMask[i];
        }

        // THE FIX: Only write to the Master Pool if we actually have a Ticket!
        if (ticketIndex != 0xFFFFFFFF) {
            int packedBase = (int)ticketIndex * 4096;
            for (int i = 0; i < 4096; i++) {
                cpuMaterialChunkPool[packedBase + i] = packedMaterials[i];
            }
        }

        localSurfaceMask.Dispose();
        packedMaterials.Dispose();
    }
}
