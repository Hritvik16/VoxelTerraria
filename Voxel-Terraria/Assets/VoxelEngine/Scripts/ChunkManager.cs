using Unity.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class ChunkManager : MonoBehaviour
{
    [Header("Clipmap Architecture")]
    public int chunkSize = 32;
    public float voxelScale = 0.1f;
    [Range(1, 3)] public int clipmapLayers = 3;
    public int renderDistanceXZ = 18; // 39x39x19 bounds = ~28,899 chunks (Tier 0 is ~60m)
    public int renderDistanceY = 9;

    [Header("Performance")]
    public int maxConcurrentJobs = 800; 
    public float gpuUpdateInterval = 0.05f;

    [Header("Physics & Entities")]
    public List<Transform> worldLoaders = new List<Transform>();
    public ComputeShader worldGenShader;
    public ComputeShader voxelLightingShader;

    // DATA STRUCTURES
    public struct SVONode { public uint childIndex; public uint material; }
    public struct ChunkData {
        public Vector4 position;
        public uint rootNodeIndex;
        public int currentLOD;
        public uint pad1, pad2;
    }
    public struct ChunkJobData {
        public Vector4 worldPos;
        public int mapIndex;
        public int lodStep; 
        public float layerScale;
        public float pad1; 
    }
    public struct LayerCoord {
        public int layer;
        public Vector3Int coord;
    }

    private NativeArray<ChunkData> chunkMapArray;
    private NativeArray<Vector3Int> chunkTargetCoordArray; 
    
    private Queue<LayerCoord>[] generationQueues = new Queue<LayerCoord>[8];
    private Vector3Int[] scanOffsets;

    // private NativeArray<SVONode> masterNodePool;
    
    private const int POOL_SIZE = 671088512; 

    private ComputeBuffer svoPoolBuffer, chunkMapBuffer, jobQueueBuffer; 

    private Vector3Int[] primaryAnchorChunks = new Vector3Int[8];
    private Vector4[] shaderCenterChunks = new Vector4[8]; 

    private Stopwatch totalLoadTimer = new Stopwatch();
    private float deltaTime = 0.0f;
    private int chunksPerLayer, totalMapCapacity;

    [Header("Aesthetics")]
    public VoxelMaterialPalette globalPalette;
    private ComputeBuffer materialBuffer;

    public static ChunkManager Instance; // THE FIX: Define the Instance

    void Awake()
    {
        Instance = this; // THE FIX: Assign the Instance
    }

    // void Start() {
    //     if (globalPalette == null) {
    //         UnityEngine.Debug.LogWarning("Missing Global Palette! Create one and assign it in the Inspector.");
    //         return;
    //     }
        
    //     materialBuffer = new ComputeBuffer(256, 32);
    //     materialBuffer.SetData(globalPalette.materials);
    //     Shader.SetGlobalBuffer("_MaterialPalette", materialBuffer);
    // }

    void OnDestroy() {
        if (materialBuffer != null) materialBuffer.Release();
    }
    
    void OnEnable() {
        // if (!masterNodePool.IsCreated) masterNodePool = new NativeArray<SVONode>(POOL_SIZE, Allocator.Persistent);
        Instance = this;
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        chunksPerLayer = sideXZ * sideXZ * sideY;
        totalMapCapacity = chunksPerLayer * clipmapLayers; 
        
        // Stratified Static Pool: Balanced for 60m radius on M1 8GB
        int tier0Size = chunksPerLayer * 8192;
        int tier1Size = chunksPerLayer * 2048; // Increased from 1024 to allow full LOD 1 surface detail
        int tier2Size = chunksPerLayer * 256;  // Reduced from 512 as Tier 2 (far distance) only needs basic shapes
        int POOL_SIZE = tier0Size + tier1Size + tier2Size;

        if (!chunkMapArray.IsCreated) {
            chunkMapArray = new NativeArray<ChunkData>(totalMapCapacity, Allocator.Persistent);
            for (int i = 0; i < totalMapCapacity; i++) {
                chunkMapArray[i] = new ChunkData {
                    position = new Vector4(-99999f, -99999f, -99999f, 0f),
                    rootNodeIndex = 0xFFFFFFFF
                };
            }
        }
        if (!chunkTargetCoordArray.IsCreated) {
            chunkTargetCoordArray = new NativeArray<Vector3Int>(totalMapCapacity, Allocator.Persistent);
            for(int i = 0; i < totalMapCapacity; i++) chunkTargetCoordArray[i] = new Vector3Int(-99999, -99999, -99999);
        }

        List<Vector3Int> offsets = new List<Vector3Int>();
        for (int y = -renderDistanceY; y <= renderDistanceY; y++)
            for (int x = -renderDistanceXZ; x <= renderDistanceXZ; x++)
                for (int z = -renderDistanceXZ; z <= renderDistanceXZ; z++)
                    offsets.Add(new Vector3Int(x, y, z));
        
        offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
        scanOffsets = offsets.ToArray();

        for(int i = 0; i < 8; i++) {
            generationQueues[i] = new Queue<LayerCoord>();
            primaryAnchorChunks[i] = new Vector3Int(-9999, -9999, -9999);
        }

        if (svoPoolBuffer == null) {
            svoPoolBuffer = new ComputeBuffer(POOL_SIZE, 8);
            // svoPoolBuffer.SetData(masterNodePool);
            Shader.SetGlobalBuffer("_SVOPool", svoPoolBuffer);
        }
        if (jobQueueBuffer == null) jobQueueBuffer = new ComputeBuffer(maxConcurrentJobs, 32);

        if (chunkMapBuffer == null) {
            chunkMapBuffer = new ComputeBuffer(totalMapCapacity, 32);
            chunkMapBuffer.SetData(chunkMapArray);
            Shader.SetGlobalBuffer("_ChunkMap", chunkMapBuffer);
        }

        if (globalPalette != null && materialBuffer == null) {
            materialBuffer = new ComputeBuffer(256, 32);
            materialBuffer.SetData(globalPalette.materials);
            Shader.SetGlobalBuffer("_MaterialPalette", materialBuffer);
        }

        Shader.SetGlobalInt("_PoolSize", POOL_SIZE);
    }

    int GetMapIndex(int layer, Vector3Int coord) {
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        int mx = ((coord.x % sideXZ) + sideXZ) % sideXZ;
        int my = ((coord.y % sideY) + sideY) % sideY;
        int mz = ((coord.z % sideXZ) + sideXZ) % sideXZ;
        return (layer * chunksPerLayer) + (mx + (mz * sideXZ) + (my * sideXZ * sideXZ));
    }

    void Update() {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f; 
        if (worldLoaders.Count == 0 || worldLoaders[0] == null) return;

        bool anyAnchorChanged = false;

        for (int L = 0; L < clipmapLayers; L++) {
            float layerScale = voxelScale * (1 << L);
            float worldChunkSize = chunkSize * layerScale;
            
            Vector3Int currentAnchor = GetChunkCoord(worldLoaders[0].position, worldChunkSize);

            if (currentAnchor != primaryAnchorChunks[L]) {
                primaryAnchorChunks[L] = currentAnchor;
                shaderCenterChunks[L] = new Vector4(currentAnchor.x, currentAnchor.y, currentAnchor.z, layerScale); 
                
                CleanupAndQueue(L, currentAnchor);
                anyAnchorChanged = true;
            }
        }

        if (anyAnchorChanged) {
            UpdateGPUBuffers(); 
            totalLoadTimer.Restart();
        }

        DispatchJobs();

        bool allQueuesEmpty = true;
        for (int i = 0; i < clipmapLayers; i++) if (generationQueues[i].Count > 0) allQueuesEmpty = false;

        if (allQueuesEmpty && totalLoadTimer.IsRunning) {
            totalLoadTimer.Stop();
            UnityEngine.Debug.LogWarning($"✅ CLIPMAPS LOADED IN {totalLoadTimer.ElapsedMilliseconds} ms ✅");
            // System.GC.Collect(); // Forces the CPU to release unused temporary job data 
        }
    }

    void CleanupAndQueue(int layer, Vector3Int center) {
        float layerScale = voxelScale * (1 << layer);

        for (int i = 0; i < scanOffsets.Length; i++) {
            Vector3Int coord = center + scanOffsets[i];
            
            // THE FIX: Cylindrical culling AT THE SOURCE. 
            // If it's a corner, we skip it before it permanently corrupts the target coordinate array.
            float distXZ = Vector2.Distance(new Vector2(coord.x, coord.z), new Vector2(center.x, center.z));
            if (distXZ > renderDistanceXZ || Mathf.Abs(coord.y - center.y) > renderDistanceY) continue;

            int idx = GetMapIndex(layer, coord);
            float expectedX = coord.x * chunkSize;
            float expectedY = coord.y * chunkSize;
            float expectedZ = coord.z * chunkSize;
            
            Vector4 currentPos = chunkMapArray[idx].position;

            if ((currentPos.x != expectedX || currentPos.y != expectedY || currentPos.z != expectedZ) 
                && chunkTargetCoordArray[idx] != coord) {
                
                chunkTargetCoordArray[idx] = coord; 
                generationQueues[layer].Enqueue(new LayerCoord { layer = layer, coord = coord });
            }
        }
    }

    void DispatchJobs() {
        int dispatchesThisFrame = 0;
        int maxAttempts = maxConcurrentJobs * 3; 
        NativeArray<ChunkJobData> jobDataArray = new NativeArray<ChunkJobData>(maxConcurrentJobs, Allocator.Temp);

        bool jobsAvailable = true;
        
        while (dispatchesThisFrame < maxConcurrentJobs && jobsAvailable && maxAttempts > 0) {
            jobsAvailable = false;
            
            for (int L = 0; L < clipmapLayers; L++) {
                if (dispatchesThisFrame >= maxConcurrentJobs) break;
                
                while (generationQueues[L].Count > 0 && maxAttempts > 0) {
                    maxAttempts--;
                    LayerCoord lc = generationQueues[L].Dequeue();
                    int idx = GetMapIndex(lc.layer, lc.coord);
                    
                    if (chunkTargetCoordArray[idx] != lc.coord) continue;

                    Vector3Int center = primaryAnchorChunks[lc.layer];
                    // The buggy distance check here was removed. Culling is now safely handled upstream.

                    float layerScale = voxelScale * (1 << lc.layer);
                    
                    uint safeRootIndex = 8;
                    int chunkIndexInLayer = idx % chunksPerLayer;
                    if (lc.layer == 0) {
                        safeRootIndex = (uint)(chunkIndexInLayer * 8192) + 8;
                    } else if (lc.layer == 1) {
                        safeRootIndex = (uint)(chunksPerLayer * 8192) + (uint)(chunkIndexInLayer * 2048) + 8;
                    } else if (lc.layer == 2) {
                        safeRootIndex = (uint)(chunksPerLayer * 8192) + (uint)(chunksPerLayer * 2048) + (uint)(chunkIndexInLayer * 256) + 8;
                    }

                    int siloCap = (lc.layer == 0) ? 8192 : (lc.layer == 1) ? 2048 : 256;
                    int targetLod = (lc.layer == 0) ? 1 : (lc.layer == 1) ? 2 : 4; 

                    jobDataArray[dispatchesThisFrame] = new ChunkJobData {
                        worldPos = new Vector4(lc.coord.x * chunkSize, lc.coord.y * chunkSize, lc.coord.z * chunkSize, siloCap),
                        mapIndex = idx,
                        lodStep = targetLod,
                        layerScale = layerScale,
                        pad1 = safeRootIndex 
                    };

                    ChunkData cd = chunkMapArray[idx];
                    // THE FIX: Invalidate the position. WorldGen will write the true position only AFTER generation is complete.
                    cd.position = new Vector4(-99999f, -99999f, -99999f, 0f);
                    cd.rootNodeIndex = safeRootIndex; 
                    chunkMapArray[idx] = cd;

                    dispatchesThisFrame++;
                    jobsAvailable = true;
                    
                    break; 
                }
            }
        }

        if (dispatchesThisFrame > 0) {
            jobQueueBuffer.SetData(jobDataArray, 0, 0, dispatchesThisFrame);
            int kernel = worldGenShader.FindKernel("GenerateChunk");
            worldGenShader.SetBuffer(kernel, "_SVOPool", svoPoolBuffer);
            worldGenShader.SetBuffer(kernel, "_ChunkMap", chunkMapBuffer);
            worldGenShader.SetBuffer(kernel, "_JobQueue", jobQueueBuffer); 
            worldGenShader.SetInt("_JobCount", dispatchesThisFrame);
            worldGenShader.Dispatch(kernel, Mathf.CeilToInt(dispatchesThisFrame / 32f), 1, 1);

            
            // --- PHASE 3: THE STUTTER-FIX DISPATCH ---
            if (voxelLightingShader != null && dispatchesThisFrame > 0) {
                int lightKernel = voxelLightingShader.FindKernel("PropagateSunlight");
                int spreadKernel = voxelLightingShader.FindKernel("SpreadLight");
                
                // Only process the single most important chunk this frame
                uint rootIdx = (uint)jobDataArray[0].pad1; 
                voxelLightingShader.SetInt("_RootIndex", (int)rootIdx);

                voxelLightingShader.SetBuffer(lightKernel, "_SVOPool", svoPoolBuffer);
                voxelLightingShader.Dispatch(lightKernel, 1, 1, 1); 

                // THE FIX: Use floating point division to avoid integer truncation errors on M1
                int layer = Mathf.FloorToInt((float)rootIdx / (chunksPerLayer * 8192f));
                if (layer == 0) {
                    voxelLightingShader.SetBuffer(spreadKernel, "_SVOPool", svoPoolBuffer);
                    voxelLightingShader.Dispatch(spreadKernel, 4, 4, 4); 
                }
            }
        }
        jobDataArray.Dispose();
        // Only pull back data when the world actually changes
        // Only pull back data when the world actually changes
        // Only pull back data when the world actually changes
        // if (dispatchesThisFrame > 0) {
        //     // CRITICAL FIX 2: RequestIntoNativeArray pipes data directly to RAM.
        //     // This completely eliminates the 75ms "data.CopyTo()" lag spike!
        //     AsyncGPUReadback.RequestIntoNativeArray(ref masterNodePool, svoPoolBuffer, (request) => {
        //         if (request.hasError || !Application.isPlaying || !masterNodePool.IsCreated) return;
        //         // No CopyTo needed here! The memory updates silently in the background.
        //     });
        // }
    }

    public void BindPhysicsData(ComputeShader cs, int kernel) {
        if (svoPoolBuffer == null || chunkMapBuffer == null) return;
        cs.SetBuffer(kernel, "_SVOPool", svoPoolBuffer);
        cs.SetBuffer(kernel, "_ChunkMap", chunkMapBuffer);
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetInt("_ChunkCount", chunksPerLayer);
        // if (materialBuffer != null) cs.SetBuffer(kernel, "_MaterialPalette", materialBuffer);
    }

    Vector3Int GetChunkCoord(Vector3 pos, float worldChunkSize) => 
        new Vector3Int(Mathf.FloorToInt(pos.x / worldChunkSize), Mathf.FloorToInt(pos.y / worldChunkSize), Mathf.FloorToInt(pos.z / worldChunkSize));

    void UpdateGPUBuffers() {
        Shader.SetGlobalVectorArray("_ClipmapCenters", shaderCenterChunks);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
    }

    void OnDisable() {
        // CRITICAL: Check if created before disposing to prevent secondary errors
        // if (masterNodePool.IsCreated) masterNodePool.Dispose();
        if (chunkMapArray.IsCreated) chunkMapArray.Dispose();
        if (chunkTargetCoordArray.IsCreated) chunkTargetCoordArray.Dispose(); 
        
        // Release GPU buffers
        svoPoolBuffer?.Release(); 
        chunkMapBuffer?.Release(); 
        jobQueueBuffer?.Release();
        materialBuffer?.Release(); // Added this to clear the palette leak
        
        svoPoolBuffer = null;
        chunkMapBuffer = null;
        jobQueueBuffer = null;
        materialBuffer = null;
    }

    void OnGUI() {
        GUI.Label(new Rect(20, 20, 200, 40), $"FPS: {1.0f / deltaTime:0.}", new GUIStyle { fontSize = 30, normal = { textColor = Color.yellow } });
    }

    // public bool IsSolid(Vector3Int gridPos) {
    //     if (gridPos.y < -10) return true;

    //     // 1. Convert global grid to Chunk + Local coordinates
    //     Vector3Int chunkCoord = new Vector3Int(
    //         Mathf.FloorToInt(gridPos.x / 32f),
    //         Mathf.FloorToInt(gridPos.y / 32f),
    //         Mathf.FloorToInt(gridPos.z / 32f)
    //     );

    //     int idx = GetMapIndex(0, chunkCoord);
    //     if (idx < 0 || idx >= totalMapCapacity) return false;

    //     // --- CRITICAL FIX: THE BLINDSPOT VERIFICATION ---
    //     // If the coordinate registered in this slot does not perfectly match
    //     // the coordinate we are asking for, the GPU data is stale. Return false.
    //     if (chunkTargetCoordArray[idx] != chunkCoord) return false;

    //     uint rootIndex = chunkMapArray[idx].rootNodeIndex;
    //     if (rootIndex == 0xFFFFFFFF) return false;

    //     // 2. Traverse the SVO on the CPU
    //     uint nodeIndex = rootIndex;
    //     int size = 32;
    //     Vector3Int localPos = new Vector3Int(
    //         ((gridPos.x % 32) + 32) % 32,
    //         ((gridPos.y % 32) + 32) % 32,
    //         ((gridPos.z % 32) + 32) % 32
    //     );

    //     for (int i = 0; i < 6; i++) {
    //         SVONode node = masterNodePool[(int)(nodeIndex & 134217727)];
    //         if (node.childIndex == 0) return (node.material & 0xFF) != 0;
            
    //         size >>= 1;
    //         int cx = (localPos.x >= size) ? 1 : 0;
    //         int cy = (localPos.y >= size) ? 1 : 0;
    //         int cz = (localPos.z >= size) ? 1 : 0;
            
    //         localPos -= new Vector3Int(cx * size, cy * size, cz * size);
    //         nodeIndex = node.childIndex + (uint)(cx + cy * 2 + cz * 4);
    //     }
    //     return false;
    // }
}