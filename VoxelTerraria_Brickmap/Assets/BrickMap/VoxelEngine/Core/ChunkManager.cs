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

public class ChunkManager : MonoBehaviour, IVoxelWorld
{

    public enum VoxelArchitecture { BrickMap32Bit, DualState1Bit }
    public VoxelArchitecture currentArchitecture = VoxelArchitecture.DualState1Bit;

    [Header("Clipmap Architecture")]
    public int chunkSize = 32;
    public float voxelScale = 0.2f;
    [Range(1, 4)] public int clipmapLayers = 3;
    public int renderDistanceXZ = 5; 
    public int renderDistanceY = 5;

    public int maxConcurrentJobs = 12; 
    public float gpuUpdateInterval = 0.05f;

    [Header("Physics & Entities")]
    public List<Transform> worldLoaders = new List<Transform>();
    public ComputeShader worldGenShader;
    public ComputeShader worldGenUtilityShader;
    public ComputeShader voxelLightingShader;


    [Header("World Editor State")]
    public bool isEditMode = false;
    public int editMode = 0;
    public int brushSize = 0;
    public int brushShape = 0;

    [Header("Debug & Profiling")]
    public bool enableAntFarmSlice = false;

    // DATA STRUCTURES
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkData {
        public Vector4 position;
        public uint rootNodeIndex;
        public int currentLOD;
        public uint packedState;
        public uint densePoolIndex;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkJobData {
        public Vector4 worldPos;
        public int mapIndex;
        public int lodStep; 
        public float layerScale;
        public uint pad1;
        public int editStartIndex; 
        public int editCount;      
        public int pad2;           
        public int pad3;           
    }
    public struct LayerCoord {
        public int layer;
        public Vector3Int coord;
    }

    public struct VoxelEdit {
        public int flatIndex;
        public uint material;
    }

    private class ChunkPriorityComparer : IComparer<LayerCoord> {
        public Vector3 pPos;
        public Vector3 travelDir;
        public float scaleMult;
        public int Compare(LayerCoord a, LayerCoord b) {
            Vector3 posA = new Vector3(a.coord.x, a.coord.y, a.coord.z) * scaleMult;
            Vector3 posB = new Vector3(b.coord.x, b.coord.y, b.coord.z) * scaleMult;
            float dotA = Vector3.Dot((posA - pPos).normalized, travelDir);
            float dotB = Vector3.Dot((posB - pPos).normalized, travelDir);
            float scoreA = Vector3.Distance(pPos, posA) - (dotA * 150f);
            float scoreB = Vector3.Distance(pPos, posB) - (dotB * 150f);
            return scoreB.CompareTo(scoreA); 
        }
    }
    private ChunkPriorityComparer chunkSorter = new ChunkPriorityComparer();

    private ComputeBuffer deltaMapBuffer;
    private const int MAX_EDITS_PER_DISPATCH = 16384; 
    private Dictionary<Vector3Int, Dictionary<int, uint>> worldDeltaMap = new Dictionary<Vector3Int, Dictionary<int, uint>>();
    
    
    public ChunkData[] chunkMapArray;
    public Vector3Int[] chunkTargetCoordArray;
    private List<LayerCoord>[] generationQueues = new List<LayerCoord>[8];
    private Vector3Int[] scanOffsets;

    private ComputeBuffer[] chunkMapBuffers = new ComputeBuffer[2]; // FIX: Ring-buffered map!
    private ComputeBuffer macroGridBuffer;

    private int kernel_commit;
    
    // --- THE RING BUFFERS ---
    private ComputeBuffer[] tempChunkUploadBuffers = new ComputeBuffer[2];
    private ComputeBuffer[] jobQueueBuffers = new ComputeBuffer[2];
    private ComputeBuffer[] macroMaskPoolBuffers = new ComputeBuffer[2];
    private int ringIndex = 0;

    // --- NEW: Pre-allocated Managed Arrays (Anti-GC) ---
    private uint[] preallocatedChunkUpload;
    private ComputeBuffer denseChunkPoolBuffer;
    
    // NEW: CPU Backing Arrays for Burst Jobs
    // FIX: Safely pushed back to 14k because we deleted the Logic pools!
    public const int MAX_DENSE_CHUNKS = 14000; 
    public NativeArray<uint> cpuDenseChunkPool;
    public NativeArray<uint> cpuMacroMaskPool; 

    // NEW: Async Job Tracking
    private Unity.Jobs.JobHandle activeTerrainJobHandle;
    private bool isTerrainJobRunning = false;
    
    // --- TRUE ZERO ALLOCATION PERSISTENT ARRAYS ---
    private NativeArray<ChunkJobData> persistentJobDataArray;
    private NativeArray<VoxelEngine.World.FeatureAnchor> persistentFeatureArray;
    private NativeArray<VoxelEngine.World.CavernNode> persistentCavernArray;
    private NativeArray<VoxelEngine.World.TunnelSpline> persistentTunnelArray; 
    private int activeDispatches = 0;
    
    [HideInInspector] public ComputeBuffer crosshairBuffer;
    [HideInInspector] public int[] crosshairData = new int[8];
    // Dynamically chooses 1024 ints (1-Bit) or 32768 ints (32-Bit)
    public int UINTS_PER_CHUNK => currentArchitecture == VoxelArchitecture.DualState1Bit ? 1024 : 32768;
    private Queue<uint> freeDenseIndices = new Queue<uint>(); 

    // --- ZERO-ALLOCATION CACHES ---
    private System.Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> crosshairCallback;
    private ChunkJobData[] pendingJobsArray;

    private Vector3Int[] primaryAnchorChunks = new Vector3Int[8];
    private Vector4[] shaderCenterChunks = new Vector4[8]; 

    private Stopwatch totalLoadTimer = new Stopwatch();
    private float deltaTime = 0.0f;
    private int chunksPerLayer, totalMapCapacity;

    [Header("Aesthetics")]
    public VoxelMaterialPalette globalPalette;
    private ComputeBuffer materialBuffer;

    public static ChunkManager Instance;
    public static IVoxelWorld World => Instance; 
    public float VoxelScale => voxelScale;
    public event System.Action<Vector3Int, uint> OnVoxelChanged;
    public event System.Action<Vector3Int, Vector3Int> OnAreaDestroyed;

    private int kernel_generate;
    int kernel_edit;
    int kernel_damage;
    int kernel_clear;

    void Awake() {
        Instance = this;
        kernel_generate = worldGenShader.FindKernel("GenerateChunk");
        kernel_commit = worldGenUtilityShader.FindKernel("CommitUploadBuffers");
        
        if (worldGenUtilityShader != null) {
            kernel_clear = worldGenUtilityShader.FindKernel("ClearJobState");
            kernel_edit = worldGenUtilityShader.FindKernel("EditDenseVoxel");
            kernel_damage = worldGenUtilityShader.FindKernel("DamageDenseVoxel");
        }
    }

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

    void OnEnable() {
        Instance = this;
        // Toggle the Shader Architecture!
        if (currentArchitecture == VoxelArchitecture.DualState1Bit) {
            Shader.EnableKeyword("DUAL_STATE_1BIT");
        } else {
            Shader.DisableKeyword("DUAL_STATE_1BIT");
        }
        if (crosshairData == null || crosshairData.Length != 8) crosshairData = new int[8];
        
        // Initialize the zero-allocation caches
        crosshairCallback = OnCrosshairReadback;
        if (pendingJobsArray == null || pendingJobsArray.Length != maxConcurrentJobs) pendingJobsArray = new ChunkJobData[maxConcurrentJobs];
        
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        chunksPerLayer = sideXZ * sideXZ * sideY;
        totalMapCapacity = chunksPerLayer * clipmapLayers; 
        
        // Stratified Static Pool: Balanced for 60m radius on M1 8GB
        int tier0Size = chunksPerLayer * 38000; // FIX: Padded to 38,000 to fix the off-by-8 floating gap bug!
        int tier1Size = chunksPerLayer * 8192;  // FIX: High quality LOD 1
        int tier2Size = chunksPerLayer * 256;   // Far distance placeholder
        int POOL_SIZE = tier0Size + tier1Size + tier2Size;

        // THE FIX: Delete the `if (null)` checks. Force Unity to build fresh, correctly sized arrays!
        chunkMapArray = new ChunkData[totalMapCapacity];
        for (int i = 0; i < totalMapCapacity; i++) {
            chunkMapArray[i] = new ChunkData {
                position = new Vector4(-99999f, -99999f, -99999f, 0f),
                rootNodeIndex = 0xFFFFFFFF,
                densePoolIndex = 0xFFFFFFFF 
            };
        }
        
        chunkTargetCoordArray = new Vector3Int[totalMapCapacity];
        for(int i = 0; i < totalMapCapacity; i++) chunkTargetCoordArray[i] = new Vector3Int(-99999, -99999, -99999);

        List<Vector3Int> offsets = new List<Vector3Int>();
        for (int y = -renderDistanceY; y <= renderDistanceY; y++) {
            for (int x = -renderDistanceXZ; x <= renderDistanceXZ; x++) {
                for (int z = -renderDistanceXZ; z <= renderDistanceXZ; z++) {
                    Vector3Int off = new Vector3Int(x, y, z);
                    float dXZ = Vector2.Distance(new Vector2(x, z), Vector2.zero);
                    if (dXZ <= renderDistanceXZ) offsets.Add(off);
                }
            }
        }
        offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
        scanOffsets = offsets.ToArray();

        for(int i = 0; i < 8; i++) {
            generationQueues[i] = new List<LayerCoord>();
            primaryAnchorChunks[i] = new Vector3Int(-9999, -9999, -9999);
        }

        // int totalVoxelCapacity = maxConcurrentJobs * VOXELS_PER_CHUNK;
        int totalVoxelCapacity = maxConcurrentJobs * UINTS_PER_CHUNK;
        if (tempChunkUploadBuffers[0] == null) {
            for (int i = 0; i < 2; i++) {
                chunkMapBuffers[i] = new ComputeBuffer(totalMapCapacity, 32);
                chunkMapBuffers[i].SetData(chunkMapArray);
                tempChunkUploadBuffers[i] = new ComputeBuffer(totalVoxelCapacity, sizeof(uint));
                jobQueueBuffers[i] = new ComputeBuffer(maxConcurrentJobs, 48);
                macroMaskPoolBuffers[i] = new ComputeBuffer(MAX_DENSE_CHUNKS * 16, sizeof(uint)); 
            }
            preallocatedChunkUpload = new uint[totalVoxelCapacity];
        }
        
        if (deltaMapBuffer == null) {
            deltaMapBuffer = new ComputeBuffer(MAX_EDITS_PER_DISPATCH, 8);
            deltaMapBuffer.SetData(new VoxelEdit[MAX_EDITS_PER_DISPATCH]); 
        }

        if (crosshairBuffer == null) crosshairBuffer = new ComputeBuffer(2, 16);

        if (denseChunkPoolBuffer == null) {
            // FIX: Removed 4 Gigabytes of Logic RAM!
            // denseChunkPoolBuffer = new ComputeBuffer(MAX_DENSE_CHUNKS * VOXELS_PER_CHUNK, sizeof(uint));
            denseChunkPoolBuffer = new ComputeBuffer(MAX_DENSE_CHUNKS * UINTS_PER_CHUNK, sizeof(uint));
            // cpuDenseChunkPool = new NativeArray<uint>(MAX_DENSE_CHUNKS * VOXELS_PER_CHUNK, Allocator.Persistent);
            cpuDenseChunkPool = new NativeArray<uint>(MAX_DENSE_CHUNKS * UINTS_PER_CHUNK, Allocator.Persistent);
            cpuMacroMaskPool = new NativeArray<uint>(MAX_DENSE_CHUNKS * 16, Allocator.Persistent); 
            
            if (!persistentJobDataArray.IsCreated) persistentJobDataArray = new NativeArray<ChunkJobData>(maxConcurrentJobs, Allocator.Persistent);
            if (!persistentFeatureArray.IsCreated) persistentFeatureArray = new NativeArray<VoxelEngine.World.FeatureAnchor>(5000, Allocator.Persistent);
            if (!persistentCavernArray.IsCreated) persistentCavernArray = new NativeArray<VoxelEngine.World.CavernNode>(5000, Allocator.Persistent);
            if (!persistentTunnelArray.IsCreated) persistentTunnelArray = new NativeArray<VoxelEngine.World.TunnelSpline>(5000, Allocator.Persistent); 

            denseChunkPoolBuffer.SetData(cpuDenseChunkPool);

            Shader.SetGlobalBuffer("_DenseChunkPool", denseChunkPoolBuffer);
            Shader.SetGlobalBuffer("_MacroMaskPool", macroMaskPoolBuffers[0]); 
            
            freeDenseIndices.Clear();
            for (uint i = 0; i < MAX_DENSE_CHUNKS; i++) freeDenseIndices.Enqueue(i);
        }

        if (macroGridBuffer == null) {
            macroGridBuffer = new ComputeBuffer(totalMapCapacity, 8);
            Shader.SetGlobalBuffer("_MacroGrid", macroGridBuffer);
        }
        Shader.SetGlobalBuffer("_ChunkMap", chunkMapBuffers[0]);

        if (globalPalette != null && materialBuffer == null) {
            materialBuffer = new ComputeBuffer(256, 32);
            materialBuffer.SetData(globalPalette.materials);
            Shader.SetGlobalBuffer("_MaterialPalette", materialBuffer);
        }
        Shader.SetGlobalInt("_PoolSize", (int)POOL_SIZE);
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalFloat("_VoxelScale", voxelScale);
    }

    
    // Zero-allocation callback for the crosshair
    void OnCrosshairReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req) {
        if (req.hasError || !Application.isPlaying) return;
        req.GetData<int>().CopyTo(crosshairData);
    }

    void CleanupAndQueue(int layer, Vector3Int center) {
        float layerScale = voxelScale * (1 << layer);
        
        int activeRadXZ = 18; 
        int activeRadY = 3;   
        
        if (layer == 1) { activeRadXZ = 12; activeRadY = 4; } 
        if (layer == 2) { activeRadXZ = 8; activeRadY = 5; }  
        
        activeRadXZ = Mathf.Min(activeRadXZ, renderDistanceXZ);
        activeRadY = Mathf.Min(activeRadY, renderDistanceY);

        int activeRadXZSq = activeRadXZ * activeRadXZ;

        for (int i = 0; i < scanOffsets.Length; i++) {
            Vector3Int coord = center + scanOffsets[i];
            
            // FIX: Zero-Allocation Distance Math! 
            // Removed Vector2() allocations to prevent hidden CPU stuttering.
            float dx = coord.x - center.x;
            float dz = coord.z - center.z;
            float distXZSq = (dx * dx) + (dz * dz);
            
            bool isInsideActiveRadius = (distXZSq <= activeRadXZSq) && (Mathf.Abs(coord.y - center.y) <= activeRadY);
            
            int idx = GetMapIndex(layer, coord);
            
            if (isInsideActiveRadius) {
                if (chunkTargetCoordArray[idx] != coord) {
                    ChunkData oldCd = chunkMapArray[idx];
                    if (oldCd.densePoolIndex != 0xFFFFFFFF) {
                        freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                        oldCd.packedState = 0; 
                        oldCd.densePoolIndex = 0xFFFFFFFF; 
                        oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                        chunkMapArray[idx] = oldCd;
                    }
                    chunkTargetCoordArray[idx] = coord; 
                    generationQueues[layer].Add(new LayerCoord { layer = layer, coord = coord });
                }
            } else {
                if (chunkTargetCoordArray[idx] == coord) {
                    ChunkData oldCd = chunkMapArray[idx];
                    if (oldCd.densePoolIndex != 0xFFFFFFFF) {
                        freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                        oldCd.packedState = 0; 
                        oldCd.densePoolIndex = 0xFFFFFFFF; 
                        oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                        chunkMapArray[idx] = oldCd;
                    }
                    chunkTargetCoordArray[idx] = new Vector3Int(-99999, -99999, -99999);
                }
            }
        }
    }

    
    
    void Update() {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f; 
        if (worldLoaders.Count == 0 || worldLoaders[0] == null) return;

        bool mapOrMaskChanged = false;

        // 1. FINISH ACTIVE JOBS
        if (isTerrainJobRunning) {
            if (!activeTerrainJobHandle.IsCompleted) return; 
            CompleteActiveJob(); 
            mapOrMaskChanged = true;
        }

        // 2. QUEUE NEW CHUNKS
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

        if (anyAnchorChanged) mapOrMaskChanged = true;

        // if (VoxelPhysicsManager.Instance != null) {
        //     VoxelPhysicsManager.Instance.SyncPhysics();
        // }

        // 3. THE TRUE GRAND SWAP (Zero-Stall GPU Upload)
        if (mapOrMaskChanged) {
            int workRing = (ringIndex + 1) % 2; // Write to the IDLE buffer!

            chunkMapBuffers[workRing].SetData(chunkMapArray);
            macroMaskPoolBuffers[workRing].SetData(cpuMacroMaskPool);

            ringIndex = workRing; // Flip the pointer!

            Shader.SetGlobalBuffer("_ChunkMap", chunkMapBuffers[ringIndex]);
            Shader.SetGlobalBuffer("_MacroMaskPool", macroMaskPoolBuffers[ringIndex]);

            if (anyAnchorChanged) {
                UpdateGPUBuffers(); 
                totalLoadTimer.Restart();
            }
        }

        // 4. DISPATCH NEW JOBS
        DispatchNewJobs(); 

        if (crosshairBuffer != null && Time.frameCount % 4 == 0) {
            UnityEngine.Rendering.AsyncGPUReadback.Request(crosshairBuffer, crosshairCallback);
        }

        bool allQueuesEmpty = true;
        for (int i = 0; i < clipmapLayers; i++) if (generationQueues[i].Count > 0) allQueuesEmpty = false;

        if (allQueuesEmpty && totalLoadTimer.IsRunning) {
            totalLoadTimer.Stop();
            UnityEngine.Debug.LogWarning($"✅ CLIPMAPS LOADED IN {totalLoadTimer.ElapsedMilliseconds} ms ✅");
        }
    }

    void CompleteActiveJob() {
        activeTerrainJobHandle.Complete();
        // int voxelCountThisDispatch = activeDispatches * VOXELS_PER_CHUNK;
        int voxelCountThisDispatch = activeDispatches * UINTS_PER_CHUNK;

        for (int i = 0; i < activeDispatches; i++) {
            int mapIndex = persistentJobDataArray[i].mapIndex;
            uint myDenseIndex = (uint)persistentJobDataArray[i].pad2;

            // THE ALIGNMENT FIX: 
            // ALWAYS copy the memory, even if abandoned, to keep the GPU upload buffers perfectly aligned!
            // int bufferOffset = (int)myDenseIndex * VOXELS_PER_CHUNK;
            int bufferOffset = (int)myDenseIndex * UINTS_PER_CHUNK;
            NativeArray<uint>.Copy(cpuDenseChunkPool, bufferOffset, preallocatedChunkUpload, i * UINTS_PER_CHUNK, UINTS_PER_CHUNK);

            // Only update the map if the player hasn't moved away from this chunk!
            if (chunkMapArray[mapIndex].densePoolIndex == myDenseIndex) {
                ChunkData cd = chunkMapArray[mapIndex];
                
                if (persistentJobDataArray[i].pad3 == 1) {
                    cd.packedState = 3u; 
                    if (cd.densePoolIndex != 0xFFFFFFFF) {
                        freeDenseIndices.Enqueue(cd.densePoolIndex);
                        cd.densePoolIndex = 0xFFFFFFFF;
                    }
                } else {
                    cd.packedState = 1u; 
                }
                
                cd.position = new Vector4(persistentJobDataArray[i].worldPos.x, persistentJobDataArray[i].worldPos.y, persistentJobDataArray[i].worldPos.z, 32.0f);
                chunkMapArray[mapIndex] = cd;
            }
        }

        // Upload to the CURRENT active ring buffer
        tempChunkUploadBuffers[ringIndex].SetData(preallocatedChunkUpload, 0, 0, voxelCountThisDispatch);
        jobQueueBuffers[ringIndex].SetData(persistentJobDataArray, 0, 0, activeDispatches);

        int commitKernel = worldGenUtilityShader.FindKernel("CommitUploadBuffers"); // Caching this to a variable in Awake() is even better!
        worldGenUtilityShader.SetInt("_JobCount", activeDispatches);
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempChunkUpload", tempChunkUploadBuffers[ringIndex]);
        worldGenUtilityShader.SetBuffer(commitKernel, "_TempLogicUpload", tempChunkUploadBuffers[ringIndex]); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_DenseChunkPool", denseChunkPoolBuffer);
        worldGenUtilityShader.SetBuffer(commitKernel, "_DenseLogicPool", denseChunkPoolBuffer); 
        worldGenUtilityShader.SetBuffer(commitKernel, "_JobQueue", jobQueueBuffers[ringIndex]); 
        
        // int threadGroups = Mathf.CeilToInt((activeDispatches * VOXELS_PER_CHUNK) / 256f);
        int threadGroups = Mathf.CeilToInt((activeDispatches * UINTS_PER_CHUNK) / 256f);
        worldGenUtilityShader.Dispatch(commitKernel, threadGroups, 1, 1);

        isTerrainJobRunning = false;
    }

    void DispatchNewJobs() {
        if (isTerrainJobRunning) return;

        if (worldLoaders.Count > 0 && worldLoaders[0] != null) {
            Rigidbody pBody = worldLoaders[0].GetComponent<Rigidbody>();
            Vector3 pPos = worldLoaders[0].position;
            Vector3 pVel = (pBody != null) ? pBody.linearVelocity : (Vector3.down * 50f); 
            Vector3 travelDir = pVel.magnitude > 1f ? pVel.normalized : Vector3.down;

            // 1. THE ZERO-GC SORTING FIX
            for (int L = 0; L < clipmapLayers; L++) {
                if (generationQueues[L].Count > 1 && Time.frameCount % 30 == 0) {
                    chunkSorter.pPos = pPos;
                    chunkSorter.travelDir = travelDir;
                    chunkSorter.scaleMult = chunkSize * (voxelScale * (1 << L));
                    generationQueues[L].Sort(chunkSorter); // No more Garbage Collection!
                }
            }
        }

        int dispatchesThisFrame = 0;
        Stopwatch queueTimer = Stopwatch.StartNew(); 
        bool jobsAvailable = true;
        
        while (dispatchesThisFrame < maxConcurrentJobs && jobsAvailable && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
            jobsAvailable = false;
            for (int L = 0; L < clipmapLayers; L++) {
                if (dispatchesThisFrame >= maxConcurrentJobs) break;
                while (generationQueues[L].Count > 0 && queueTimer.Elapsed.TotalMilliseconds < 4.0) {
                    int lastQueueElement = generationQueues[L].Count - 1;
                    LayerCoord lc = generationQueues[L][lastQueueElement];
                    
                    int idx = GetMapIndex(lc.layer, lc.coord);
                    if (chunkTargetCoordArray[idx] != lc.coord) {
                        generationQueues[L].RemoveAt(lastQueueElement); 
                        continue;
                    }

                    if (freeDenseIndices.Count == 0) {
                        jobsAvailable = false;
                        break; 
                    }
                    
                    generationQueues[L].RemoveAt(lastQueueElement);
                    uint denseIndex = freeDenseIndices.Dequeue();

                    float layerScale = voxelScale * (1 << lc.layer);
                    uint safeRootIndex = 8;
                    int chunkIndexInLayer = idx % chunksPerLayer;
                    if (lc.layer == 0) safeRootIndex = (uint)(chunkIndexInLayer * 38000) + 8;
                    else if (lc.layer == 1) safeRootIndex = (uint)(chunksPerLayer * 38000) + (uint)(chunkIndexInLayer * 8192) + 8;
                    else if (lc.layer == 2) safeRootIndex = (uint)(chunksPerLayer * 38000) + (uint)(chunksPerLayer * 8192) + (uint)(chunkIndexInLayer * 256) + 8;

                    int siloCap = (lc.layer == 0) ? 38000 : (lc.layer == 1) ? 8192 : 256;
                    int targetLod = (lc.layer == 0) ? 1 : (lc.layer == 1) ? 2 : 4;

                    pendingJobsArray[dispatchesThisFrame] = new ChunkJobData {
                        worldPos = new Vector4(lc.coord.x * chunkSize, lc.coord.y * chunkSize, lc.coord.z * chunkSize, siloCap),
                        mapIndex = idx,
                        lodStep = targetLod,
                        layerScale = layerScale,
                        pad1 = safeRootIndex,
                        editStartIndex = 0,
                        editCount = 0,
                        pad2 = (int)denseIndex, 
                        pad3 = 0
                    };

                    ChunkData cd = chunkMapArray[idx];
                    cd.position = new Vector4(-99999f, -99999f, -99999f, 0f);
                    cd.rootNodeIndex = safeRootIndex; 
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
            int fCount = Mathf.Min(fList.Count, 5000);
            for(int i = 0; i < fCount; i++) persistentFeatureArray[i] = fList[i]; 

            var cList = VoxelEngine.WorldManager.Instance.cavernNodes;
            int cCount = Mathf.Min(cList.Count, 5000);
            for(int i = 0; i < cCount; i++) persistentCavernArray[i] = cList[i];

            var tList = VoxelEngine.WorldManager.Instance.tunnelSplines;
            int tCount = Mathf.Min(tList.Count, 5000);
            for(int i = 0; i < tCount; i++) persistentTunnelArray[i] = tList[i];

            jobQueueBuffers[ringIndex].SetData(persistentJobDataArray, 0, 0, dispatchesThisFrame);
            worldGenUtilityShader.SetInt("_JobCount", dispatchesThisFrame);
            worldGenUtilityShader.SetBuffer(kernel_clear, "_MacroGrid", macroGridBuffer);
            // FIX: Restored [ringIndex] so we stop colliding with the Raytracer!
            worldGenUtilityShader.SetBuffer(kernel_clear, "_ChunkMap", chunkMapBuffers[ringIndex]); 
            worldGenUtilityShader.SetBuffer(kernel_clear, "_JobQueue", jobQueueBuffers[ringIndex]);
            worldGenUtilityShader.Dispatch(kernel_clear, dispatchesThisFrame, 1, 1);

            // Replace the 'TerrainGenJob terrainJob = new TerrainGenJob {...}' block with this:
            if (currentArchitecture == VoxelArchitecture.DualState1Bit) {
                TerrainGenJob_1Bit terrainJob = new TerrainGenJob_1Bit {
                    jobQueue = persistentJobDataArray, features = persistentFeatureArray,
                    caverns = persistentCavernArray, tunnels = persistentTunnelArray,
                    denseChunkPool = cpuDenseChunkPool, macroMaskPool = cpuMacroMaskPool, 
                    worldRadiusXZ = VoxelEngine.WorldManager.Instance.WorldRadiusXZ,
                    worldSeed = VoxelEngine.WorldManager.Instance.worldSeed,
                    featureCount = persistentFeatureArray.Length, cavernCount = persistentCavernArray.Length
                };
                activeTerrainJobHandle = terrainJob.Schedule(dispatchesThisFrame, 1);
            } else {
                TerrainGenJob_32Bit terrainJob = new TerrainGenJob_32Bit {
                    jobQueue = persistentJobDataArray, features = persistentFeatureArray,
                    caverns = persistentCavernArray, tunnels = persistentTunnelArray,
                    denseChunkPool = cpuDenseChunkPool, macroMaskPool = cpuMacroMaskPool, 
                    worldRadiusXZ = VoxelEngine.WorldManager.Instance.WorldRadiusXZ,
                    worldSeed = VoxelEngine.WorldManager.Instance.worldSeed,
                    featureCount = persistentFeatureArray.Length, cavernCount = persistentCavernArray.Length
                };
                activeTerrainJobHandle = terrainJob.Schedule(dispatchesThisFrame, 1);
            }
            isTerrainJobRunning = true;
        }
    }
    
    public int GetMapIndex(int layer, Vector3Int coord) {
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        int mx = ((coord.x % sideXZ) + sideXZ) % sideXZ;
        int my = ((coord.y % sideY) + sideY) % sideY;
        int mz = ((coord.z % sideXZ) + sideXZ) % sideXZ;
        return (layer * chunksPerLayer) + (mx + (mz * sideXZ) + (my * sideXZ * sideXZ));
    }

    public bool IsReady() {
        return worldGenShader != null && worldGenUtilityShader != null && 
               chunkMapBuffers[0] != null && denseChunkPoolBuffer != null; // FIX: Check the new array
    }

    
    public void BindPhysicsData(ComputeShader cs, int kernel) {
        if (chunkMapBuffers[0] == null) return;
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
        if (macroGridBuffer != null) cs.SetBuffer(kernel, "_MacroGrid", macroGridBuffer);
        if (denseChunkPoolBuffer != null) cs.SetBuffer(kernel, "_DenseChunkPool", denseChunkPoolBuffer);
        
        // FIX: Reverted [0] back to [ringIndex] so it reads the Grand Swap!
        if (macroMaskPoolBuffers[ringIndex] != null) cs.SetBuffer(kernel, "_MacroMaskPool", macroMaskPoolBuffers[ringIndex]);
        cs.SetBuffer(kernel, "_ChunkMap", chunkMapBuffers[ringIndex]);
        
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetInt("_ClipmapLayers", clipmapLayers);
        cs.SetFloat("_VoxelScale", voxelScale);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetInt("_ChunkCount", chunksPerLayer);
    }


    public void BindMathData(ComputeShader cs, int kernel) {
        if (deltaMapBuffer != null) cs.SetBuffer(kernel, "_DeltaMapBuffer", deltaMapBuffer);

        if (WorldManager.Instance != null) {
            if (WorldManager.Instance.featureBuffer != null) cs.SetBuffer(kernel, "_FeatureAnchorBuffer", WorldManager.Instance.featureBuffer);
            if (WorldManager.Instance.cavernBuffer != null) cs.SetBuffer(kernel, "_CavernNodeBuffer", WorldManager.Instance.cavernBuffer);
            if (WorldManager.Instance.tunnelBuffer != null) cs.SetBuffer(kernel, "_TunnelSplineBuffer", WorldManager.Instance.tunnelBuffer);
            cs.SetInt("_FeatureCount", WorldManager.Instance.mapFeatures.Count);
            cs.SetInt("_CavernCount", WorldManager.Instance.cavernNodes.Count);
            cs.SetInt("_TunnelCount", WorldManager.Instance.tunnelSplines.Count);
        }
        
        cs.SetInt("_ChunkCount", chunksPerLayer);
        cs.SetInt("_ClipmapLayers", clipmapLayers);
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetFloat("_VoxelScale", voxelScale);
    }

    Vector3Int GetChunkCoord(Vector3 pos, float worldChunkSize) => 
        new Vector3Int(Mathf.FloorToInt(pos.x / worldChunkSize), Mathf.FloorToInt(pos.y / worldChunkSize), Mathf.FloorToInt(pos.z / worldChunkSize));

    void UpdateGPUBuffers() {
        Shader.SetGlobalVectorArray("_ClipmapCenters", shaderCenterChunks);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
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
                        chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
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
                        chunkMapBuffers[ringIndex].SetData(chunkMapArray, idx, idx, 1);
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

    void OnDisable() {
        if (isTerrainJobRunning) {
            activeTerrainJobHandle.Complete(); 
        }
        if (cpuDenseChunkPool.IsCreated) cpuDenseChunkPool.Dispose();
        if (cpuMacroMaskPool.IsCreated) cpuMacroMaskPool.Dispose(); 
        
        macroGridBuffer?.Release();
        deltaMapBuffer?.Release();
        materialBuffer?.Release();
        denseChunkPoolBuffer?.Release(); 
        crosshairBuffer?.Release();
        
        for (int i = 0; i < 2; i++) {
            chunkMapBuffers[i]?.Release();
            tempChunkUploadBuffers[i]?.Release();
            jobQueueBuffers[i]?.Release();
            macroMaskPoolBuffers[i]?.Release();
        }

        macroGridBuffer = null;
        materialBuffer = null;
        denseChunkPoolBuffer = null;
        deltaMapBuffer = null;
        crosshairBuffer = null;
    }

    void OnGUI() {
        GUI.Label(new Rect(20, 20, 200, 40), $"FPS: {1.0f / deltaTime:0.}", new GUIStyle { fontSize = 30, normal = { textColor = Color.yellow } });
    }
}