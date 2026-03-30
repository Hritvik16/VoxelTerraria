// using Unity.Collections;
// using Unity.Jobs;          // <-- ADDED: For JobHandle and .Schedule()
// using Unity.Mathematics;   // <-- ADDED: For high-performance math types
// using System.Diagnostics;
// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.Rendering;
// using VoxelEngine;
// using VoxelEngine.Interfaces;
// using VoxelEngine.World; // FIX: Tell ChunkManager to look inside the World namespace!
// using System.Runtime.InteropServices;

// public partial class ChunkManager : MonoBehaviour, IVoxelWorld
// {
//     private void RecordEditToDeltaMap(Vector3Int globalVoxelPos, uint newMaterial) {
//         Vector3Int chunkCoord = new Vector3Int(
//             Mathf.FloorToInt(globalVoxelPos.x / 32f),
//             Mathf.FloorToInt(globalVoxelPos.y / 32f),
//             Mathf.FloorToInt(globalVoxelPos.z / 32f)
//         );

//         int localX = globalVoxelPos.x - (chunkCoord.x * 32);
//         int localY = globalVoxelPos.y - (chunkCoord.y * 32);
//         int localZ = globalVoxelPos.z - (chunkCoord.z * 32);

//         int flatLocalIndex = localX + (localY << 5) + (localZ << 10);

//         if (!worldDeltaMap.ContainsKey(chunkCoord)) {
//             worldDeltaMap[chunkCoord] = new Dictionary<int, uint>();
//         }
//         worldDeltaMap[chunkCoord][flatLocalIndex] = newMaterial;
//     }

//     public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
//         for (int x = -brushSize; x <= brushSize; x++) {
//             for (int y = -brushSize; y <= brushSize; y++) {
//                 for (int z = -brushSize; z <= brushSize; z++) {
//                     Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
//                     float dist = brushShape == 0 ? 
//                         Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
//                     if (dist <= brushSize + 0.5f) RecordEditToDeltaMap(targetPos, newMaterial);
//                 }
//             }
//         }
//         Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
//         Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
//         Vector3Int minChunk = new Vector3Int(Mathf.FloorToInt(minGlobal.x / 32f), Mathf.FloorToInt(minGlobal.y / 32f), Mathf.FloorToInt(minGlobal.z / 32f));
//         Vector3Int maxChunk = new Vector3Int(Mathf.FloorToInt(maxGlobal.x / 32f), Mathf.FloorToInt(maxGlobal.y / 32f), Mathf.FloorToInt(maxGlobal.z / 32f));

//         for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
//             for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
//                 for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
//                     Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
//                     int idx = GetMapIndex(0, chunkCoord);
//                     if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;

//                     // FIX: Read directly from the CPU master array! Zero GPU stalls!
//                     ChunkData cd = chunkMapArray[idx]; 
//                     uint denseIndex = 0;
//                     if ((cd.packedState & 1) == 1) {
//                         denseIndex = cd.densePoolIndex;
//                     } else {
//                         if (freeDenseIndices.Count == 0) continue;
//                         denseIndex = freeDenseIndices.Dequeue();
//                         cd.packedState |= 1; 
//                         cd.densePoolIndex = denseIndex;
//                         chunkMapArray[idx] = cd;
//                         // chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
//                     }

//                     int localX = globalVoxelPos.x - (cx * 32);
//                     int localY = globalVoxelPos.y - (cy * 32);
//                     int localZ = globalVoxelPos.z - (cz * 32);
//                     worldGenUtilityShader.SetBuffer(kernel_edit, "_DenseChunkPool", denseChunkPoolBuffer);
//                     worldGenUtilityShader.SetInt("_TargetDenseIndex", (int)denseIndex);
//                     worldGenUtilityShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
//                     worldGenUtilityShader.SetInt("_NewMaterialData", (int)newMaterial);
//                     worldGenUtilityShader.SetInt("_BrushSize", brushSize);
//                     worldGenUtilityShader.SetInt("_BrushShape", brushShape);
//                     worldGenUtilityShader.Dispatch(kernel_edit, 1, 1, 1);
//                     if (brushSize == 0) OnVoxelChanged?.Invoke(globalVoxelPos, newMaterial);
//                 }
//             }
//         }
//     }

//     public void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0) {
//         for (int x = -brushSize; x <= brushSize; x++) {
//             for (int y = -brushSize; y <= brushSize; y++) {
//                 for (int z = -brushSize; z <= brushSize; z++) {
//                     Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
//                     float dist = brushShape == 0 ? 
//                         Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
//                     if (dist <= brushSize + 0.5f) RecordEditToDeltaMap(targetPos, 0); 
//                 }
//             }
//         }
//         Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
//         Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
//         if (brushSize > 0) OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);

//         Vector3Int minChunk = new Vector3Int(Mathf.FloorToInt(minGlobal.x / 32f), Mathf.FloorToInt(minGlobal.y / 32f), Mathf.FloorToInt(minGlobal.z / 32f));
//         Vector3Int maxChunk = new Vector3Int(Mathf.FloorToInt(maxGlobal.x / 32f), Mathf.FloorToInt(maxGlobal.y / 32f), Mathf.FloorToInt(maxGlobal.z / 32f));

//         for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
//             for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
//                 for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
//                     Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
//                     int idx = GetMapIndex(0, chunkCoord);
//                     if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;
                    
//                     // FIX: Read directly from the CPU master array! Zero GPU stalls!
//                     ChunkData cd = chunkMapArray[idx]; 
//                     uint denseIndex = 0;
//                     if ((cd.packedState & 1) == 1) {
//                         denseIndex = cd.densePoolIndex;
//                     } else {
//                         if (freeDenseIndices.Count == 0) continue;
//                         denseIndex = freeDenseIndices.Dequeue();
//                         cd.packedState |= 1; 
//                         cd.densePoolIndex = denseIndex;
//                         chunkMapArray[idx] = cd;
//                         // chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
//                     }

//                     int localX = globalVoxelPos.x - (cx * 32);
//                     int localY = globalVoxelPos.y - (cy * 32);
//                     int localZ = globalVoxelPos.z - (cz * 32);
//                     worldGenUtilityShader.SetBuffer(kernel_damage, "_DenseChunkPool", denseChunkPoolBuffer);
//                     worldGenUtilityShader.SetBuffer(kernel_damage, "_DenseLogicPool", denseChunkPoolBuffer); // Dummy bind
//                     worldGenUtilityShader.SetInt("_TargetDenseIndex", (int)denseIndex);
//                     worldGenUtilityShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
//                     worldGenUtilityShader.SetInt("_DamageAmount", damageAmount);
//                     worldGenUtilityShader.SetInt("_BrushSize", brushSize);
//                     worldGenUtilityShader.SetInt("_BrushShape", brushShape);
//                     worldGenUtilityShader.Dispatch(kernel_damage, 1, 1, 1);
//                 }
//             }
//         }
//     }
// }
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;

public partial class ChunkManager : MonoBehaviour, VoxelEngine.Interfaces.IVoxelWorld
{
    // --- THE BATCH TRACKERS ---
    // These track which chunks were touched during a brush stroke so we only upload exactly what changed.
    private HashSet<uint> dirtyDenseChunks = new HashSet<uint>();
    private HashSet<int> dirtyMapIndices = new HashSet<int>();

    private void RecordEditToDeltaMap(Vector3Int globalVoxelPos, uint newMaterial) {
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(globalVoxelPos.x / 32f),
            Mathf.FloorToInt(globalVoxelPos.y / 32f),
            Mathf.FloorToInt(globalVoxelPos.z / 32f)
        );

        int localX = globalVoxelPos.x - (chunkCoord.x * 32);
        int localY = globalVoxelPos.y - (chunkCoord.y * 32);
        int localZ = globalVoxelPos.z - (chunkCoord.z * 32);

        int flatLocalIndex = localX + (localY << 5) + (localZ << 10);

        if (!worldDeltaMap.ContainsKey(chunkCoord)) {
            worldDeltaMap[chunkCoord] = new Dictionary<int, uint>();
        }
        worldDeltaMap[chunkCoord][flatLocalIndex] = newMaterial;
    }

    // --- THE SILENT CPU EDITOR ---
    private void SetVoxel1Bit(Vector3Int globalPos, bool isSolid) {
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(globalPos.x / 32f),
            Mathf.FloorToInt(globalPos.y / 32f),
            Mathf.FloorToInt(globalPos.z / 32f)
        );

        int idx = GetMapIndex(0, chunkCoord); 
        if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) return;

        ChunkData cd = chunkMapArray[idx];
        uint denseIndex = 0;
        
        if ((cd.packedState & 1) == 1) {
            denseIndex = cd.densePoolIndex;
        } else {
            if (freeDenseIndices.Count == 0 || !isSolid) return; 
            denseIndex = freeDenseIndices.Dequeue();
            
            cd.packedState = (cd.packedState & 0xFFFFFF00) | 1u; 
            cd.densePoolIndex = denseIndex;
            chunkMapArray[idx] = cd;
            
            // Flag this Map Index for upload
            dirtyMapIndices.Add(idx); 
        }

        int localX = globalPos.x - (chunkCoord.x * 32);
        int localY = globalPos.y - (chunkCoord.y * 32);
        int localZ = globalPos.z - (chunkCoord.z * 32);

        if (localX < 0 || localX >= 32 || localY < 0 || localY >= 32 || localZ < 0 || localZ >= 32) return;

        int flatIdx = localX + (localY << 5) + (localZ << 10);
        int uintIdx = flatIdx >> 5;
        int bitIdx = flatIdx & 31;
        int poolIndex = (int)(denseIndex * 1024u) + uintIdx;

        // Flip the bit ONLY on the CPU
        uint currentData = cpuDenseChunkPool[poolIndex];
        if (isSolid) {
            currentData |= (1u << bitIdx);  
        } else {
            currentData &= ~(1u << bitIdx); 
        }
        cpuDenseChunkPool[poolIndex] = currentData;

        // Update the CPU Macro Mask
        if (isSolid) {
            int maskBase = (int)denseIndex * 19;
            int subIndex = (localX >> 2) + ((localY >> 2) << 3) + ((localZ >> 2) << 6);
            cpuMacroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31)); 
            
            int mip1Index = (localX >> 3) + ((localY >> 3) << 2) + ((localZ >> 3) << 4);
            cpuMacroMaskPool[maskBase + 16 + (mip1Index >> 5)] |= (1u << (mip1Index & 31)); 
            
            int mip2Index = (localX >> 4) + ((localY >> 4) << 1) + ((localZ >> 4) << 2);
            cpuMacroMaskPool[maskBase + 18] |= (1u << mip2Index); 
        }

        // Flag this chunk for batch upload!
        dirtyDenseChunks.Add(denseIndex);
        RecordEditToDeltaMap(globalPos, isSolid ? 1u : 0u);
    }

    // --- THE BATCH UPLOADER ---
    // Runs exactly ONCE per mouse click, regardless of brush size.
    private void FlushDirtyChunks() {
        if (dirtyMapIndices.Count > 0) {
            NativeArray<ChunkData> cdUpload = new NativeArray<ChunkData>(1, Allocator.Temp);
            foreach (int idx in dirtyMapIndices) {
                cdUpload[0] = chunkMapArray[idx];
                chunkMapBuffer.SetData(cdUpload, 0, idx, 1);
            }
            cdUpload.Dispose();
            dirtyMapIndices.Clear();
        }

        if (dirtyDenseChunks.Count > 0) {
            foreach (uint denseIndex in dirtyDenseChunks) {
                // Upload the 1024 uints (4KB) for this chunk in ONE shot
                denseChunkPoolBuffer.SetData(cpuDenseChunkPool, (int)(denseIndex * 1024), (int)(denseIndex * 1024), 1024);
                // Upload the 19 uints for the mask in ONE shot
                macroMaskPoolBuffer.SetData(cpuMacroMaskPool, (int)(denseIndex * 19), (int)(denseIndex * 19), 19);
            }
            dirtyDenseChunks.Clear();
        }
    }

    public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    
                    if (dist <= brushSize + 0.5f) {
                        SetVoxel1Bit(targetPos, true);
                    }
                }
            }
        }
        FlushDirtyChunks(); // Push to GPU!
        if (brushSize == 0) OnVoxelChanged?.Invoke(globalVoxelPos, newMaterial);
    }

    public void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0) {
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    
                    if (dist <= brushSize + 0.5f) {
                        SetVoxel1Bit(targetPos, false);
                    }
                }
            }
        }
        FlushDirtyChunks(); // Push to GPU!
        
        if (brushSize > 0) {
            Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
            Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
            OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);
        }
    }
}