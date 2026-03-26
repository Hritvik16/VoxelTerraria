using UnityEngine;
using System.Collections.Generic;

public class VoxelPhysicsManager : MonoBehaviour
{
    public static VoxelPhysicsManager Instance;
    public ChunkManager chunkManager;
    public List<VoxelBody> trackedBodies = new List<VoxelBody>();
    
    private GameObject[] colliderPool;
    private int poolSize = 1024; // Easily covers a 3x5x3 meter area without triggering GC

    void Awake() {
        Instance = this; 
        colliderPool = new GameObject[poolSize];
        for (int i = 0; i < poolSize; i++) {
            GameObject go = new GameObject("VoxelCol");
            go.transform.SetParent(this.transform);
            go.layer = gameObject.layer; 
            BoxCollider box = go.AddComponent<BoxCollider>();
            go.SetActive(false);
            colliderPool[i] = go;
        }
    }

    public void RegisterBody(VoxelBody body) { if (!trackedBodies.Contains(body)) trackedBodies.Add(body); }
    public void UnregisterBody(VoxelBody body) { trackedBodies.Remove(body); }

    public void SyncPhysics() {
        if (chunkManager == null || !chunkManager.cpuDenseChunkPool.IsCreated) return;
        float scale = chunkManager.voxelScale;

        // Ensure invisible colliders match the current 0.2m voxel scale
        if (colliderPool[0].GetComponent<BoxCollider>().size.x != scale) {
            for (int i = 0; i < poolSize; i++) colliderPool[i].GetComponent<BoxCollider>().size = Vector3.one * scale;
        }

        foreach (var body in trackedBodies) {
            // FIX: Check the player's current coordinate, not the coordinate below them!
            int cx = Mathf.FloorToInt(body.transform.position.x / (32f * scale));
            int cy = Mathf.FloorToInt(body.transform.position.y / (32f * scale)); 
            int cz = Mathf.FloorToInt(body.transform.position.z / (32f * scale));
            Vector3Int bodyChunk = new Vector3Int(cx, cy, cz);
            
            int mapIdx = chunkManager.GetMapIndex(0, bodyChunk);
            bool chunkIsSafe = false;
            
            if (mapIdx >= 0 && mapIdx < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[mapIdx] == bodyChunk) {
                uint state = chunkManager.chunkMapArray[mapIdx].packedState;
                if (state == 1u || state == 3u) chunkIsSafe = true; // 1=Rock, 3=Sky
            }

            Rigidbody rb = body.GetComponent<Rigidbody>();
            if (rb != null) {
                if (!chunkIsSafe) {
                    if (!rb.isKinematic) {
                        // Unity 6 automatically zeroes velocity when set to kinematic. No illegal calls!
                        rb.isKinematic = true;
                    }
                } else {
                    if (rb.isKinematic) rb.isKinematic = false;
                }
            }

            // --- INVISIBLE BOX POOLING ---
            // THE FIX: Tighten the X/Z bounds to exactly the player radius (0.5m).
            // Wide bounds waste the pool on empty space before it reaches your feet!
            int minX = Mathf.FloorToInt((body.transform.position.x - 0.5f) / scale);
            int minY = Mathf.FloorToInt((body.transform.position.y - 6.0f) / scale); 
            int minZ = Mathf.FloorToInt((body.transform.position.z - 0.5f) / scale);
            
            int maxX = Mathf.FloorToInt((body.transform.position.x + 0.5f) / scale);
            int maxY = Mathf.FloorToInt((body.transform.position.y + 2.0f) / scale);
            int maxZ = Mathf.FloorToInt((body.transform.position.z + 0.5f) / scale);

            int poolIdx = 0;
            for (int x = minX; x <= maxX; x++) {
                for (int y = minY; y <= maxY; y++) {
                    for (int z = minZ; z <= maxZ; z++) {
                        if (GetVoxel(x, y, z) > 0) {
                            if (poolIdx < poolSize) {
                                colliderPool[poolIdx].transform.position = new Vector3(x, y, z) * scale;
                                if (!colliderPool[poolIdx].activeSelf) colliderPool[poolIdx].SetActive(true);
                                poolIdx++;
                            }
                        }
                    }
                }
            }

            // Instantly hide unused boxes
            for (int i = poolIdx; i < poolSize; i++) {
                if (colliderPool[i].activeSelf) colliderPool[i].SetActive(false);
            }
        }
    }

    uint GetVoxel(int x, int y, int z) {
        int cx = Mathf.FloorToInt(x / 32f);
        int cy = Mathf.FloorToInt(y / 32f);
        int cz = Mathf.FloorToInt(z / 32f);
        Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
        
        int mapIdx = chunkManager.GetMapIndex(0, chunkCoord); 
        if (mapIdx >= 0 && mapIdx < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[mapIdx] == chunkCoord) {
            var cd = chunkManager.chunkMapArray[mapIdx];
            if ((cd.packedState & 1) == 1 && cd.densePoolIndex != 0xFFFFFFFF) {
                int lx = x - (cx * 32);
                int ly = y - (cy * 32);
                int lz = z - (cz * 32);
                if (lx >= 0 && lx < 32 && ly >= 0 && ly < 32 && lz >= 0 && lz < 32) {
                    return chunkManager.cpuDenseChunkPool[(int)(cd.densePoolIndex * 32768 + lx + (ly << 5) + (lz << 10))] & 0xFF;
                }
            }
        }
        return 0; 
    }
}