using UnityEngine;
using System.Collections.Generic;

public class VoxelPhysicsManager : MonoBehaviour
{
    public static VoxelPhysicsManager Instance;
    public ChunkManager chunkManager;
    public List<VoxelBody> trackedBodies = new List<VoxelBody>();
    
    private GameObject[] colliderPool;
    private int poolSize = 1024; // Covers a massive area

    // --- ZERO-ALLOCATION CHUNK CACHE ---
    private Vector3Int lastChunkCoord = new Vector3Int(-99999, -99999, -99999);
    private int lastMapIdx = -1;
    private bool lastChunkValid = false;
    private uint lastDensePoolIndex = 0xFFFFFFFF;

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
    void FixedUpdate() {
        ChunkManager.Instance.WaitForTerrainJobs();
        SyncPhysics();
    }

    public void RegisterBody(VoxelBody body) { if (!trackedBodies.Contains(body)) trackedBodies.Add(body); }
    public void UnregisterBody(VoxelBody body) { trackedBodies.Remove(body); }

    public void SyncPhysics() {
        if (chunkManager == null || !chunkManager.cpuDenseChunkPool.IsCreated) return;
        float scale = chunkManager.voxelScale;

        foreach (var body in trackedBodies) {
            
            // 1. SAFETY NET (Void Fall Prevention)
            int cx = Mathf.FloorToInt(body.transform.position.x / (32f * scale));
            int cy = Mathf.FloorToInt(body.transform.position.y / (32f * scale)); 
            int cz = Mathf.FloorToInt(body.transform.position.z / (32f * scale));
            Vector3Int bodyChunk = new Vector3Int(cx, cy, cz);
            
            int mapIdx = chunkManager.GetMapIndex(0, bodyChunk);
            bool chunkIsSafe = false;
            
            if (mapIdx >= 0 && mapIdx < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[mapIdx] == bodyChunk) {
                uint state = chunkManager.chunkMapArray[mapIdx].packedState;
                if ((state & 0xFF) == 1u || (state & 0xFF) == 3u) chunkIsSafe = true; // 1=Rock, 3=Sky
            }

            Rigidbody rb = body.GetComponent<Rigidbody>();
            if (rb != null) {
                if (!chunkIsSafe && !rb.isKinematic) rb.isKinematic = true;
                else if (chunkIsSafe && rb.isKinematic) rb.isKinematic = false;
            }

            // 2. TIGHT BOUNDING BOX
            int minX = Mathf.FloorToInt((body.transform.position.x - 0.5f) / scale);
            int minY = Mathf.FloorToInt((body.transform.position.y - 2.5f) / scale); 
            int minZ = Mathf.FloorToInt((body.transform.position.z - 0.5f) / scale);
            
            int maxX = Mathf.FloorToInt((body.transform.position.x + 0.5f) / scale);
            int maxY = Mathf.FloorToInt((body.transform.position.y + 1.5f) / scale);
            int maxZ = Mathf.FloorToInt((body.transform.position.z + 0.5f) / scale);

            int poolIdx = 0;

            // 3. THE 1D GREEDY STRIP ALGORITHM
            for (int y = minY; y <= maxY; y++) {
                for (int z = minZ; z <= maxZ; z++) {
                    
                    int startX = minX;
                    int currentLength = 0;

                    // Notice we check up to maxX + 1 to force the strip to close at the end
                    for (int x = minX; x <= maxX + 1; x++) {
                        
                        bool isSolid = (x <= maxX) && (FastGetVoxel(x, y, z) > 0);

                        if (isSolid) {
                            if (currentLength == 0) startX = x; // Start a new strip
                            currentLength++;
                        } else {
                            if (currentLength > 0) {
                                // Close the strip and spawn exactly ONE merged collider!
                                if (poolIdx < poolSize) {
                                    GameObject colObj = colliderPool[poolIdx];
                                    BoxCollider box = colObj.GetComponent<BoxCollider>();

                                    // Position it exactly in the center of the greedy strip
                                    float centerX = (startX + (currentLength - 1) * 0.5f) * scale;
                                    colObj.transform.position = new Vector3(centerX, y * scale, z * scale);
                                    
                                    // Scale the box on the X axis to cover the merged blocks
                                    box.size = new Vector3(currentLength * scale, scale, scale);

                                    if (!colObj.activeSelf) colObj.SetActive(true);
                                    poolIdx++;
                                }
                                currentLength = 0; // Reset for the next strip
                            }
                        }
                    }
                }
            }

            // 4. Instantly hide unused boxes
            for (int i = poolIdx; i < poolSize; i++) {
                if (colliderPool[i].activeSelf) colliderPool[i].SetActive(false);
            }
        }
    }

    // --- THE CHUNK-LOCAL MEMORY CACHE ---
    // --- THE CHUNK-LOCAL MEMORY CACHE ---
    uint FastGetVoxel(int x, int y, int z) {
        int cx = Mathf.FloorToInt(x / 32f);
        int cy = Mathf.FloorToInt(y / 32f);
        int cz = Mathf.FloorToInt(z / 32f);
        Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
        
        // If we crossed a chunk boundary, do the heavy math. Otherwise, skip it!
        if (chunkCoord != lastChunkCoord) {
            lastChunkCoord = chunkCoord;
            lastMapIdx = chunkManager.GetMapIndex(0, chunkCoord);
            lastChunkValid = false;

            if (lastMapIdx >= 0 && lastMapIdx < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[lastMapIdx] == chunkCoord) {
                var cd = chunkManager.chunkMapArray[lastMapIdx];
                if ((cd.packedState & 1) == 1 && cd.densePoolIndex != 0xFFFFFFFF) {
                    lastChunkValid = true;
                    lastDensePoolIndex = cd.densePoolIndex;
                }
            }
        }

        if (lastChunkValid) {
            int lx = x - (cx * 32);
            int ly = y - (cy * 32);
            int lz = z - (cz * 32);
            
            if (lx >= 0 && lx < 32 && ly >= 0 && ly < 32 && lz >= 0 && lz < 32) {
                int flatIdx = lx + (ly << 5) + (lz << 10);
                
                // --- THE ARCHITECTURE TOGGLE ---
                if (chunkManager.currentArchitecture == ChunkManager.VoxelArchitecture.DualState1Bit) {
                    // 1-Bit Packed Read
                    uint packedBase = lastDensePoolIndex * 1024u;
                    uint matData = chunkManager.cpuDenseChunkPool[(int)packedBase + (flatIdx >> 5)];
                    return (matData & (1u << (flatIdx & 31))) != 0 ? 1u : 0u;
                } else {
                    // 32-Bit BrickMap Read
                    uint denseBase = lastDensePoolIndex * 32768u;
                    return chunkManager.cpuDenseChunkPool[(int)denseBase + flatIdx] & 0xFF;
                }
            }
        }
        return 0; 
    }
}