using Unity.Collections;
using Unity.Jobs;          // <-- ADDED: For JobHandle and .Schedule()
using Unity.Mathematics;   // <-- ADDED: For high-performance math types
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine;
using VoxelEngine.Interfaces;
using VoxelEngine.World; // FIX: Tell ChunkManager to look inside the World namespace!
using System.Runtime.InteropServices;

public partial class ChunkManager : MonoBehaviour, IVoxelWorld
{

    void CompleteActiveJob() {
        activeTerrainJobHandle.Complete();
        // int voxelCountThisDispatch = activeDispatches * VOXELS_PER_CHUNK;
        int voxelCountThisDispatch = activeDispatches * UINTS_PER_CHUNK;

        // Inside the for(activeDispatches) loop:
        for (int i = 0; i < activeDispatches; i++) {
            int mapIndex = persistentJobDataArray[i].mapIndex;
            uint myDenseIndex = (uint)persistentJobDataArray[i].pad2;

            int bufferOffset = (int)myDenseIndex * UINTS_PER_CHUNK;
            NativeArray<uint>.Copy(cpuDenseChunkPool, bufferOffset, nativeChunkUpload, i * UINTS_PER_CHUNK, UINTS_PER_CHUNK);

            int maskOffset = (int)myDenseIndex * 19; // CHANGED to 19
            NativeArray<uint>.Copy(cpuMacroMaskPool, maskOffset, nativeMaskUpload, i * 19, 19);

            // NEW: Copy the 1-bit Surface Mask & Prefix Sum
            NativeArray<uint>.Copy(cpuSurfaceMaskPool, (int)myDenseIndex * 1024, nativeSurfaceUpload, i * 1024, 1024);
            NativeArray<uint>.Copy(cpuSurfacePrefixPool, (int)myDenseIndex * 1024, nativePrefixUpload, i * 1024, 1024);

            // --- NEW: THE SPARSE TICKET ASSIGNMENT ---
            bool fromVault = persistentJobDataArray[i].editCount == 1;
            uint ticket = cpuMaterialPointers[mapIndex];

            if (!fromVault) {
                int surfaceCount = persistentJobDataArray[i].editStartIndex; // Burst passed this back!
                
                if (surfaceCount > 0) {
                    if (ticket == 0xFFFFFFFF && freeMaterialIndices.Count > 0) {
                        ticket = freeMaterialIndices.Dequeue();
                        cpuMaterialPointers[mapIndex] = ticket;
                    }
                    if (ticket != 0xFFFFFFFF) {
                        // Copy from Courier (Burst) TO Master CPU Pool
                        NativeArray<uint>.Copy(nativeMaterialUpload, i * 4096, cpuMaterialChunkPool, (int)ticket * 4096, 4096);
                    }
                } else {
                    if (ticket != 0xFFFFFFFF) {
                        freeMaterialIndices.Enqueue(ticket);
                        ticket = 0xFFFFFFFF;
                        cpuMaterialPointers[mapIndex] = ticket;
                    }
                }
            } else {
                // If loaded from Vault (Phase 3 will fix the Vault array alignment)
                if (ticket != 0xFFFFFFFF) {
                    NativeArray<uint>.Copy(cpuMaterialChunkPool, (int)ticket * 4096, nativeMaterialUpload, i * 4096, 4096);
                } else {
                    for(int m=0; m<4096; m++) nativeMaterialUpload[i * 4096 + m] = 0;
                }
            }

            // Repack the ticket into the job so the GPU Courier knows exactly where to put it!
            ChunkJobData modifiedJob = persistentJobDataArray[i];
            modifiedJob.editStartIndex = (int)ticket;
            persistentJobDataArray[i] = modifiedJob;
            
            // Only update the map if the player hasn't moved away from this chunk!
            if (chunkMapArray[mapIndex].densePoolIndex == myDenseIndex) {
                ChunkData cd = chunkMapArray[mapIndex];
                
                if (persistentJobDataArray[i].pad3 == 1) {
                    if (cd.densePoolIndex != 0xFFFFFFFF) {
                        freeDenseIndices.Enqueue(cd.densePoolIndex);
                        cd.densePoolIndex = 0xFFFFFFFF;
                    }
                }
                
                // --- THE 0-BYTE SPATIAL TAG ---
                uint state = (persistentJobDataArray[i].pad3 == 1) ? 3u : 1u;
                
                // THE FIX: Do not divide by layerScale!
                int cx = Mathf.FloorToInt(persistentJobDataArray[i].worldPos.x / 32f);
                int cy = Mathf.FloorToInt(persistentJobDataArray[i].worldPos.y / 32f);
                int cz = Mathf.FloorToInt(persistentJobDataArray[i].worldPos.z / 32f);
                
                uint px = (uint)(cx & 0xFF);
                uint py = (uint)(cy & 0xFF);
                uint pz = (uint)(cz & 0xFF);
                
                cd.packedState = state | (px << 8) | (py << 16) | (pz << 24);
                
                chunkMapArray[mapIndex] = cd;
                // dirtyLightChunks.Enqueue(mapIndex);
            }
        }

        // Upload to the CURRENT active ring buffer
        // The Zero-GC Instant Uploads!
        tempChunkUploadBuffers[ringIndex].SetData(nativeChunkUpload, 0, 0, activeDispatches * 1024);
        jobQueueBuffers[ringIndex].SetData(persistentJobDataArray, 0, 0, activeDispatches);
        tempMaskUploadBuffers[ringIndex].SetData(nativeMaskUpload, 0, 0, activeDispatches * 19); // CHANGED to 19
        
        // THE FIX: Push the 8-bit material arrays to the GPU! (4096 uints per chunk)
        tempMaterialUploadBuffers[ringIndex].SetData(nativeMaterialUpload, 0, 0, activeDispatches * 4096);
        
        // NEW: Push the Surface Mask & Prefix Sum to the GPU
        tempSurfaceUploadBuffers[ringIndex].SetData(nativeSurfaceUpload, 0, 0, activeDispatches * 1024);
        tempPrefixUploadBuffers[ringIndex].SetData(nativePrefixUpload, 0, 0, activeDispatches * 1024);

        chunkHeightBuffer.SetData(cpuChunkHeights);
        
        // Push the new Ticket assignments to the Raytracer!
        materialPointersBuffer.SetData(cpuMaterialPointers);

        int commitKernel = worldGenUtilityShader.FindKernel("CommitUploadBuffers"); 
        worldGenUtilityShader.SetInt("_JobCount", activeDispatches);
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempChunkUpload", tempChunkUploadBuffers[ringIndex]);
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempLogicUpload", tempChunkUploadBuffers[ringIndex]); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_DenseChunkPool", denseChunkPoolBuffer);
        worldGenUtilityShader.SetBuffer(commitKernel, "_DenseLogicPool", denseChunkPoolBuffer); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_JobQueue", jobQueueBuffers[ringIndex]); 
        
        // NEW: Bind the destination and courier buffers
        worldGenUtilityShader.SetBuffer(commitKernel, "_MacroMaskPool", macroMaskPoolBuffer);
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempMaskUpload", tempMaskUploadBuffers[ringIndex]);
        worldGenUtilityShader.SetBuffer(commitKernel, "_MaterialChunkPool", materialChunkPoolBuffer); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempMaterialUpload", tempMaterialUploadBuffers[ringIndex]); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_SurfaceMaskPool", surfaceMaskPoolBuffer); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempSurfaceUpload", tempSurfaceUploadBuffers[ringIndex]); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_SurfacePrefixPool", surfacePrefixPoolBuffer); // NEW
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempPrefixUpload", tempPrefixUploadBuffers[ringIndex]); // NEW
        worldGenUtilityShader.SetBuffer(commitKernel, "_ChunkMap", chunkMapBuffer);
        
        int threadGroups = Mathf.CeilToInt((activeDispatches * UINTS_PER_CHUNK) / 256f);
        worldGenUtilityShader.Dispatch(commitKernel, threadGroups, 1, 1);
        // int threadGroups = Mathf.CeilToInt((activeDispatches * UINTS_PER_CHUNK) / 256f);
        // worldGenUtilityShader.Dispatch(commitKernel, threadGroups, 1, 1);

        // --- NEW: DISPATCH THE COMPUTE RASTERIZER ---
        if (currentRenderer == RenderBackend.ComputeRasterizer && greedyMesherShader != null) {
            int meshKernel = greedyMesherShader.FindKernel("BuildMesh");
            
            // We pass the exact same JobQueue so the Mesher knows which chunks just generated!
            greedyMesherShader.SetBuffer(meshKernel, "_JobQueue", jobQueueBuffers[ringIndex]);
            greedyMesherShader.SetInt("_JobCount", activeDispatches);
            
            greedyMesherShader.SetBuffer(meshKernel, "_DenseChunkPool", denseChunkPoolBuffer);
            greedyMesherShader.SetBuffer(meshKernel, "_VertexBuffer", vertexBuffer);
            greedyMesherShader.SetBuffer(meshKernel, "_ArgsBuffer", argsBuffer); // Tells the GPU to increase the triangle count
            
            // We dispatch exactly 1 Thread Group per chunk. 
            // The shader will use [numthreads(8,8,8)] to process all 32x32x32 voxels in parallel.
            greedyMesherShader.Dispatch(meshKernel, activeDispatches, 1, 1);
        }

        isTerrainJobRunning = false;
        activeDispatches = 0;

    }

    /*
    private void ProcessDirtyLightChunks() {
        int processedThisFrame = 0;
        int maxPerFrame = 5; // Throttle to prevent CPU spikes
        int attempts = dirtyLightChunks.Count;

        while (processedThisFrame < maxPerFrame && attempts > 0) {
            attempts--;
            int mapIndex = dirtyLightChunks.Dequeue();
            ChunkData cd = chunkMapArray[mapIndex];

            // 1. Stale Check: If the chunk was unloaded while waiting in line, ignore it.
            if (cd.densePoolIndex == 0xFFFFFFFF) continue;

            int layer = mapIndex / chunksPerLayer;
            Vector3Int coord = chunkTargetCoordArray[mapIndex];

            // 2. Neighbor Check: Do the 6 adjacent chunks exist yet?
            bool allNeighborsReady = true;
            Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back };
            
            foreach (Vector3Int dir in dirs) {
                int nIdx = GetMapIndex(layer, coord + dir);
                // If the neighbor's state is 0, it hasn't finished geometry generation.
                if (chunkTargetCoordArray[nIdx] != coord + dir || chunkMapArray[nIdx].packedState == 0) {
                    allNeighborsReady = false;
                    break;
                }
            }

            // 3. The Execution
            if (allNeighborsReady) {
                // TODO: Run C# Burst Light Propagation Job!
                
                // For now, just mark it as processed
                processedThisFrame++;
            } else {
                // 4. The Patience: Not ready yet. Throw it to the back of the line to check again later.
                dirtyLightChunks.Enqueue(mapIndex);
            }
        }
    }
    */


    private LayerCoord ExtractClosestChunk(List<LayerCoord> queue, Vector3Int centerCoord) {
        if (queue.Count == 1) {
            LayerCoord lc = queue[0];
            queue.RemoveAt(0);
            return lc;
        }

        int closestIdx = -1;
        int minDistance = int.MaxValue;
        
        for (int i = 0; i < queue.Count; i++) {
            Vector3Int c = queue[i].coord;
            int dx = c.x - centerCoord.x;
            int dy = c.y - centerCoord.y;
            int dz = c.z - centerCoord.z;
            int distSq = (dx * dx) + (dy * dy) + (dz * dz);
            
            if (distSq < minDistance) {
                minDistance = distSq;
                closestIdx = i;
            }
        }
        
        LayerCoord closest = queue[closestIdx];
        
        int lastIdx = queue.Count - 1;
        queue[closestIdx] = queue[lastIdx];
        queue.RemoveAt(lastIdx);
        
        return closest;
    }

    void DispatchNewJobs() {
        if (isTerrainJobRunning) return;

        if (worldLoaders.Count > 0 && worldLoaders[0] != null) {
            Rigidbody pBody = worldLoaders[0].GetComponent<Rigidbody>();
            Vector3 pPos = worldLoaders[0].position;
            Vector3 pVel = (pBody != null) ? pBody.linearVelocity : (Vector3.down * 50f); 
            Vector3 travelDir = pVel.magnitude > 1f ? pVel.normalized : Vector3.down;
        }



        int dispatchesThisFrame = 0;
        Stopwatch queueTimer = Stopwatch.StartNew(); 
        bool jobsAvailable = true;
        
        while (dispatchesThisFrame < maxConcurrentJobs && jobsAvailable && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
            jobsAvailable = false;
            for (int L = 0; L < clipmapLayers; L++) {
                if (dispatchesThisFrame >= maxConcurrentJobs) break;
                while (generationQueues[L].Count > 0 && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
                    if (freeDenseIndices.Count == 0) {
                        jobsAvailable = false;
                        break; 
                    }
                    
                    LayerCoord lc = ExtractClosestChunk(generationQueues[L], primaryAnchorChunks[L]);
                    
                    int idx = GetMapIndex(lc.layer, lc.coord);
                    if (chunkTargetCoordArray[idx] != lc.coord) {
                        continue;
                    }

                    uint denseIndex = freeDenseIndices.Dequeue();

                    float layerScale = voxelScale * (1 << lc.layer);
                    int chunkIndexInLayer = idx % chunksPerLayer;
                    
                    int targetLod = 1 << lc.layer; // Dynamically evaluates to 1, 2, 4, 8, 16...
                    int siloCap = (lc.layer == 0) ? 38000 : ((lc.layer == 1) ? 8192 : 256);

                    // Dynamic legacy 32-bit SVO offset calculation
                    uint safeRootIndex = 8;
                    for (int prevLayer = 0; prevLayer < lc.layer; prevLayer++) {
                        int prevSiloCap = (prevLayer == 0) ? 38000 : ((prevLayer == 1) ? 8192 : 256);
                        safeRootIndex += (uint)(chunksPerLayer * prevSiloCap);
                    }
                    safeRootIndex += (uint)(chunkIndexInLayer * siloCap);

                    // --- THE RAM VAULT LOADER ---
                    bool hasCache = (lc.layer == 0) && modifiedChunks.ContainsKey(lc.coord);
                    if (hasCache) {
                        RestoreFromVault(lc.coord, denseIndex);
                    }

                    pendingJobsArray[dispatchesThisFrame] = new ChunkJobData {
                        worldPos = new Vector4(lc.coord.x * chunkSize, lc.coord.y * chunkSize, lc.coord.z * chunkSize, siloCap),
                        mapIndex = idx,
                        lodStep = targetLod,
                        layerScale = layerScale,
                        pad1 = safeRootIndex,
                        editStartIndex = 0, // Unused
                        editCount = hasCache ? 1 : 0, // Tells the generator to EXIT EARLY if cached!
                        pad2 = (int)denseIndex, 
                        pad3 = 0
                    };

                    ChunkData cd = chunkMapArray[idx];
                    // cd.position = new Vector4(-99999f, -99999f, -99999f, 0f);
                    // cd.rootNodeIndex = safeRootIndex; 
                    cd.packedState = 0; 
                    cd.densePoolIndex = denseIndex;
                    chunkMapArray[idx] = cd;

                    dispatchesThisFrame++;
                    jobsAvailable = true;
                    break;
                }
            }
        }

        if (dispatchesThisFrame > 0) {
            activeDispatches = dispatchesThisFrame;

            ringIndex = (ringIndex + 1) % 2; 
            
            for(int i = 0; i < dispatchesThisFrame; i++) persistentJobDataArray[i] = pendingJobsArray[i];

            var fList = VoxelEngine.WorldManager.Instance.mapFeatures;
            int persistentIndex = 0;
            foreach (var feature in fList) {
                if (feature.topologyID != 0) { // It's a Topology Feature!
                    persistentFeatureArray[persistentIndex++] = feature;
                    if (persistentIndex >= 5000) break;
                }
            }
            int fCount = persistentIndex;
            var cList = VoxelEngine.WorldManager.Instance.cavernNodes;
            int cCount = Mathf.Min(cList.Count, 5000);
            for(int i = 0; i < cCount; i++) persistentCavernArray[i] = cList[i];

            var tList = VoxelEngine.WorldManager.Instance.tunnelSplines;
            int tCount = Mathf.Min(tList.Count, 5000);
            for(int i = 0; i < tCount; i++) persistentTunnelArray[i] = tList[i];

            jobQueueBuffers[ringIndex].SetData(persistentJobDataArray, 0, 0, dispatchesThisFrame);
            worldGenUtilityShader.SetInt("_JobCount", dispatchesThisFrame);
           worldGenUtilityShader.SetBuffer(kernel_clear, "_MacroGrid", macroGridBuffer);
            worldGenUtilityShader.SetBuffer(kernel_clear, "_ChunkMap", chunkMapBuffer); 
            worldGenUtilityShader.SetBuffer(kernel_clear, "_JobQueue", jobQueueBuffers[ringIndex]);
            worldGenUtilityShader.Dispatch(kernel_clear, dispatchesThisFrame, 1, 1);

            if (currentArchitecture == VoxelArchitecture.DualState1Bit) {
                TerrainGenJob_1Bit terrainJob = new TerrainGenJob_1Bit {
                    jobQueue = persistentJobDataArray, features = persistentFeatureArray,
                    caverns = persistentCavernArray, tunnels = persistentTunnelArray,
                    denseChunkPool = cpuDenseChunkPool, macroMaskPool = cpuMacroMaskPool, 
                    chunkHeights = cpuChunkHeights,
                    jobMaterialUpload = nativeMaterialUpload, // THE FIX: Hand Burst the Courier Array
                    cpuSurfaceMaskPool = cpuSurfaceMaskPool, 
                    cpuSurfacePrefixPool = cpuSurfacePrefixPool, 
                    cpuShadowRAMPool = cpuShadowRAMPool, // NEW
                    featureCount = fCount, cavernCount = cCount, tunnelCount = tCount,
                    biomes = cpuBiomes, biomeCount = cpuBiomes.IsCreated ? cpuBiomes.Length : 0
                };
                activeTerrainJobHandle = terrainJob.Schedule(dispatchesThisFrame, 1);
            } else {
                TerrainGenJob_32Bit terrainJob = new TerrainGenJob_32Bit {
                    jobQueue = persistentJobDataArray, features = persistentFeatureArray,
                    caverns = persistentCavernArray, tunnels = persistentTunnelArray,
                    denseChunkPool = cpuDenseChunkPool, macroMaskPool = cpuMacroMaskPool, 
                    worldRadiusXZ = VoxelEngine.WorldManager.Instance.WorldRadiusXZ,
                    worldSeed = VoxelEngine.WorldManager.Instance.worldSeed,
                    featureCount = fCount, cavernCount = cCount, tunnelCount = tCount 
                };
                activeTerrainJobHandle = terrainJob.Schedule(dispatchesThisFrame, 1);
            }
            isTerrainJobRunning = true;
        }
    }

    public void WaitForTerrainJobs() {
        if (isTerrainJobRunning) {
            activeTerrainJobHandle.Complete(); 
        }
    }
}
