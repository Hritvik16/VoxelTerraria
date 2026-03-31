using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;

public partial class ChunkManager : MonoBehaviour, VoxelEngine.Interfaces.IVoxelWorld
{
    private struct EditedChunkInfo {
        public Vector3Int coord;
        public int mapIndex;
    }
    
    private Dictionary<uint, EditedChunkInfo> editorDirtyChunks = new Dictionary<uint, EditedChunkInfo>();
    
    // --- THE COURIER BUFFERS ---
    private ComputeBuffer editorChunkUploadBuffer;
    private ComputeBuffer editorMaskUploadBuffer;
    private ComputeBuffer editorJobQueueBuffer;
    private NativeArray<uint> editorChunkArray;
    private NativeArray<uint> editorMaskArray;
    private NativeArray<ChunkJobData> editorJobArray;
    private bool editorBuffersInitialized = false;

    private void InitEditorBuffers() {
        if (editorBuffersInitialized) return;
        int capacity = 128; // Can comfortably handle brush sizes up to 10
        editorChunkUploadBuffer = new ComputeBuffer(capacity * 1024, sizeof(uint));
        editorMaskUploadBuffer = new ComputeBuffer(capacity * 19, sizeof(uint));
        editorJobQueueBuffer = new ComputeBuffer(capacity, 48); 
        
        editorChunkArray = new NativeArray<uint>(capacity * 1024, Allocator.Persistent);
        editorMaskArray = new NativeArray<uint>(capacity * 19, Allocator.Persistent);
        editorJobArray = new NativeArray<ChunkJobData>(capacity, Allocator.Persistent);
        editorBuffersInitialized = true;
    }

    private void OnDestroy() {
        if (editorBuffersInitialized) {
            editorChunkUploadBuffer?.Release();
            editorMaskUploadBuffer?.Release();
            editorJobQueueBuffer?.Release();
            if (editorChunkArray.IsCreated) editorChunkArray.Dispose();
            if (editorMaskArray.IsCreated) editorMaskArray.Dispose();
            if (editorJobArray.IsCreated) editorJobArray.Dispose();
        }
    }

    // 100% CPU-Driven Math: Guarantees Physics, Visuals, and Persistence never desync!
    private uint TrackEditOnCPU(Vector3Int globalPos, bool isSolid, out Vector3Int outChunkCoord, out int outMapIndex) {
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
            if (freeDenseIndices.Count == 0 || !isSolid) return 0xFFFFFFFF;
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

        // 1. Persistence Tracking (Your original delta map goal)
        if (!worldDeltaMap.ContainsKey(chunkCoord)) worldDeltaMap[chunkCoord] = new Dictionary<int, uint>();
        worldDeltaMap[chunkCoord][flatIdx] = isSolid ? 1u : 0u;

        // 2. Physics & Visual Dense Memory
        if (isSolid) cpuDenseChunkPool[poolIndex] |= (1u << bitIdx);
        else cpuDenseChunkPool[poolIndex] &= ~(1u << bitIdx);

        // 3. Lightning Fast O(1) Shadow Mask Updates
        if (isSolid) {
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

        int i = 0;
        foreach (var kvp in editorDirtyChunks) {
            uint denseIndex = kvp.Key;
            EditedChunkInfo info = kvp.Value;

            // Pack the 4KB Chunk Data
            NativeArray<uint>.Copy(cpuDenseChunkPool, (int)denseIndex * 1024, editorChunkArray, i * 1024, 1024);
            
            // Pack the 76-Byte Mask Data
            NativeArray<uint>.Copy(cpuMacroMaskPool, (int)denseIndex * 19, editorMaskArray, i * 19, 19);

            // Construct the Job Data so the GPU knows exactly where to paste it!
            ChunkJobData jobData = new ChunkJobData {
                worldPos = new Vector4(info.coord.x * 32, info.coord.y * 32, info.coord.z * 32, 0),
                mapIndex = info.mapIndex,
                pad2 = (int)denseIndex,
                pad3 = 0 // 0 = Solid. This allows the GPU to successfully write the Spatial Anti-Ghosting Tag!
            };
            editorJobArray[i] = jobData;
            i++;
        }

        // Upload the tiny payload (Zero PCIe Stalls!)
        editorChunkUploadBuffer.SetData(editorChunkArray, 0, 0, dirtyCount * 1024);
        editorMaskUploadBuffer.SetData(editorMaskArray, 0, 0, dirtyCount * 19);
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
        worldGenUtilityShader.SetBuffer(kernel_commit, "_ChunkMap", chunkMapBuffer);

        int threadGroups = Mathf.CeilToInt((dirtyCount * 1024f) / 256f);
        worldGenUtilityShader.Dispatch(kernel_commit, threadGroups, 1, 1);
    }

    public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
        editorDirtyChunks.Clear();

        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    
                    float dist = brushShape == 0 ?
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : Mathf.Sqrt(x*x + y*y + z*z);

                    if (dist <= brushSize + 0.5f) {
                        uint dirtyIdx = TrackEditOnCPU(targetPos, true, out Vector3Int cCoord, out int mIdx);
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
        editorDirtyChunks.Clear();

        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ?
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : Mathf.Sqrt(x*x + y*y + z*z);

                    if (dist <= brushSize + 0.5f) {
                        uint dirtyIdx = TrackEditOnCPU(targetPos, false, out Vector3Int cCoord, out int mIdx);
                        if (dirtyIdx != 0xFFFFFFFF && !editorDirtyChunks.ContainsKey(dirtyIdx)) {
                            editorDirtyChunks[dirtyIdx] = new EditedChunkInfo { coord = cCoord, mapIndex = mIdx };
                        }
                    }
                }
            }
        }

        // Fire the Courier!
        DispatchCourier();

        if (brushSize > 0) {
            Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
            Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
            OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);
        }
        else OnVoxelChanged?.Invoke(globalVoxelPos, 0);
    }
}