using Unity.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine;
using VoxelEngine.Interfaces;
using System.Runtime.InteropServices;

[ExecuteAlways]

public class ChunkManager : MonoBehaviour, IVoxelWorld
{
    [Header("Clipmap Architecture")]
    public int chunkSize = 32;
    public float voxelScale = 0.2f;
    [Range(1, 3)] public int clipmapLayers = 3;
    public int renderDistanceXZ = 5; // 39x39x19 bounds = ~28,899 chunks (Tier 0 is ~60m)
    public int renderDistanceY = 5;

    [Header("Performance")]
    public int maxConcurrentJobs = 512; // THE FIX: 512 concurrent chunks pushes terminal velocity limits beyond bounds.
    public float gpuUpdateInterval = 0.05f;

    [Header("Physics & Entities")]
    public List<Transform> worldLoaders = new List<Transform>();
    public ComputeShader worldGenShader;
    public ComputeShader voxelLightingShader;


    [Header("World Editor State")]
    public bool isEditMode = false;
    public int editMode = 0;
    public int brushSize = 0;
    public int brushShape = 0;

    [Header("Debug & Profiling")]
    [Tooltip("Locks the Z-axis to generate a 2D cross-section of the world for testing subterranean biomes.")]
    public bool enableAntFarmSlice = false;

    // DATA STRUCTURES
    public struct SVONode { public uint childIndex; public uint material; }
    
    [StructLayout(LayoutKind.Sequential)] // FIX: Force exact byte alignment
    public struct ChunkData {
        public Vector4 position;    // Note: position.w already holds chunkMaxY from WorldGen!
        public uint rootNodeIndex;
        public int currentLOD;
        public uint packedState;    // Bit 0: isDense flag. Bits 8-15: chunkMinY (HDDA optimization).
        public uint densePoolIndex; // Pointer to the chunk's 1D array in _DenseChunkPool
    }
    
    [StructLayout(LayoutKind.Sequential)] // FIX: Force exact byte alignment
    public struct ChunkJobData {
        public Vector4 worldPos;
        public int mapIndex;
        public int lodStep; 
        public float layerScale;
        public uint pad1;          // FIX: Changed to uint to survive numbers > 16.7 Million!
        public int editStartIndex; 
        public int editCount;      
        public int pad2;           
        public int pad3;           
    }
    public struct LayerCoord {
        public int layer;
        public Vector3Int coord;
    }

    // --- DELTA MAP STRUCTURES ---
    public struct VoxelEdit {
        public int flatIndex;
        public uint material;
    }
    private ComputeBuffer deltaMapBuffer;
    private const int MAX_EDITS_PER_DISPATCH = 16384; // Safe cap for a single frame's generation batch
    
    // The Master Record of all changes in the world. 
    // Key: Global Chunk Coordinate (e.g., x:10, y:0, z:-5)
    // Value: A dictionary mapping the flatLocalIndex to the new material.
    private Dictionary<Vector3Int, Dictionary<int, uint>> worldDeltaMap = new Dictionary<Vector3Int, Dictionary<int, uint>>();
    
    

    private ChunkData[] chunkMapArray;
    private Vector3Int[] chunkTargetCoordArray;  
    
    private List<LayerCoord>[] generationQueues = new List<LayerCoord>[8];
    private Vector3Int[] scanOffsets;

    // private NativeArray<SVONode> masterNodePool;
    
    private const int POOL_SIZE = 671088512; 

    private ComputeBuffer svoPoolBuffer, chunkMapBuffer, jobQueueBuffer;
    private ComputeBuffer tempDenseBuffer; // NEW: The flat 1D array for bottom-up SVO generation 
    
    // --- DENSE POOL VARIABLES (Visual & Logic) ---
    private ComputeBuffer denseChunkPoolBuffer; // Visuals (Material, Light)
    private ComputeBuffer denseLogicPoolBuffer; // State (Health, Fluid, Block State)
    public const int MAX_DENSE_CHUNKS = 400;
    
    [HideInInspector] public ComputeBuffer crosshairBuffer;
    [HideInInspector] public int[] crosshairData = new int[8]; // MUST be 8 to prevent the NativeArray crash!
    public const int VOXELS_PER_CHUNK = 32768; // 32 * 32 * 32
    private Queue<uint> freeDenseIndices = new Queue<uint>(); // Tracks our 400 available memory slots

    private Vector3Int[] primaryAnchorChunks = new Vector3Int[8];
    private Vector4[] shaderCenterChunks = new Vector4[8]; 

    private Stopwatch totalLoadTimer = new Stopwatch();
    private float deltaTime = 0.0f;
    private int chunksPerLayer, totalMapCapacity;

    [Header("Aesthetics")]
    public VoxelMaterialPalette globalPalette;
    private ComputeBuffer materialBuffer;

    public static ChunkManager Instance; // THE FIX: Define the Instance
    
    // --- IVOXELWORLD IMPLEMENTATION ---
    public static IVoxelWorld World => Instance; 
    public float VoxelScale => voxelScale;
    public event System.Action<Vector3Int, uint> OnVoxelChanged;
    public event System.Action<Vector3Int, Vector3Int> OnAreaDestroyed;

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

    // Saves an edit to standard RAM permanently. Returns true if it was a new edit.
    private void RecordEditToDeltaMap(Vector3Int globalVoxelPos, uint newMaterial) {
        // 1. Find which chunk this voxel belongs to
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(globalVoxelPos.x / 32f),
            Mathf.FloorToInt(globalVoxelPos.y / 32f),
            Mathf.FloorToInt(globalVoxelPos.z / 32f)
        );

        // 2. Find the local coordinate inside that chunk (0 to 31)
        int localX = globalVoxelPos.x - (chunkCoord.x * 32);
        int localY = globalVoxelPos.y - (chunkCoord.y * 32);
        int localZ = globalVoxelPos.z - (chunkCoord.z * 32);

        // 3. Convert 3D local coordinate to a 1D flat index (Fastest for GPU compute)
        int flatLocalIndex = localX + (localY << 5) + (localZ << 10);

        // 4. Save to the Delta Map
        if (!worldDeltaMap.ContainsKey(chunkCoord)) {
            worldDeltaMap[chunkCoord] = new Dictionary<int, uint>();
        }
        
        worldDeltaMap[chunkCoord][flatLocalIndex] = newMaterial;
    }

    void OnDestroy() {
        if (materialBuffer != null) materialBuffer.Release();
    }
    
    void OnEnable() {
        // if (!masterNodePool.IsCreated) masterNodePool = new NativeArray<SVONode>(POOL_SIZE, Allocator.Persistent);
        Instance = this;
        
        // THE FIX: Forcefully correct the array size in case Unity loaded a stale length of 4 from the saved Scene YAML
        if (crosshairData == null || crosshairData.Length != 8) crosshairData = new int[8];
        
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        chunksPerLayer = sideXZ * sideXZ * sideY;
        totalMapCapacity = chunksPerLayer * clipmapLayers; 
        
        // Stratified Static Pool: Balanced for 60m radius on M1 8GB
        int tier0Size = chunksPerLayer * 38000; // FIX: Padded to 38,000 to fix the off-by-8 floating gap bug!
        int tier1Size = chunksPerLayer * 8192;  // FIX: High quality LOD 1
        int tier2Size = chunksPerLayer * 256;   // Far distance placeholder
        int POOL_SIZE = tier0Size + tier1Size + tier2Size;

        if (chunkMapArray == null) {
            chunkMapArray = new ChunkData[totalMapCapacity];
            for (int i = 0; i < totalMapCapacity; i++) {
                chunkMapArray[i] = new ChunkData {
                    position = new Vector4(-99999f, -99999f, -99999f, 0f),
                    rootNodeIndex = 0xFFFFFFFF
                };
            }
        }
        if (chunkTargetCoordArray == null) {
            chunkTargetCoordArray = new Vector3Int[totalMapCapacity];
            for(int i = 0; i < totalMapCapacity; i++) chunkTargetCoordArray[i] = new Vector3Int(-99999, -99999, -99999);
        }

        List<Vector3Int> offsets = new List<Vector3Int>();
        
        if (enableAntFarmSlice) {
            // TEST 2: THE ANT FARM SLICE
            // Generates a massive 2D wall slightly in front of the player
            // Y is clamped to your render distance so deep chunks don't get culled!
            int yMin = Mathf.Max(-12, -renderDistanceY);
            for (int y = yMin; y <= 3; y++) 
                for (int x = -10; x <= 10; x++) 
                    for (int z = 2; z <= 2; z++) // Pushed 2 chunks forward so you don't spawn inside it
                        offsets.Add(new Vector3Int(x, y, z));
        } else {
            // Standard Spherical Generation
            for (int y = -renderDistanceY; y <= renderDistanceY; y++)
                for (int x = -renderDistanceXZ; x <= renderDistanceXZ; x++)
                    for (int z = -renderDistanceXZ; z <= renderDistanceXZ; z++)
                        offsets.Add(new Vector3Int(x, y, z));
        }
        
        offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
        scanOffsets = offsets.ToArray();

        for(int i = 0; i < 8; i++) {
            generationQueues[i] = new List<LayerCoord>();
            primaryAnchorChunks[i] = new Vector3Int(-9999, -9999, -9999);
        }

        if (svoPoolBuffer == null) {
            svoPoolBuffer = new ComputeBuffer(POOL_SIZE, 8);
            // svoPoolBuffer.SetData(masterNodePool);
            Shader.SetGlobalBuffer("_SVOPool", svoPoolBuffer);
        }
        if (jobQueueBuffer == null) jobQueueBuffer = new ComputeBuffer(maxConcurrentJobs, 48); // FIX: 48 bytes for proper stride
        
        if (tempDenseBuffer == null) {
            // Allocate exact space for the 37,449 SVO mipmap tree nodes multiplied by max jobs
            tempDenseBuffer = new ComputeBuffer(maxConcurrentJobs * 37449, sizeof(uint));
            Shader.SetGlobalBuffer("_TempDenseBuffer", tempDenseBuffer);
        }
        
        if (tempDenseBuffer == null) {
            // Allocate exact space for the 37,449 SVO mipmap tree nodes multiplied by max jobs
            tempDenseBuffer = new ComputeBuffer(maxConcurrentJobs * 37449, sizeof(uint));
            Shader.SetGlobalBuffer("_TempDenseBuffer", tempDenseBuffer);
        }

        if (deltaMapBuffer == null) {
            deltaMapBuffer = new ComputeBuffer(MAX_EDITS_PER_DISPATCH, 8); // 8 bytes per edit
            // FIX: Upload empty data immediately. Metal treats uninitialized buffers as fatal null pointers!
            deltaMapBuffer.SetData(new VoxelEdit[MAX_EDITS_PER_DISPATCH]); 
        }

        // THE FIX: Forcefully destroy the old buffer if it survived a script recompile!
        if (crosshairBuffer != null && crosshairBuffer.count != 2) {
            crosshairBuffer.Release();
            crosshairBuffer = null;
        }
        if (crosshairBuffer == null) crosshairBuffer = new ComputeBuffer(2, 16); // 8 ints total

        if (denseChunkPoolBuffer == null) {
            // Allocates exactly 52.4 MB of unified memory for visual data
            denseChunkPoolBuffer = new ComputeBuffer(MAX_DENSE_CHUNKS * VOXELS_PER_CHUNK, sizeof(uint));
            Shader.SetGlobalBuffer("_DenseChunkPool", denseChunkPoolBuffer);

            // Allocates exactly 52.4 MB of unified memory for logic data (JIT Wake-up shadow pool)
            denseLogicPoolBuffer = new ComputeBuffer(MAX_DENSE_CHUNKS * VOXELS_PER_CHUNK, sizeof(uint));
            Shader.SetGlobalBuffer("_DenseLogicPool", denseLogicPoolBuffer);
            
            // Fill our allocator queue with all 400 available memory slots
            freeDenseIndices.Clear();
            for (uint i = 0; i < MAX_DENSE_CHUNKS; i++) freeDenseIndices.Enqueue(i);
        }

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

        // Safely pull the center pixel target from the GPU without stalling the pipeline
        if (crosshairBuffer != null) {
            AsyncGPUReadback.Request(crosshairBuffer, (req) => {
                if (req.hasError || !Application.isPlaying) return;
                req.GetData<int>().CopyTo(crosshairData);
            });
        }

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
                generationQueues[layer].Add(new LayerCoord { layer = layer, coord = coord });
            }
        }

        // ONE-TIME O(N log N) PRE-SORT: Sort descending so the closest chunks are at the END. 
        // This makes `RemoveAt(Count - 1)` an instant O(1) op in the tightly packed inner `DispatchJobs` loop.
        generationQueues[layer].Sort((a, b) => {
            float distA = Vector3.SqrMagnitude(a.coord - center);
            float distB = Vector3.SqrMagnitude(b.coord - center);
            return distB.CompareTo(distA);
        });
    }

    void DispatchJobs() {
        int dispatchesThisFrame = 0;
        
        // THE FIX: Dynamic Time-Slicing for the CPU Queue!
        // We give the CPU exactly 2.0 milliseconds to dig through the queue.
        // It will instantly bypass thousands of "Ghost Chunks" without ever stuttering,
        // guaranteeing the GPU pipeline stays fully fed at high speeds.
        Stopwatch queueTimer = Stopwatch.StartNew(); 
        
        NativeArray<ChunkJobData> jobDataArray = new NativeArray<ChunkJobData>(maxConcurrentJobs, Allocator.Temp);
        
        // NEW: Prepare the flat delta map array for the GPU
        NativeArray<VoxelEdit> editDataArray = new NativeArray<VoxelEdit>(MAX_EDITS_PER_DISPATCH, Allocator.Temp);
        int totalEditsThisFrame = 0;

        bool jobsAvailable = true;
        
        // Loop until we hit our GPU limit OR our CPU time limit (4.0ms)
        while (dispatchesThisFrame < maxConcurrentJobs && jobsAvailable && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
            jobsAvailable = false;
            
            for (int L = 0; L < clipmapLayers; L++) {
                if (dispatchesThisFrame >= maxConcurrentJobs) break;
                
                while (generationQueues[L].Count > 0 && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
                    
                    // O(1) Pop from end (the array is ALREADY PRE-SORTED descending upstream!)
                    int lastQueueElement = generationQueues[L].Count - 1;
                    LayerCoord lc = generationQueues[L][lastQueueElement];
                    generationQueues[L].RemoveAt(lastQueueElement);
                    
                    int idx = GetMapIndex(lc.layer, lc.coord);
                    
                    if (chunkTargetCoordArray[idx] != lc.coord) continue;

                    // The buggy distance check here was removed. Culling is now safely handled upstream.

                    float layerScale = voxelScale * (1 << lc.layer);
                    
                    uint safeRootIndex = 8;
                    int chunkIndexInLayer = idx % chunksPerLayer;
                    if (lc.layer == 0) {
                        safeRootIndex = (uint)(chunkIndexInLayer * 38000) + 8;
                    } else if (lc.layer == 1) {
                        safeRootIndex = (uint)(chunksPerLayer * 38000) + (uint)(chunkIndexInLayer * 8192) + 8;
                    } else if (lc.layer == 2) {
                        safeRootIndex = (uint)(chunksPerLayer * 38000) + (uint)(chunksPerLayer * 8192) + (uint)(chunkIndexInLayer * 256) + 8;
                    }

                    int siloCap = (lc.layer == 0) ? 38000 : (lc.layer == 1) ? 8192 : 256;
                    int targetLod = (lc.layer == 0) ? 1 : (lc.layer == 1) ? 2 : 4; 

                    int startIndex = totalEditsThisFrame;
                    int editCount = 0;
                    
                    // Check if this chunk has any recorded edits in the Delta Map
                    if (worldDeltaMap.TryGetValue(lc.coord, out var chunkEdits)) {
                        foreach (var edit in chunkEdits) {
                            if (totalEditsThisFrame >= MAX_EDITS_PER_DISPATCH) break; // Safety cap
                            editDataArray[totalEditsThisFrame] = new VoxelEdit { flatIndex = edit.Key, material = edit.Value };
                            totalEditsThisFrame++;
                            editCount++;
                        }
                    }

                    jobDataArray[dispatchesThisFrame] = new ChunkJobData {
                        worldPos = new Vector4(lc.coord.x * chunkSize, lc.coord.y * chunkSize, lc.coord.z * chunkSize, siloCap),
                        mapIndex = idx,
                        lodStep = targetLod,
                        layerScale = layerScale,
                        pad1 = safeRootIndex,
                        editStartIndex = startIndex,
                        editCount = editCount,
                        pad2 = 0,
                        pad3 = 0
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
            
            // FIX: Never upload a 0-length array to Metal. This caused the 1 FPS stutter!
            if (totalEditsThisFrame > 0) {
                deltaMapBuffer.SetData(editDataArray, 0, 0, totalEditsThisFrame); 
            }

            int kEval = worldGenShader.FindKernel("EvaluateVoxels");
            int kReduce1 = worldGenShader.FindKernel("ReduceL1");
            int kReduce2 = worldGenShader.FindKernel("ReduceL2");
            int kReduce3 = worldGenShader.FindKernel("ReduceL3");
            int kReduce4 = worldGenShader.FindKernel("ReduceL4");
            int kReduce5 = worldGenShader.FindKernel("ReduceL5");
            int kConstruct = worldGenShader.FindKernel("ConstructSVO");

            worldGenShader.SetBuffer(kEval, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kEval, "_JobQueue", jobQueueBuffer); 
            worldGenShader.SetBuffer(kEval, "_DeltaMapBuffer", deltaMapBuffer);

            worldGenShader.SetBuffer(kReduce1, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce2, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce3, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce4, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce5, "_TempDenseBuffer", tempDenseBuffer);

            worldGenShader.SetBuffer(kConstruct, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kConstruct, "_SVOPool", svoPoolBuffer);
            worldGenShader.SetBuffer(kConstruct, "_ChunkMap", chunkMapBuffer);
            worldGenShader.SetBuffer(kConstruct, "_JobQueue", jobQueueBuffer);
            
            worldGenShader.SetInt("_JobCount", dispatchesThisFrame);

            // TEST 3: THE ALU PROFILER
            Stopwatch aluTimer = Stopwatch.StartNew();
            
            // Step 1: Evaluate 32,768 voxels flawlessly in parallel
            worldGenShader.Dispatch(kEval, dispatchesThisFrame * 4, 4, 4);

            // Step 2: Parallel Reduction Mipmapping (Bottom-Up)
            worldGenShader.Dispatch(kReduce1, dispatchesThisFrame * 2, 2, 2);
            worldGenShader.Dispatch(kReduce2, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce3, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce4, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce5, dispatchesThisFrame, 1, 1);

            // Step 3: Construct the final SVO into global memory using the mapped reduction tree
            worldGenShader.Dispatch(kConstruct, dispatchesThisFrame, 1, 1);
            
            aluTimer.Stop();
            
            // FIX: Silenced to prevent massive Garbage Collection CPU spikes!
            // if (aluTimer.Elapsed.TotalMilliseconds > 6.0) {
            //      UnityEngine.Debug.LogWarning($"[Profiler] GPU WorldGen Spike: {dispatchesThisFrame} chunks took {aluTimer.Elapsed.TotalMilliseconds:F2} ms");
            // } else {
            //      UnityEngine.Debug.Log($"[Profiler] GPU WorldGen: {dispatchesThisFrame} chunks in {aluTimer.Elapsed.TotalMilliseconds:F2} ms");
            // }

            // --- PHASE 3: THE STUTTER-FIX DISPATCH ---
            if (voxelLightingShader != null && dispatchesThisFrame > 0) {
                int lightKernel = voxelLightingShader.FindKernel("PropagateSunlight");
                int spreadKernel = voxelLightingShader.FindKernel("SpreadLight");
                
                // Only process the single most important chunk this frame
                uint rootIdx = (uint)jobDataArray[0].pad1; 
                voxelLightingShader.SetInt("_RootIndex", (int)rootIdx);

                voxelLightingShader.SetBuffer(lightKernel, "_SVOPool", svoPoolBuffer);
                voxelLightingShader.Dispatch(lightKernel, 1, 1, 1); 

                // THE FIX: Sync the lighting shader math with the new 38,000 padded LOD 0 cap
                int layer = Mathf.FloorToInt((float)rootIdx / (chunksPerLayer * 38000f));
                if (layer == 0) {
                    voxelLightingShader.SetBuffer(spreadKernel, "_SVOPool", svoPoolBuffer);
                    voxelLightingShader.Dispatch(spreadKernel, 4, 4, 4); 
                    
                    int aoKernel = voxelLightingShader.FindKernel("BakeAO");
                    voxelLightingShader.SetBuffer(aoKernel, "_SVOPool", svoPoolBuffer);
                    voxelLightingShader.SetInt("_RootIndex", (int)rootIdx);
                    voxelLightingShader.Dispatch(aoKernel, 4, 4, 4);
                }
            }
        }
        jobDataArray.Dispose();
        editDataArray.Dispose();
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
        
        // Only bind crosshair/edit data to the RayTracer, not the Physics Scanner
        if (cs.name == "RayTracer" && crosshairBuffer != null) {
            cs.SetBuffer(kernel, "_CrosshairTarget", crosshairBuffer);
            cs.SetVector("_TargetBlock", new Vector4(crosshairData[0], crosshairData[1], crosshairData[2], crosshairData[3]));
            cs.SetVector("_TargetNormal", new Vector4(crosshairData[4], crosshairData[5], crosshairData[6], 0));
            cs.SetInt("_IsEditMode", isEditMode ? 1 : 0);
            cs.SetInt("_EditMode", editMode);
            cs.SetInt("_BrushSize", brushSize);
            cs.SetInt("_BrushShape", brushShape);
            cs.SetFloat("_VoxelScale", voxelScale);
        }
        
        // --- THE CRITICAL FIX: PLUG IN THE DENSE POOLS ---
        if (denseChunkPoolBuffer != null) cs.SetBuffer(kernel, "_DenseChunkPool", denseChunkPoolBuffer);
        if (denseLogicPoolBuffer != null) cs.SetBuffer(kernel, "_DenseLogicPool", denseLogicPoolBuffer);
        
        cs.SetBuffer(kernel, "_SVOPool", svoPoolBuffer);
        cs.SetBuffer(kernel, "_ChunkMap", chunkMapBuffer);
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetInt("_ChunkCount", chunksPerLayer);
    }

    Vector3Int GetChunkCoord(Vector3 pos, float worldChunkSize) => 
        new Vector3Int(Mathf.FloorToInt(pos.x / worldChunkSize), Mathf.FloorToInt(pos.y / worldChunkSize), Mathf.FloorToInt(pos.z / worldChunkSize));

    void UpdateGPUBuffers() {
        Shader.SetGlobalVectorArray("_ClipmapCenters", shaderCenterChunks);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
    }

   // --- PHASE 2: INTERACTION & DECOMPRESSION ---
    public void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0) {
        // 0. Record to Delta Map (Memory)
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    if (dist <= brushSize + 0.5f) {
                        RecordEditToDeltaMap(targetPos, newMaterial);
                    }
                }
            }
        }

        // 1. Calculate the global bounding box of the brush
        Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);

        // 2. Convert to chunk coordinates to see how many chunks are affected
        Vector3Int minChunk = new Vector3Int(
            Mathf.FloorToInt(minGlobal.x / 32f),
            Mathf.FloorToInt(minGlobal.y / 32f),
            Mathf.FloorToInt(minGlobal.z / 32f)
        );
        Vector3Int maxChunk = new Vector3Int(
            Mathf.FloorToInt(maxGlobal.x / 32f),
            Mathf.FloorToInt(maxGlobal.y / 32f),
            Mathf.FloorToInt(maxGlobal.z / 32f)
        );

        // 3. Loop through every chunk touched by the brush
        for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
            for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
                for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
                    
                    Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
                    int idx = GetMapIndex(0, chunkCoord);
                    
                    // Boundary safety
                    if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;

                    ChunkData[] gpuData = new ChunkData[1];
                    chunkMapBuffer.GetData(gpuData, 0, idx, 1);
                    ChunkData cd = gpuData[0];

                    uint denseIndex = 0;
                    bool isDense = (cd.packedState & 1) == 1;

                    // Decompress SVO to Dense Array if it hasn't been already
                    if (isDense) {
                        denseIndex = cd.densePoolIndex;
                    } else {
                        if (freeDenseIndices.Count == 0) continue; // Memory safety limit
                        denseIndex = freeDenseIndices.Dequeue();
                        
                        int decompressKernel = worldGenShader.FindKernel("DecompressSVOToDense");
                        worldGenShader.SetBuffer(decompressKernel, "_SVOPool", svoPoolBuffer);
                        worldGenShader.SetBuffer(decompressKernel, "_DenseChunkPool", denseChunkPoolBuffer);
                        worldGenShader.SetBuffer(decompressKernel, "_DenseLogicPool", denseLogicPoolBuffer); // FIX: Ensure logic state is initialized
                        worldGenShader.SetInt("_TargetRootIndex", (int)cd.rootNodeIndex);
                        worldGenShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                        worldGenShader.SetInt("_PoolSize", svoPoolBuffer.count); 
                        worldGenShader.Dispatch(decompressKernel, 4, 4, 4);

                        cd.packedState |= 1; 
                        cd.densePoolIndex = denseIndex;
                        gpuData[0] = cd;
                        chunkMapBuffer.SetData(gpuData, 0, idx, 1);
                    }

                    // Calculate the local edit position RELATIVE TO THIS SPECIFIC CHUNK
                    int localX = globalVoxelPos.x - (cx * 32);
                    int localY = globalVoxelPos.y - (cy * 32);
                    int localZ = globalVoxelPos.z - (cz * 32);

                    int editKernel = worldGenShader.FindKernel("EditDenseVoxel");
                    worldGenShader.SetBuffer(editKernel, "_DenseChunkPool", denseChunkPoolBuffer);
                    worldGenShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                    worldGenShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
                    worldGenShader.SetInt("_NewMaterialData", (int)newMaterial);
                    worldGenShader.SetInt("_BrushSize", brushSize);
                    worldGenShader.SetInt("_BrushShape", brushShape);
                    
                    worldGenShader.Dispatch(editKernel, 1, 1, 1);

                    // Broadcast that a block changed. If brushSize > 0, we don't spam the event for every block.
                    if (brushSize == 0) {
                        OnVoxelChanged?.Invoke(globalVoxelPos, newMaterial);
                    }
                }
            }
        }
        
        // Notify the Physics Manager that the terrain changed so it instantly updates colliders
        if (VoxelPhysicsManager.Instance != null) {
            VoxelPhysicsManager.Instance.forceRebuild = true;
        }
    }

    public void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0) {
        // 0. Record destruction to Delta Map (Memory)
        for (int x = -brushSize; x <= brushSize; x++) {
            for (int y = -brushSize; y <= brushSize; y++) {
                for (int z = -brushSize; z <= brushSize; z++) {
                    Vector3Int targetPos = globalVoxelPos + new Vector3Int(x, y, z);
                    float dist = brushShape == 0 ? 
                        Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) : new Vector3(x, y, z).magnitude;
                    if (dist <= brushSize + 0.5f) {
                        RecordEditToDeltaMap(targetPos, 0); // 0 = Air/Destroyed
                    }
                }
            }
        }

        // 1. Calculate the global bounding box of the damage area
        Vector3Int minGlobal = globalVoxelPos - new Vector3Int(brushSize, brushSize, brushSize);
        Vector3Int maxGlobal = globalVoxelPos + new Vector3Int(brushSize, brushSize, brushSize);

        // Broadcast the explosion zone to any listening logic modules
        if (brushSize > 0) {
            OnAreaDestroyed?.Invoke(minGlobal, maxGlobal);
        }

        // 2. Convert to chunk coordinates to see how many chunks are affected
        Vector3Int minChunk = new Vector3Int(
            Mathf.FloorToInt(minGlobal.x / 32f),
            Mathf.FloorToInt(minGlobal.y / 32f),
            Mathf.FloorToInt(minGlobal.z / 32f)
        );
        Vector3Int maxChunk = new Vector3Int(
            Mathf.FloorToInt(maxGlobal.x / 32f),
            Mathf.FloorToInt(maxGlobal.y / 32f),
            Mathf.FloorToInt(maxGlobal.z / 32f)
        );

        // 3. Loop through every chunk touched by the damage radius
        for (int cy = minChunk.y; cy <= maxChunk.y; cy++) {
            for (int cx = minChunk.x; cx <= maxChunk.x; cx++) {
                for (int cz = minChunk.z; cz <= maxChunk.z; cz++) {
                    
                    Vector3Int chunkCoord = new Vector3Int(cx, cy, cz);
                    int idx = GetMapIndex(0, chunkCoord);
                    
                    if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) continue;

                    ChunkData[] gpuData = new ChunkData[1];
                    chunkMapBuffer.GetData(gpuData, 0, idx, 1);
                    ChunkData cd = gpuData[0];

                    uint denseIndex = 0;
                    bool isDense = (cd.packedState & 1) == 1;

                    // JIT Wake-up logic
                    if (isDense) {
                        denseIndex = cd.densePoolIndex;
                    } else {
                        if (freeDenseIndices.Count == 0) continue;
                        denseIndex = freeDenseIndices.Dequeue();
                        
                        int decompressKernel = worldGenShader.FindKernel("DecompressSVOToDense");
                        worldGenShader.SetBuffer(decompressKernel, "_SVOPool", svoPoolBuffer);
                        worldGenShader.SetBuffer(decompressKernel, "_DenseChunkPool", denseChunkPoolBuffer);
                        worldGenShader.SetBuffer(decompressKernel, "_DenseLogicPool", denseLogicPoolBuffer);
                        worldGenShader.SetInt("_TargetRootIndex", (int)cd.rootNodeIndex);
                        worldGenShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                        worldGenShader.SetInt("_PoolSize", svoPoolBuffer.count); 
                        worldGenShader.Dispatch(decompressKernel, 4, 4, 4);

                        cd.packedState |= 1; 
                        cd.densePoolIndex = denseIndex;
                        gpuData[0] = cd;
                        chunkMapBuffer.SetData(gpuData, 0, idx, 1);
                    }

                    int localX = globalVoxelPos.x - (cx * 32);
                    int localY = globalVoxelPos.y - (cy * 32);
                    int localZ = globalVoxelPos.z - (cz * 32);

                    int damageKernel = worldGenShader.FindKernel("DamageDenseVoxel");
                    worldGenShader.SetBuffer(damageKernel, "_DenseChunkPool", denseChunkPoolBuffer);
                    worldGenShader.SetBuffer(damageKernel, "_DenseLogicPool", denseLogicPoolBuffer);
                    worldGenShader.SetInt("_TargetDenseIndex", (int)denseIndex);
                    worldGenShader.SetInts("_EditLocalPos", new int[] { localX, localY, localZ }); 
                    worldGenShader.SetInt("_DamageAmount", damageAmount);
                    worldGenShader.SetInt("_BrushSize", brushSize);
                    worldGenShader.SetInt("_BrushShape", brushShape);
                    
                    worldGenShader.Dispatch(damageKernel, 1, 1, 1);
                }
            }
        }
        
        if (VoxelPhysicsManager.Instance != null) {
            VoxelPhysicsManager.Instance.forceRebuild = true;
        }
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
        tempDenseBuffer?.Release();
        tempDenseBuffer?.Release(); // NEW
        deltaMapBuffer?.Release();
        materialBuffer?.Release();
        denseChunkPoolBuffer?.Release(); 
        denseLogicPoolBuffer?.Release();
        crosshairBuffer?.Release();
        
        svoPoolBuffer = null;
        chunkMapBuffer = null;
        jobQueueBuffer = null;
        tempDenseBuffer = null;
        tempDenseBuffer = null;
        materialBuffer = null;
        denseChunkPoolBuffer = null;
        denseLogicPoolBuffer = null;
        deltaMapBuffer = null;  // FIX: Prevent zombie buffer crash
        crosshairBuffer = null; // FIX: Prevent zombie buffer crash
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