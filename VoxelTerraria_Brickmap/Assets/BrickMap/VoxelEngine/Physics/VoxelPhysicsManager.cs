using UnityEngine;
using System.Collections.Generic;

public class VoxelPhysicsManager : MonoBehaviour
{
    public static VoxelPhysicsManager Instance;
    public ChunkManager chunkManager;
    public List<VoxelBody> trackedBodies = new List<VoxelBody>();
    
    private GameObject[] colliderPool;
    private int poolSize = 4096; // Reverted to 4096 for massive high-speed fall boundaries

    // --- CASCADING CHUNK CACHE ---
    private Vector3Int lastQueryChunkL0 = new Vector3Int(-99999, -99999, -99999);
    private int activeCacheLayer = -1;
    private Vector3Int activeCacheChunkCoord = new Vector3Int(-99999, -99999, -99999);
    private uint activeCacheDenseIndex = 0xFFFFFFFF;

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

    void Start() {
        if (ChunkManager.Instance != null) {
            ChunkManager.Instance.OnVoxelChanged += ForceRebuild;
            ChunkManager.Instance.OnAreaDestroyed += ForceRebuildArea;
        }
    }


    public void RegisterBody(VoxelBody body) { if (!trackedBodies.Contains(body)) trackedBodies.Add(body); }
    public void UnregisterBody(VoxelBody body) { trackedBodies.Remove(body); }

    public void SyncPhysics() {
        if (chunkManager == null || !chunkManager.cpuDenseChunkPool.IsCreated) return;
        
        // THE FIX: Internal lock check removed. ChunkManager now guarantees this only runs when safe!

        // THE FIX: Invalidate cache every physics tick. 
        // This ensures the millisecond an LOD 0 job finishes, the colliders instantly snap to high-res!
        lastQueryChunkL0 = new Vector3Int(-99999, -99999, -99999);

        float scale = chunkManager.voxelScale;

        foreach (var body in trackedBodies) {
            
            // THE FIX: Actually trigger the velocity prediction!
            body.UpdateBounds(scale);

            // 1. CASCADING SAFETY NET
            bool chunkIsSafe = false;
            for (int L = 0; L < chunkManager.clipmapLayers; L++) {
                int cxL = Mathf.FloorToInt(body.transform.position.x / (32f * scale * (1 << L)));
                int cyL = Mathf.FloorToInt(body.transform.position.y / (32f * scale * (1 << L))); 
                int czL = Mathf.FloorToInt(body.transform.position.z / (32f * scale * (1 << L)));
                Vector3Int bodyChunkL = new Vector3Int(cxL, cyL, czL);
                
                int mapIdxL = chunkManager.GetMapIndex(L, bodyChunkL);
                if (mapIdxL >= 0 && mapIdxL < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[mapIdxL] == bodyChunkL) {
                    uint state = chunkManager.chunkMapArray[mapIdxL].packedState;
                    if ((state & 0xFF) == 1u || (state & 0xFF) == 3u) {
                        chunkIsSafe = true; // A valid layer exists under the player!
                        break;
                    }
                }
            }

            Rigidbody rb = body.GetComponent<Rigidbody>();
            if (rb != null) {
                if (!chunkIsSafe && !rb.isKinematic) {
                    rb.linearVelocity = Vector3.zero; 
                    rb.isKinematic = true;
                }
                else if (chunkIsSafe && rb.isKinematic) rb.isKinematic = false;
            }

            // 2. DYNAMIC PREDICTIVE BOUNDING BOX
            // THE FIX: Use your body's predicted grid bounds instead of the hardcoded 2.5m offset!
            int minX = body.minGridBound.x;
            int minY = body.minGridBound.y;
            int minZ = body.minGridBound.z;
            
            int maxX = body.maxGridBound.x;
            int maxY = body.maxGridBound.y;
            int maxZ = body.maxGridBound.z;

            int poolIdx = 0;

            // 3. THE 1D GREEDY STRIP ALGORITHM
            for (int y = minY; y <= maxY; y++) {
                for (int z = minZ; z <= maxZ; z++) {
                    
                    int startX = minX;
                    int currentLength = 0;

                    for (int x = minX; x <= maxX + 1; x++) {
                        
                        bool isSolid = (x <= maxX) && (FastGetVoxel(x, y, z) > 0);

                        if (isSolid) {
                            if (currentLength == 0) startX = x; 
                            currentLength++;
                        } else {
                            if (currentLength > 0) {
                                if (poolIdx < poolSize) {
                                    GameObject colObj = colliderPool[poolIdx];
                                    BoxCollider box = colObj.GetComponent<BoxCollider>();

                                    float centerX = (startX + (currentLength - 1) * 0.5f) * scale;
                                    colObj.transform.position = new Vector3(centerX, y * scale, z * scale);
                                    box.size = new Vector3(currentLength * scale, scale, scale);

                                    if (!colObj.activeSelf) colObj.SetActive(true);
                                    poolIdx++;
                                }
                                currentLength = 0; 
                            }
                        }
                    }
                }
            }

            for (int i = poolIdx; i < poolSize; i++) {
                if (colliderPool[i].activeSelf) colliderPool[i].SetActive(false);
            }
        }
    }

    // --- THE CASCADING CHUNK-LOCAL MEMORY CACHE ---
    uint FastGetVoxel(int x, int y, int z) {
        int cx0 = Mathf.FloorToInt(x / 32f);
        int cy0 = Mathf.FloorToInt(y / 32f);
        int cz0 = Mathf.FloorToInt(z / 32f);
        Vector3Int chunkCoordL0 = new Vector3Int(cx0, cy0, cz0);
        
        // 1. CHUNK BOUNDARY CHECK (Scan downward through LODs)
        if (chunkCoordL0 != lastQueryChunkL0) {
            lastQueryChunkL0 = chunkCoordL0;
            activeCacheLayer = -1;
            activeCacheDenseIndex = 0xFFFFFFFF;

            for (int L = 0; L < chunkManager.clipmapLayers; L++) {
                int scaledX = x >> L;
                int scaledY = y >> L;
                int scaledZ = z >> L;

                int cxL = Mathf.FloorToInt(scaledX / 32f);
                int cyL = Mathf.FloorToInt(scaledY / 32f);
                int czL = Mathf.FloorToInt(scaledZ / 32f);
                Vector3Int coordL = new Vector3Int(cxL, cyL, czL);

                int mapIdx = chunkManager.GetMapIndex(L, coordL);
                if (mapIdx >= 0 && mapIdx < chunkManager.chunkTargetCoordArray.Length && chunkManager.chunkTargetCoordArray[mapIdx] == coordL) {
                    var cd = chunkManager.chunkMapArray[mapIdx];
                    if ((cd.packedState & 1) == 1 && cd.densePoolIndex != 0xFFFFFFFF) {
                        activeCacheLayer = L;
                        activeCacheDenseIndex = cd.densePoolIndex;
                        activeCacheChunkCoord = coordL;
                        break; // Found the highest detail terrain available!
                    }
                }
            }
        }

        // 2. THE CASCADING READ
        if (activeCacheLayer != -1) {
            int L = activeCacheLayer;
            int scaledX = x >> L;
            int scaledY = y >> L;
            int scaledZ = z >> L;

            int lx = scaledX - (activeCacheChunkCoord.x * 32);
            int ly = scaledY - (activeCacheChunkCoord.y * 32);
            int lz = scaledZ - (activeCacheChunkCoord.z * 32);
            
            if (lx >= 0 && lx < 32 && ly >= 0 && ly < 32 && lz >= 0 && lz < 32) {
                int flatIdx = lx + (ly << 5) + (lz << 10);
                
                if (chunkManager.currentArchitecture == ChunkManager.VoxelArchitecture.DualState1Bit) {
                    uint packedBase = activeCacheDenseIndex * 1024u;
                    uint matData = chunkManager.cpuDenseChunkPool[(int)packedBase + (flatIdx >> 5)];
                    return (matData & (1u << (flatIdx & 31))) != 0 ? 1u : 0u;
                } else {
                    uint denseBase = activeCacheDenseIndex * 32768u;
                    return chunkManager.cpuDenseChunkPool[(int)denseBase + flatIdx] & 0xFF;
                }
            }
        }
        return 0; 
    }

    private void ForceRebuild(Vector3Int pos, uint mat) {
        foreach (var body in trackedBodies) {
            if (pos.x >= body.minGridBound.x && pos.x <= body.maxGridBound.x &&
                pos.y >= body.minGridBound.y && pos.y <= body.maxGridBound.y &&
                pos.z >= body.minGridBound.z && pos.z <= body.maxGridBound.z) {
                body.lastMinBound = new Vector3Int(-9999, -9999, -9999); 
            }
        }
    }

    private void ForceRebuildArea(Vector3Int min, Vector3Int max) {
        foreach (var body in trackedBodies) {
            if (min.x <= body.maxGridBound.x && max.x >= body.minGridBound.x &&
                min.y <= body.maxGridBound.y && max.y >= body.minGridBound.y &&
                min.z <= body.maxGridBound.z && max.z >= body.minGridBound.z) {
                body.lastMinBound = new Vector3Int(-9999, -9999, -9999);
            }
        }
    }
}