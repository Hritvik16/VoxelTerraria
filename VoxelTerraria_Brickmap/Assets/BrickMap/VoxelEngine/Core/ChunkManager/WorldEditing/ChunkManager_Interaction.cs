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

    public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    if (dist <= brushSize + 0.5f) RecordEditToDeltaMap(targetPos, newMaterial);
                }
            }
        }
        Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int minChunk = new Vector3Int(Mathf.FloorToInt(minGlobal.x / 32f), Mathf.FloorToInt(minGlobal.y / 32f), Mathf.FloorToInt(minGlobal.z / 32f));
        Vector3Int maxChunk = new Vector3Int(Mathf.FloorToInt(maxGlobal.x / 32f), Mathf.FloorToInt(maxGlobal.y / 32f), Mathf.FloorToInt(maxGlobal.z / 32f));

        for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
            for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
                for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
                    Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
                    int idx = GetMapIndex(0, chunkCoord);
                    if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;

                    // FIX: Read directly from the CPU master array! Zero GPU stalls!
                    ChunkData cd = chunkMapArray[idx]; 
                    uint denseIndex = 0;
                    if ((cd.packedState & 1) == 1) {
                        denseIndex = cd.densePoolIndex;
                    } else {
                        if (freeDenseIndices.Count == 0) continue;
                        denseIndex = freeDenseIndices.Dequeue();
                        cd.packedState |= 1; 
                        cd.densePoolIndex = denseIndex;
                        chunkMapArray[idx] = cd;
                        // chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
                    }

                    int localX = globalVoxelPos.x - (cx * 32);
                    int localY = globalVoxelPos.y - (cy * 32);
                    int localZ = globalVoxelPos.z - (cz * 32);
                    worldGenUtilityShader.SetBuffer(kernel_edit, "_DenseChunkPool", denseChunkPoolBuffer);
                    worldGenUtilityShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                    worldGenUtilityShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
                    worldGenUtilityShader.SetInt("_NewMaterialData", (int)newMaterial);
                    worldGenUtilityShader.SetInt("_BrushSize", brushSize);
                    worldGenUtilityShader.SetInt("_BrushShape", brushShape);
                    worldGenUtilityShader.Dispatch(kernel_edit, 1, 1, 1);
                    if (brushSize == 0) OnVoxelChanged?.Invoke(globalVoxelPos, newMaterial);
                }
            }
        }
    }

    public void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0) {
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    if (dist <= brushSize + 0.5f) RecordEditToDeltaMap(targetPos, 0); 
                }
            }
        }
        Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);
        if (brushSize > 0) OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);

        Vector3Int minChunk = new Vector3Int(Mathf.FloorToInt(minGlobal.x / 32f), Mathf.FloorToInt(minGlobal.y / 32f), Mathf.FloorToInt(minGlobal.z / 32f));
        Vector3Int maxChunk = new Vector3Int(Mathf.FloorToInt(maxGlobal.x / 32f), Mathf.FloorToInt(maxGlobal.y / 32f), Mathf.FloorToInt(maxGlobal.z / 32f));

        for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
            for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
                for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
                    Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
                    int idx = GetMapIndex(0, chunkCoord);
                    if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;
                    
                    // FIX: Read directly from the CPU master array! Zero GPU stalls!
                    ChunkData cd = chunkMapArray[idx]; 
                    uint denseIndex = 0;
                    if ((cd.packedState & 1) == 1) {
                        denseIndex = cd.densePoolIndex;
                    } else {
                        if (freeDenseIndices.Count == 0) continue;
                        denseIndex = freeDenseIndices.Dequeue();
                        cd.packedState |= 1; 
                        cd.densePoolIndex = denseIndex;
                        chunkMapArray[idx] = cd;
                        // chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
                    }

                    int localX = globalVoxelPos.x - (cx * 32);
                    int localY = globalVoxelPos.y - (cy * 32);
                    int localZ = globalVoxelPos.z - (cz * 32);
                    worldGenUtilityShader.SetBuffer(kernel_damage, "_DenseChunkPool", denseChunkPoolBuffer);
                    worldGenUtilityShader.SetBuffer(kernel_damage, "_DenseLogicPool", denseChunkPoolBuffer); // Dummy bind
                    worldGenUtilityShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                    worldGenUtilityShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
                    worldGenUtilityShader.SetInt("_DamageAmount", damageAmount);
                    worldGenUtilityShader.SetInt("_BrushSize", brushSize);
                    worldGenUtilityShader.SetInt("_BrushShape", brushShape);
                    worldGenUtilityShader.Dispatch(kernel_damage, 1, 1, 1);
                }
            }
        }
    }
}
